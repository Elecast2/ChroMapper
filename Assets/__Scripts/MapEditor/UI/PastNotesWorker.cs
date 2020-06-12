﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class PastNotesWorker : MonoBehaviour
{
    [SerializeField] private AudioTimeSyncController atsc;
    [SerializeField] private NotesContainer notesContainer;
    [SerializeField] private GameObject gridNotePrefab;
    [SerializeField] private BeatmapObjectCallbackController callbackController;
    [SerializeField] private NoteAppearanceSO noteAppearance;

    private Transform notes;
    private Canvas _canvas;

    private readonly float _tolerance = 0.25f;
    private readonly float _gridSize = 25f;
    private Dictionary<int, BeatmapNote> lastByType = new Dictionary<int, BeatmapNote>(); //Used to improve performance
    private float scale = 0;

    private Dictionary<int, Dictionary<GameObject, Image>> InstantiatedNotes = new Dictionary<int, Dictionary<GameObject, Image>>();

    private void Start()
    {
        _canvas = GetComponent<Canvas>();
        scale = Settings.Instance.PastNotesGridScale;
        _canvas.enabled = scale != 0f;
        transform.localScale = Vector3.one * (scale + 0.25f);
        if (scale == 0f) return;

        callbackController.NotePassedThreshold += NotePassedThreshold;
        atsc.OnTimeChanged += OnTimeChanged;

        notes = transform.GetChild(0);
        Settings.NotifyBySettingName("PastNotesGridScale", UpdatePastNotesGridScale);
    }

    private void UpdatePastNotesGridScale(object obj)
    {
        scale = (float)obj;
        _canvas.enabled = scale != 0f;
        transform.localScale = Vector3.one * (scale + 0.25f);
    }

    private void OnDestroy()
    {
        callbackController.NotePassedThreshold -= NotePassedThreshold;
        atsc.OnTimeChanged -= OnTimeChanged;
        Settings.ClearSettingNotifications("PastNotesGridScale");
    }

    private bool _firstLoad = true;

    private void OnTimeChanged()
    {
        if (atsc.IsPlaying) return;
        BeatmapObject lastRed = notesContainer.LoadedObjects.LastOrDefault(x => x._time < atsc.CurrentBeat &&
            (x as BeatmapNote)._type == BeatmapNote.NOTE_TYPE_A);

        BeatmapObject lastBlue = notesContainer.LoadedObjects.LastOrDefault(x => x._time < atsc.CurrentBeat &&
            (x as BeatmapNote)._type == BeatmapNote.NOTE_TYPE_B);


        if (lastRed != null)
        {
            NotePassedThreshold(false, 0, lastRed);
        }
        if (lastBlue != null)
        {
            NotePassedThreshold(false, 0, lastBlue);
        }
    }

    private void NotePassedThreshold(bool natural, int id, BeatmapObject obj)
    {
        BeatmapNote note = obj as BeatmapNote;

        if (!InstantiatedNotes.ContainsKey(note._type))
        {
            InstantiatedNotes.Add(note._type, new Dictionary<GameObject, Image>());
        }

        if (lastByType.TryGetValue(note._type, out BeatmapNote lastInTime) && lastInTime != obj)
        {
            foreach (KeyValuePair<GameObject, Image> child in InstantiatedNotes[note._type]) child.Key.SetActive(false);
        }

        if (note._type == BeatmapNote.NOTE_TYPE_BOMB) return;

        float gridPosX = note._lineIndex, gridPosY = note._lineLayer;

        if (note._customData?.HasKey("_position") ?? false)
        {
            Vector2 pos = note._customData["_position"];
            gridPosX = pos.x + 1.5f;
            gridPosY = pos.y - 0.5f;
        }
        else //mapping extensions ew
        {
            if (gridPosX >= 1000) gridPosX = gridPosX / 1000 - 1f;
            else if (gridPosX <= -1000f) gridPosX = gridPosX / 1000f + 1f;

            if (gridPosY >= 1000) gridPosY = gridPosY / 1000f - 1f;
            else if (gridPosY <= -1000f) gridPosY = gridPosY / 1000f + 1f;
        }

        GameObject g; //Instead of instantiating new objects every frame (Bad on performance), we are instead using a pooled system to use
        Image img; //Already existing notes, and only create ones we need.
        if (InstantiatedNotes[note._type].Any(x => !x.Key.activeSelf))
        {
            g = InstantiatedNotes[note._type].First(x => !x.Key.activeSelf).Key;
            img = InstantiatedNotes[note._type][g];
            g.SetActive(true);
            g.transform.SetSiblingIndex(g.transform.parent.childCount);
            foreach (Transform child in g.transform) child.gameObject.SetActive(true);
        }
        else
        {
            g = Instantiate(gridNotePrefab, notes.transform, true);
            img = g.GetComponent<Image>();
            InstantiatedNotes[note._type].Add(g, img);
        }

        var transform1 = img.transform;
        transform1.localPosition = new Vector3(_gridSize * gridPosX, _gridSize * gridPosY, 1);
        float sc = scale / 10f + .06f;
        transform1.localScale = new Vector3(sc, sc); //I have to do this because the UI scaling is weird

        //transform1.rotation = o.transform.rotation; //This code breaks when using 360 maps; use local rotation instead.
        transform1.localEulerAngles = Vector3.forward * BeatmapNoteContainer.Directionalize(note).z; //Sets the rotation of the image to match the same rotation as the block
        img.color = note._type == BeatmapNote.NOTE_TYPE_A ? noteAppearance.RedInstance.color : noteAppearance.BlueInstance.color;

        bool dotEnabled = note._cutDirection == BeatmapNote.NOTE_CUT_DIRECTION_ANY; //Checks to see if the Dot is visible on the block

        if (dotEnabled) g.transform.GetChild(0).gameObject.SetActive(false);
        else g.transform.GetChild(1).gameObject.SetActive(false);
        img.enabled = true;

        if (!lastByType.ContainsKey(note._type))
        {
            lastByType.Add(note._type, note);
        }
        else
        {
            lastByType[note._type] = note;
        }
    }
}
