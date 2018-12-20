﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParentEdge : MonoBehaviour {

    public GameObject[] EDGE_COLL_GO
    {
        get
        {
            GameObject[] coll = new GameObject[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
            {
                coll[i] = transform.GetChild(i).gameObject;
            }
            return coll;
        }
    }
    public PREdge[] EDGE_COLL_COMP
    {
        get
        {
            PREdge[] coll = new PREdge[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
            {
                coll[i] = transform.GetChild(i).GetComponent<PREdge>();
            }
            return coll;
        }
    }

    // Use this for initialization
    void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		
	}
}