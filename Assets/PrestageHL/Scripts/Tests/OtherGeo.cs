﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OtherGeo : PRCube
{

    void Awake()
    {
        CubeMesh = GetComponent<MeshFilter>().mesh;
        GetComponent<MeshCollider>().sharedMesh = CubeMesh;
    }

    void Start () {
		
	}
	
	void Update () {
		
	}
}
