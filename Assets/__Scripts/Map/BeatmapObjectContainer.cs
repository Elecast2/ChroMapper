﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class BeatmapObjectContainer : MonoBehaviour
{
    public static Action<BeatmapObjectContainer, bool, string> FlaggedForDeletionEvent;

    private static readonly int Outline = Shader.PropertyToID("_Outline");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");

    public bool OutlineVisible { get => SelectionMaterials.FirstOrDefault()?.GetFloat(Outline) != 0;
        set {
            foreach (Material SelectionMaterial in SelectionMaterials)
            {
                if (!SelectionMaterial.HasProperty(OutlineColor)) return;
                SelectionMaterial.SetFloat(Outline, value ? 0.03f : 0);
                Color c = SelectionMaterial.GetColor(OutlineColor);
                SelectionMaterial.SetColor(OutlineColor, new Color(c.r, c.g, c.b, value ? 1 : 0));
            }
        }
    }

    public Track AssignedTrack { get; private set; } = null;

    [SerializeField]
    public abstract BeatmapObject objectData { get; set; }

    public abstract void UpdateGridPosition();

    protected int chunkID;
    public int ChunkID { get => chunkID; }
    public IEnumerable<Material> ModelMaterials = new Material[] { };
    public IEnumerable<Material> SelectionMaterials = new Material[] { };

    [SerializeField] protected BoxCollider boxCollider;
    internal bool SelectionStateChanged;

    protected virtual void Start()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            ModelMaterials = ModelMaterials.Append(renderer.materials.First());
            SelectionMaterials = SelectionMaterials.Append(renderer.materials.Last());
        }
        OutlineVisible = false;
    }

    internal virtual void SafeSetActive(bool active)
    {
        if (active != gameObject.activeSelf)
        {
            gameObject.SetActive(active);
            if (boxCollider != null) boxCollider.enabled = active;
        }
    }

    internal void SafeSetBoxCollider(bool con)
    {
        if (boxCollider == null) return;
        if (con != boxCollider.isTrigger) boxCollider.isTrigger = con;
    }

    internal void SetOutlineColor(Color color, bool automaticallyShowOutline = true)
    {
        if (automaticallyShowOutline) OutlineVisible = true;
        foreach (Material SelectionMaterial in SelectionMaterials)
        {
            SelectionMaterial.SetColor(OutlineColor, color);
        }
    }

    public void AssignTrack(Track track)
    {
        AssignedTrack = track;
        chunkID = (int)Math.Round(objectData._time / (double)BeatmapObjectContainerCollection.ChunkSize,
                 MidpointRounding.AwayFromZero);
    }
}
