using Laubrary.VisualDebug3D;
using UnityEngine;

namespace Laubrary.VisualDebug.Samples
{
    public class Blink3DTester : MonoBehaviour
    {
        public Transform blinkMarker;
        [Range(1, 100)] public int countBlinks;
        [Range(0.01f, 0.5f)] public float durationBlinks;
        [Range(0.1f, 3f)] public float scale;
        public Color colorBlinks;

        public void Blink()
        {
            var meshrenderer = blinkMarker.GetComponent<MeshRenderer>();
            BubbleBlink.Blink(meshrenderer, colorBlinks, durationBlinks, countBlinks, false, scale);
        }

        public void BlinkUnlit()
        {
            var meshrenderer = blinkMarker.GetComponent<MeshRenderer>();
            BubbleBlink.Blink(meshrenderer, colorBlinks, durationBlinks, countBlinks, true, scale);
        }
    }
}