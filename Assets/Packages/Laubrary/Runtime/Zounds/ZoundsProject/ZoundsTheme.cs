using UnityEngine;

namespace Laubrary.Zounds {
    /// <summary>
    /// Represents a visual theme for the Zounds editor.
    /// This is decoupled from the main project file to allow for skins and sharing.
    /// </summary>
    [System.Serializable]
    public class ZoundsTheme {
        public Color playerHeadColor = new Color(0.1f, 0.1f, 0.9f, 0.75f);
        public float playerHeadThickness = 1.5f;
        public Color klipWaveformBGColor = new Color32(252, 192, 7, 255);
        public Color zequenceWaveformBGColor = new Color32(172, 227, 222, 255);
        public Color volumeEnvelopeColor = new Color(0.1f, 0.7f, 0.1f);
        public float volumeEnvelopeThickness = 1.5f;
        public Color pitchEnvelopeColor = new Color(0.9f, 0.2f, 0.1f);
        public float pitchEnvelopeThickness = 1.5f;
        public Color trimHandleColor = Color.white;
        public float trimHandleThickness = 2.0f;
        public Color waveformColor = Color.black;
        public Color renderedWaveformColor = Color.black;
        public Color renderedWaveformBGColor = new Color32(200, 200, 200, 255);
        public Color renderedPlayerHeadColor = new Color(0.9f, 0.1f, 0.1f, 0.8f);
        public Color trimAreaColor = new Color(0f, 0f, 0f, 0.5f);
        public Color selectedEnvelopeLineColor = new Color(0.1f, 0.7f, 0.9f);
        public Color selectedEnvelopeHandleColor = new Color(0.1f, 0.75f, 0.85f);
    }
}
