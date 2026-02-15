using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Ticker.Samples
{
    public class TickableTestType
    {
        public static int count = 0;
        public string info;

        public TickableTestType()
        {
            global::Laubrary.LaubraryTicker.Ticker.OnUpdate += Update;
            count++;
            Debug.LogWarning("IUUUIUUUIUU " + count);
        }

        public void OnDestroy()
        {
            global::Laubrary.LaubraryTicker.Ticker.OnUpdate -= Update;
        }

        public void Update()
        {
            if (Mouse.current != null)
            {
                info = Mouse.current.position.ReadValue().ToString();
                Debug.Log(info);
            }
        }
    }
}