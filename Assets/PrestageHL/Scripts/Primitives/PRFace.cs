﻿using System.Collections;
using System.Collections.Generic;
using HoloToolkit.Unity.InputModule;
using PRGeoClasses;
using UnityEngine;

public class PRFace : MonoBehaviour, IFocusable
{

    private PRGeo PARENT_CUBE
    {
        get { return transform.parent.parent.GetComponent<PRGeo>(); }
    }
    private Mesh CUBE_MESH
    {
        get { return PARENT_CUBE.GeoMesh; }
    }
    public PRFaceHolder FaceHolder;
    private Vector3 _savePos;
    private Vector3[] _meshVertices;
    private Material _savedThisMat;
    private Mesh FACE_MESH
    {
        get { return GetComponent<MeshFilter>().mesh; }
    }

    public bool Active;
    public bool FocusActive = false;
    public float OffsetFromFace = 0.01f;


    #region Unity
    protected virtual void Awake()
    {
        MeshCollider mC = gameObject.AddComponent(typeof(MeshCollider)) as MeshCollider;
    }

    protected virtual void Start()
    {
        _savedThisMat = GetComponent<MeshRenderer>().material;
        //Debug.Log("MSTopo: " + FaceHolder.MeshTopo);
    }

    protected virtual void Update()
    {
        MoveFace();
        UpdateHighlightStatus();
    }

    void OnEnable()
    {
        EventManager.AirTapDown += OnInputDownLocal;
        EventManager.AirTapUp += OnInputUpLocal;
    }

    void OnDisable()
    {
        EventManager.AirTapDown -= OnInputDownLocal;
        EventManager.AirTapUp -= OnInputUpLocal;

        FaceMeshDisplay(true);
        // Unhighlight the faces, so they are not highlighted next time they are turned on.
        Active = false;
        UnhighlightFace();
    }
    #endregion //Unity

    #region Events
    public void OnFocusEnter()
    {
        if (!Active)
        {
            FocusActive = true;
        }
    }

    public void OnFocusExit()
    {
        if (!Active)
        {
            FocusActive = false;
        }
    }

    public void OnInputDownLocal()
    {
        if (UpdateActiveStatus())
        {
            _savePos = transform.localPosition;
            // Save the face holder.
            FaceHolder.savedFH = new PRFaceHolder(FaceHolder);
            _meshVertices = CUBE_MESH.vertices;
        }
        else
        {
            FaceHolder.UpdateInactiveFaceInfo(FaceHolder.Mesh);
            if (Manager.Instance.GET_COLLIDER_TAG == "PRFace" ||
                Manager.Instance.IsGizmoHit()) FaceMeshDisplay(false);
        }
    }

    private void OnInputUpLocal()
    {

        if (Active)
        {
            _savePos = Vector3.zero;
            FaceHolder.savedFH = null;
            _meshVertices = null;
            FocusActive = false;
        }
        else
        {
            FaceHolder.UpdateInactiveFaceInfo(CUBE_MESH);
            UpdateCollider();
        }
        UpdateActiveStatus();
        // Display all the faces.
        FaceMeshDisplay(true);
    }

    private void HighlightFace()
    {
        Material highlight = new Material(Manager.Instance.HighlightColliderMat);
        GetComponent<MeshRenderer>().material = highlight;
    }

    private void UnhighlightFace()
    {
        GetComponent<MeshRenderer>().material = _savedThisMat;
    }

    protected void FaceMeshDisplay(bool state)
    {
        GetComponent<MeshRenderer>().enabled = state;
        GetComponent<Collider>().enabled = state;
    }
    #endregion //Events

    #region Move&Snap

