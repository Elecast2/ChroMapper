﻿using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public abstract class PlacementController<BO, BOC, BOCC> : MonoBehaviour, CMInput.IPlacementControllersActions, CMInput.ICancelPlacementActions where BO : BeatmapObject where BOC : BeatmapObjectContainer where BOCC : BeatmapObjectContainerCollection
{
    [SerializeField] private GameObject objectContainerPrefab;
    [SerializeField] private BO objectData;
    [SerializeField] internal BOCC objectContainerCollection;
    [SerializeField] protected Transform parentTrack;
    [SerializeField] protected Transform interfaceGridParent;
    [SerializeField] protected bool AssignTo360Tracks;
    [SerializeField] private BeatmapObject.Type objectDataType;
    [SerializeField] private bool startingActiveState;
    [SerializeField] protected AudioTimeSyncController atsc;
    [SerializeField] private CustomStandaloneInputModule customStandaloneInputModule;
    [SerializeField] protected TracksManager tracksManager;
    [SerializeField] protected RotationCallbackController gridRotation;
    [SerializeField] protected GridChild gridChild;

    [HideInInspector] protected virtual bool DestroyBoxCollider { get; set; } = true;

    [HideInInspector] protected virtual bool CanClickAndDrag { get; set; } = true;

    [HideInInspector] protected virtual float RoundedTime { get; private set; } = 0;

    protected bool isDraggingObject = false;
    protected Vector2 mousePosition;
    protected bool isOnPlacement = false;
    protected Camera mainCamera = null;

    protected BOC draggedObjectContainer = null;
    private BO draggedObjectData = null;
    private BO originalQueued = null;
    private BO originalDraggedObjectData = null;

    private bool applicationFocus = false;
    private bool applicationFocusChanged = false;

    public virtual bool IsValid { get
        {
            return !KeybindsController.AnyCriticalKeys && !Input.GetMouseButton(1) && !SongTimelineController.IsHovering && IsActive && 
                !BoxSelectionPlacementController.IsSelecting && applicationFocus && !SceneTransitionManager.IsLoading && KeybindsController.IsMouseInWindow &&
                !DeleteToolController.IsActive;
        } }

    public bool IsActive = false;

    internal BO queuedData; //Data that is not yet applied to the BeatmapObjectContainer.
    internal BOC instantiatedContainer;

    internal virtual void Start()
    {
        Physics.autoSyncTransforms = false; //Causes performance degradation, do not want.
        queuedData = GenerateOriginalData();
        IsActive = startingActiveState;
        mainCamera = Camera.main;
    }

    protected void CalculateTimes(RaycastHit hit, out Vector3 roundedHit, out float roundedTime)
    {
        roundedHit = parentTrack.InverseTransformPoint(hit.point);
        float realTime = roundedHit.z / EditorScaleController.EditorScale;
        roundedTime = atsc.FindRoundedBeatTime(realTime) + atsc.offsetBeat;
        float roundedCurrent = atsc.FindRoundedBeatTime(atsc.CurrentBeat);
        float offsetTime = hit.collider.gameObject.name.Contains("Interface") ? 0 : atsc.CurrentBeat - roundedCurrent;
        if (!atsc.IsPlaying) roundedTime += offsetTime;
    }

    void ColliderExit()
    {
        if (instantiatedContainer != null) instantiatedContainer.gameObject.SetActive(false);
    }

    protected void RefreshVisuals()
    {
        instantiatedContainer = Instantiate(objectContainerPrefab,
            parentTrack).GetComponent(typeof(BOC)) as BOC;
        instantiatedContainer.Setup();
        instantiatedContainer.OutlineVisible = false;
        if (instantiatedContainer.GetComponent<BoxCollider>() != null && DestroyBoxCollider)
            Destroy(instantiatedContainer.GetComponent<BoxCollider>());
        instantiatedContainer.name = $"Hover {objectDataType}";
    }

    private void Update360Tracks()
    {
        if (!AssignTo360Tracks) return;
        TracksManager manager = objectContainerCollection.GetComponent<TracksManager>();
        if (manager == null)
            Debug.LogWarning("Could not find an attached TracksManager.");
        else
        {
            Track track = manager.GetTrackAtTime(RoundedTime);
            if (track != null)
            {
                Vector3 localPos = instantiatedContainer.transform.localPosition;
                parentTrack = track.ObjectParentTransform;
                instantiatedContainer.transform.SetParent(track.ObjectParentTransform, false);
                instantiatedContainer.transform.localPosition = localPos;
                instantiatedContainer.transform.localEulerAngles = new Vector3(instantiatedContainer.transform.localEulerAngles.x,
                    0, instantiatedContainer.transform.localEulerAngles.z);
            }
        }
    }

    internal virtual void ApplyToMap()
    {
        objectData = BeatmapObject.GenerateCopy(queuedData);
        objectData._time = RoundedTime;
        objectContainerCollection.SpawnObject(objectData, out IEnumerable<BeatmapObject> conflicting);
        BeatmapActionContainer.AddAction(GenerateAction(objectData, conflicting));
        queuedData = BeatmapObject.GenerateCopy(queuedData);
    }

    public abstract BO GenerateOriginalData();
    public abstract BeatmapAction GenerateAction(BeatmapObject spawned, IEnumerable<BeatmapObject> conflicting);
    public abstract void OnPhysicsRaycast(RaycastHit hit, Vector3 transformedPoint);

    public virtual void AfterDraggedObjectDataChanged() { }

    public virtual void ClickAndDragFinished() { }

    public virtual void CancelPlacement() { }

    public virtual int PlacementXMin => 0;

    public virtual int PlacementXMax => GridOrderController.GetSizeForOrder(gridChild.Order);

    public abstract void TransferQueuedToDraggedObject(ref BO dragged, BO queued);

    public void OnPlaceObject(InputAction.CallbackContext context)
    {
        if (customStandaloneInputModule.IsPointerOverGameObject<GraphicRaycaster>(-1, true)) return;
        if (context.performed && !isDraggingObject && isOnPlacement && instantiatedContainer != null && IsValid
            && !PersistentUI.Instance.DialogBox_IsEnabled &&
            queuedData?._time >= 0 && !applicationFocusChanged) ApplyToMap();
    }

    public void OnInitiateClickandDrag(InputAction.CallbackContext context)
    {
        if (context.performed && CanClickAndDrag)
        {
            Ray dragRay = mainCamera.ScreenPointToRay(mousePosition);
            if (Physics.Raycast(dragRay, out RaycastHit dragHit, 999f, 1 << 9))
            {
                BeatmapObjectContainer con = dragHit.transform.gameObject.GetComponent<BeatmapObjectContainer>();
                if (con is null || !(con is BOC)) return; //Filter out null objects and objects that aren't what we're targetting.
                isDraggingObject = true;
                draggedObjectData = con.objectData as BO;
                originalQueued = BeatmapObject.GenerateCopy(queuedData);
                originalDraggedObjectData = BeatmapObject.GenerateCopy(con.objectData) as BO;
                queuedData = BeatmapObject.GenerateCopy(draggedObjectData);
                draggedObjectContainer = con as BOC;
            }
        }
        else if (context.canceled && isDraggingObject && instantiatedContainer != null)
        {
            //First, find and delete anything that's overlapping our dragged object.
            objectContainerCollection.RemoveConflictingObjects(new[] { draggedObjectData });
            isDraggingObject = false;
            queuedData = BeatmapObject.GenerateCopy(originalQueued);
            BeatmapActionContainer.AddAction(new BeatmapObjectModifiedAction(draggedObjectData, originalDraggedObjectData, "Modified via alt-click and drag."));
            ClickAndDragFinished();
        }
    }

    protected virtual void Update()
    {
        if (Application.isFocused != applicationFocus)
        {
            applicationFocus = Application.isFocused;
            applicationFocusChanged = true;
            return;
        }
        else if (applicationFocusChanged)
        {
            applicationFocusChanged = false;
        }
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);
        RaycastHit[] BeatmapObjectsHit = Physics.RaycastAll(ray, 999f);
        isOnPlacement = false;
        foreach (RaycastHit objectHit in BeatmapObjectsHit)
        {
            if (!isOnPlacement && objectHit.transform.GetComponentsInParent(GetType()).Any())
                isOnPlacement = true;
            BeatmapObjectContainer con = objectHit.transform.gameObject.GetComponent<BeatmapObjectContainer>();
            if (con == null || con == draggedObjectContainer) continue;
            con.SafeSetBoxCollider(KeybindsController.AnyCriticalKeys || Input.GetMouseButtonDown(2));
        }
        if (PauseManager.IsPaused) return;
        if ((!IsValid && (!isDraggingObject || !IsActive)) || !isOnPlacement)
        {
            ColliderExit();
            return;
        }
        if (instantiatedContainer == null) RefreshVisuals();
        if (!instantiatedContainer.gameObject.activeSelf) instantiatedContainer.gameObject.SetActive(true);
        objectData = queuedData;
        if (Physics.Raycast(ray, out RaycastHit hit, 999f, 1 << 11))
        {
            Transform hitTransform = hit.transform; //Make a reference to the transform instead of calling hit.transform a lot
            if (!hitTransform.IsChildOf(transform) || hitTransform.GetComponent<PlacementMessageSender>() == null ||
                PersistentUI.Instance.DialogBox_IsEnabled)
            {
                ColliderExit();
                return;
            }
            if (customStandaloneInputModule.IsPointerOverGameObject<GraphicRaycaster>(-1, true)) return;
            if (BeatmapObjectContainerCollection.TrackFilterID != null && !objectContainerCollection.IgnoreTrackFilter)
            {
                if (queuedData._customData == null) queuedData._customData = new SimpleJSON.JSONObject();
                queuedData._customData["track"] = BeatmapObjectContainerCollection.TrackFilterID;
            }
            else queuedData?._customData?.Remove("track");
            CalculateTimes(hit, out Vector3 roundedHit, out float roundedTime);
            RoundedTime = roundedTime;
            float placementZ = RoundedTime * EditorScaleController.EditorScale;
            Update360Tracks();

            //this mess of localposition and position assignments are to align the shits up with the grid
            //and to hopefully not cause IndexOutOfRangeExceptions
            instantiatedContainer.transform.localPosition = parentTrack.InverseTransformPoint(hit.point); //fuck transformedpoint we're doing it ourselves

            Vector3 localMax = parentTrack.InverseTransformPoint(hit.collider.bounds.max);
            Vector3 localMin = parentTrack.InverseTransformPoint(hit.collider.bounds.min);
            float farRightPoint = PlacementXMax;
            float farLeftPoint = PlacementXMin;
            float farTopPoint = localMax.y;
            float farBottomPoint = localMin.y;

            roundedHit = new Vector3(Mathf.Ceil(roundedHit.x), Mathf.Ceil(roundedHit.y), placementZ);
            instantiatedContainer.transform.localPosition = roundedHit - new Vector3(0.5f, 1f, 0);
            float x = instantiatedContainer.transform.localPosition.x; //Clamp values to prevent exceptions
            float y = instantiatedContainer.transform.localPosition.y;
            instantiatedContainer.transform.localPosition = new Vector3(
                Mathf.Clamp(x, farLeftPoint + 0.5f, farRightPoint - 0.5f),
                Mathf.Round(Mathf.Clamp(y, farBottomPoint, farTopPoint - 1)) + 0.5f,
                instantiatedContainer.transform.localPosition.z);

            if (!hit.collider.gameObject.name.Contains("Grid X"))
            {
                instantiatedContainer.transform.localPosition += new Vector3(0, 1f, 0);
            }

            OnPhysicsRaycast(hit, roundedHit);
            queuedData._time = RoundedTime;
            if (isDraggingObject && queuedData != null)
            {
                TransferQueuedToDraggedObject(ref draggedObjectData, BeatmapObject.GenerateCopy(queuedData));
                draggedObjectContainer.objectData = draggedObjectData;
                draggedObjectContainer.objectData._time = placementZ / EditorScaleController.EditorScale;
                if (draggedObjectContainer != null)
                {
                    draggedObjectContainer?.UpdateGridPosition();
                }
                AfterDraggedObjectDataChanged();
            }
        }
        else
        {
            ColliderExit();
            return;
        }
    }

    public virtual void OnMousePositionUpdate(InputAction.CallbackContext context)
    {
        mousePosition = Mouse.current.position.ReadValue();
    }

    public void OnCancelPlacement(InputAction.CallbackContext context)
    {
        if (context.performed)
            CancelPlacement();
    }
}
