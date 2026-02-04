using Sirenix.OdinInspector;
using System;
using UnityEngine;

public class TickerTester : MonoBehaviour
{
    private TickableTestType MyTickerType;

    private void Start()
    {
        MyTickerType = new();
    }

    private void OnDestroy()
    {
        MyTickerType.OnDestroy();
    }

    [Button]
    void Kill()
    {
        DestroyImmediate(gameObject);
    }
}