    private void MoveFace()
    {
        if (Active && _meshVertices != null)
        {
            // Move the Face holder verts
            FaceHolder.UpdateFace(transform.localPosition - _savePos);
            // Move the overlaping verts as this edge.
            for (int i = 0; i < FaceHolder.SameV0Index.Count; i++)
            {
                _meshVertices[FaceHolder.SameV0Index[i]] = FaceHolder.F_VERTICES[0];
            }
            for (int i = 0; i < FaceHolder.SameV1Index.Count; i++)
            {
                _meshVertices[FaceHolder.SameV1Index[i]] = FaceHolder.F_VERTICES[1];
            }
            for (int i = 0; i < FaceHolder.SameV2Index.Count; i++)
            {
                _meshVertices[FaceHolder.SameV2Index[i]] = FaceHolder.F_VERTICES[2];
            }
            // If the face is a quad transform the 4th vertex too.
            if (FaceHolder.MeshTopo == MeshTopology.Quads)
            {
                for (int i = 0; i < FaceHolder.SameV3Index.Count; i++)
                {
                    _meshVertices[FaceHolder.SameV3Index[i]] = FaceHolder.F_VERTICES[3];
                }
            }
            CUBE_MESH.vertices = _meshVertices;
            CUBE_MESH.RecalculateBounds();
        }
    }

    #endregion //Move&Snap

    public Mesh GenerateMeshCollider()
    {
        Mesh mesh = new Mesh();
        // Assign verts
        mesh.vertices = FaceHolder.F_VERTICES;
        // Create Mesh either as Quad or Triangle.
        if (FaceHolder.MeshTopo == MeshTopology.Quads)
        {
            int[] quad = new int[4] { 0, 1, 2, 3 };
            mesh.SetIndices(quad, MeshTopology.Quads, 0);
        }
        else if (FaceHolder.MeshTopo == MeshTopology.Triangles)
        {
            int[] quad = new int[3] { 0, 1, 2 };
            mesh.SetIndices(quad, MeshTopology.Triangles, 0);
        }

        // Recalculate all
        Vector3[] newVerts = mesh.vertices;
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            newVerts[i] += (FaceHolder.NORMALS[i] * OffsetFromFace) - transform.localPosition;
        }

        mesh.vertices = newVerts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        return mesh;
    }

    #region UpdateElements
    /// <summary>
    /// Update the collider of the face at AirtapUp to match the modified face.
    /// </summary>
    /// <param name="offset">Offset distance of the face from its edges</param>
    public void UpdateCollider()
    {
        // Update the face transform location, so gizmo is displayed in the center.
        transform.localPosition = FaceHolder.CENTER;
        // 1. Get the verts of the face from cube.mesh
        Vector3[] vertColl = new Vector3[FaceHolder.F_VERTICES.Length];
        for (int i = 0; i < vertColl.Length; i++)
        {
            vertColl[i] = CUBE_MESH.vertices[FaceHolder.VertexIndices[i]];
            vertColl[i] += (FaceHolder.NORMALS[i].normalized * OffsetFromFace) - transform.localPosition;
        }
        // 2. Update the face verts
        FACE_MESH.vertices = vertColl;
        // 3. Recalculate all
        FACE_MESH.RecalculateBounds();
        // 4. Update mesh collider.
        GetComponent<MeshCollider>().sharedMesh = FACE_MESH;
    }

    /// <summary>
    /// Get the selected object form gizmo and check if it is this.
    /// </summary>
    private bool UpdateActiveStatus()
    {
        if (Manager.Instance.GIZMO.targetRootsOrdered.Count > 0)
        {

            if (Manager.Instance.GIZMO.targetRootsOrdered[0].name == this.name)
            {
                Active = true;
                return true;
            }
            else
            {
                Active = false;
                return false;
            }
        }
        else
        {
            Active = false;
            return false;
        }
    }

    private void UpdateHighlightStatus()
    {
        // Change Edge material to activeMaterial.
        if (Active)
        {
            GetComponent<MeshRenderer>().material = Manager.Instance.ActiveColliderMat;
        }

        // Unhighlight all faces when they are inactive and Gizmo.nearAxis is not None.
        if (!Active && Manager.Instance.GET_COLLIDER_LAYER == "Gizmo")
        {
            UnhighlightFace();
        }
        if (FocusActive)
        {
            if (Manager.Instance.GET_COLLIDER_LAYER != "Gizmo")
            {
                HighlightFace();
            }
        }
        else if (!Active)
        {
            UnhighlightFace();
        }
    }
    #endregion //UpdateElements
}
