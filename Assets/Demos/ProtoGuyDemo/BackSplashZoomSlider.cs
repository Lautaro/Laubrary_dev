using UnityEngine;
using Laubrary.BackSplash;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// DEBUG-ONLY (T-0251), plain native game code -- NOT a Laubrary tool. A small OnGUI slider so Lautaro can
    /// tune the demo's BackSplash background zoom by eye while playing, same simple on-screen-button approach
    /// T-0247's <see cref="FireDirectionVisualizer"/> already uses on this same "Demo Controls" object.
    /// </summary>
    public class BackSplashZoomSlider : MonoBehaviour
    {
        [Tooltip("Turn the on-screen zoom slider off without removing the component.")]
        public bool enableSlider = true;
        public BackSplashBackdrop backdrop;
        public float minZoom = 0.2f;
        public float maxZoom = 5f;

        void OnGUI()
        {
            if (!enableSlider || backdrop == null || backdrop.backSplash == null) return;

            const float w = 260f, h = 20f, pad = 8f;
            float y = pad + 34f; // sit below FireDirectionVisualizer's "Clear Fire Lines" button
            GUI.Label(new Rect(pad, y, w, h), $"Background Zoom: {backdrop.backSplash.imageZoom:0.00}");
            float newZoom = GUI.HorizontalSlider(new Rect(pad, y + h, w, h), backdrop.backSplash.imageZoom, minZoom, maxZoom);
            if (!Mathf.Approximately(newZoom, backdrop.backSplash.imageZoom))
            {
                backdrop.backSplash.imageZoom = newZoom;
                backdrop.Apply();
            }
        }
    }
}
