using Laubrary.Cookbook2D;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PolygonRandomPointTester : MonoBehaviour
{
    PolygonCollider2D polly;
    public GameObject player;
    

    void Start()
    {
        polly = GetComponent<PolygonCollider2D>();
        
    }

    [Button]

    private void MovePlayerInsidePolygon()
    {
        if (polly == null)
            polly = GetComponent<PolygonCollider2D>();
        player.transform.position = polly.GetRandomPoint();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
