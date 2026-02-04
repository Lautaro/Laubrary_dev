using Laubrary.LaubraryTicker;
using UnityEngine;

/// <summary>
/// A custom type that uses Ticker should have a OnDestroy method to remove itself from OnUpdate. Otherwise the reference keeps the instance from being garbage collected
/// </summary>
public class TickableTestType
{
    public static int count = 0;
    public string info;
    public TickableTestType()
    {
        Ticker.OnUpdate += Update;
        count++;
        Debug.LogWarning("IUUUIUUUIUU " + count);
    }

    public void OnDestroy()
    {
        Ticker.OnUpdate -= Update;
    }

    public void Update()
    { 
        info = Input.mousePosition.ToString();
        Debug.Log(info);
    }
}
