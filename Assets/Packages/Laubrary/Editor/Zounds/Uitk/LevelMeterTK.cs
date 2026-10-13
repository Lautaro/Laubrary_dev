using Laubrary.Zounds.Dsp;
using Laubrary.Zui;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// One feed for every Zounds meter: the engine's peak is taken (and cleared) once per step, so the Klip editor, the
    /// Zequence editor and the Zounds window all show the same thing without stealing each other's peaks.
    /// </summary>
    internal static class LevelMeterFeed {
        public static readonly ZuiLevelFeed Shared = new ZuiLevelFeed(
            SapVoiceRegistry.ReadOutputPeak,
            () => ZoundEngine.LastTriggerTime >= 0d ? Time.realtimeSinceStartupAsDouble - ZoundEngine.LastTriggerTime : double.MaxValue,
            () => ZoundsProject.Instance.projectSettings.editorStyle.levelMeterIdleReset,
            () => ZoundsProject.Instance.projectSettings.editorStyle.levelMeterIdleSeconds);

        public static ZuiLevelMeterMath.State State => Shared.State;
        public static void Step() => Shared.Step();
        public static void Clear() => Shared.Clear();
    }

    /// <summary>
    /// The level meter (owner, 2026-10-09): a thin bar along the bottom edge of the Klip editor, the Zequence editor and
    /// the Zounds window, showing how hot everything Zounds plays is. The meter itself is ZUI's (<see cref="ZuiLevelMeter"/>,
    /// shared with ZTracker since T-0635); this gives it the Zounds feed and the Settings tab's show/hide switch.
    /// </summary>
    internal sealed class LevelMeterTK : ZuiLevelMeter {
        public LevelMeterTK() : base(LevelMeterFeed.Shared,
            "How loud everything Zounds plays is, before the mixer: the bar is the level now, the white mark the loudest of the last two seconds, and the number the loudest peak since the meter was cleared (red above 0 dB: clipping). Click to clear it. It also clears once no sound has been triggered for the time set in Settings > Level meter.",
            () => ZoundsProject.Instance.projectSettings.editorStyle.levelMeterShown) {
            AddToClassList("zs-level-meter");
            Readout.AddToClassList("zs-lbl");
        }
    }
}
