using UnityEngine;

namespace Laubrary.Ticker.Samples
{
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
    }
}