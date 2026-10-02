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

            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            labelStyle.normal.textColor = Color.white;

            const float w = 360f, h = 30f, pad = 8f;
            float y = pad + 210f; // sit below ChunksUCDeathSwitcher's key legend, now drawn at a larger font
            GUI.Label(new Rect(pad, y, w, h), $"Background Zoom: {backdrop.backSplash.imageZoom:0.00}", labelStyle);
            float newZoom = GUI.HorizontalSlider(new Rect(pad, y + h + 4f, w, h), backdrop.backSplash.imageZoom, minZoom, maxZoom);
            if (!Mathf.Approximately(newZoom, backdrop.backSplash.imageZoom))
            {
                backdrop.backSplash.imageZoom = newZoom;
                backdrop.Apply();
            }
        }
    }
}
