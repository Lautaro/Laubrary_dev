using Laubrary.Ticker;
using System;
using UnityEngine;

[Serializable]
public class TickableTestType
{
    public string info;
    public TickableTestType()
    {
        Ticker.OnUpdate += Update;
    }

    public void Update()
    { 
        info = Input.mousePosition.ToString();
    }
}
