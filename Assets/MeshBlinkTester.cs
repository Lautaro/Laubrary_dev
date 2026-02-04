using Laubrary.VisualDebug3D;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshBlinkTester : MonoBehaviour
{
    public GameObject blinkMarker;
    [Range(1, 100)] public int countBlinks;
    [Range(0.01f, 0.5f)] public float durationBlinks;
    public Color color;
    public bool useUnlit;

    [Button]
    public void BlinkMesh()
    {
        MeshBlink.Create(blinkMarker, color,durationBlinks, countBlinks, useUnlit);
    }
}
