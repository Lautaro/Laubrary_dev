using Laubrary.ZTracker.Engine;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // The level meter along the bottom of the window (T-0635): ZUI's meter, the same one Zounds shows, fed by every
        // renderer this window owns (the song, the instrument preview, the drum-source preview), read before the output
        // limiter so it shows when the mix is too hot.
        const float LevelIdleSeconds = 4f;
        ZuiLevelFeed levelFeed;
        double lastSound = -1;

        float TakeLevel()
        {
            float peak = 0;
            void From(TrackerSapGenerator g) { if (g != null) peak = Mathf.Max(peak, g.TakeOutputPeak()); }
            From(host); From(midiPreviewHost); if (drumSourcePreview != null) From(drumSourcePreview.GetComponent<TrackerSapGenerator>());
            if (peak > 1e-5f) lastSound = Time.realtimeSinceStartupAsDouble;
            return peak;
        }

        ZuiLevelFeed LevelFeed => levelFeed ??= new ZuiLevelFeed(TakeLevel, () => lastSound < 0 ? double.MaxValue : Time.realtimeSinceStartupAsDouble - lastSound, () => true, () => LevelIdleSeconds);

        VisualElement LevelMeter()
        {
            var meter = new ZuiLevelMeter(LevelFeed, "How loud ZTracker plays (the song and the instrument previews): the bar is the level now, the white mark the loudest of the last two seconds, and the number the loudest peak since the meter was cleared. Red above 0 dB means the notes add up to more than full scale: the output limiter then turns the whole mix down so it does not clip; lower Volume or Level for a clean sound. Click to clear it; it also clears after " + LevelIdleSeconds + " seconds of silence.");
            meter.name = "level-meter";
            return meter;
        }
    }
}
