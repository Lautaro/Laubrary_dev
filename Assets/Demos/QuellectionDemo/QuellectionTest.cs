using Laubrary.Quellection;
using UnityEngine;

namespace Laubrary.Quellection.Samples
{
    public class QuellectionTest : MonoBehaviour
    {
        void Start()
        {
            for (int i = 0; i < 10; i++)
            {
                var superText = new SuperText() { text = i.ToString() };
            }
        }

        public void Kill()
        {
            SuperText.qullection.Destroy();
            DestroyImmediate(this);
        }
    }

    public class SuperText
    {
        public string text;
        public static Qullection<SuperText> qullection = new Qullection<SuperText>("PrintText");

        public void PrintText()
        {
            Debug.Log(Time.time + " - My text:" + text);
        }

        public SuperText()
        {
            qullection.Add(this);
        }
    }
}