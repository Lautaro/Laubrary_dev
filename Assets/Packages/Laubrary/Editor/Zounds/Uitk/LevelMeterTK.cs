using Laubrary.Zounds.Dsp;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The level meter's arithmetic (2026-10-09), kept apart from the drawing so a check can measure it: decibels, the
    /// level that falls back smoothly, the peak mark that holds for two seconds and then falls, and the loudest peak since
    /// the meter was last cleared (by a click, or by nothing being triggered for the Settings tab's number of seconds).
    /// </summary>
    internal static class LevelMeterMath {
        public const float FloorDb = -60f, TopDb = 6f, HoldSeconds = 2f, PeakFallDbPerSecond = 12f, LevelFallDbPerSecond = 30f;

        public struct State {
            /// <summary>The bar: the level now, falling back at <see cref="LevelFallDbPerSecond"/>.</summary>
            public float levelDb;
            /// <summary>The peak mark: held for <see cref="HoldSeconds"/>, then falling at <see cref="PeakFallDbPerSecond"/>.</summary>
            public float holdDb;
            public double holdAt;
            /// <summary>The number: the loudest peak since the meter was last cleared.</summary>
            public float maxDb;
            public bool hasMax;
        }

        public static float ToDb(float linear) => linear <= 1e-6f ? -120f : 20f * Mathf.Log10(linear);

        public static State Cleared => new State { levelDb = FloorDb, holdDb = FloorDb, maxDb = FloorDb, hasMax = false };

        /// <summary>One step: <paramref name="peak"/> is the loudest sample (linear) since the last step, <paramref name="dt"/>
        /// the seconds since it. <paramref name="idleFor"/> is how long nothing has been triggered (negative: unknown).</summary>
        public static void Step(ref State s, float peak, double now, float dt, bool idleReset, float idleSeconds, double idleFor) {
            float inDb = Mathf.Max(ToDb(peak), FloorDb - 1f);
            s.levelDb = Mathf.Max(inDb, Mathf.Max(FloorDb, s.levelDb - LevelFallDbPerSecond * dt));
            if (inDb >= s.holdDb && inDb > FloorDb) { s.holdDb = inDb; s.holdAt = now; }
            else if (now - s.holdAt > HoldSeconds) s.holdDb = Mathf.Max(FloorDb, s.holdDb - PeakFallDbPerSecond * dt);
            if (inDb > FloorDb && (!s.hasMax || inDb > s.maxDb)) { s.maxDb = inDb; s.hasMax = true; }
            if (idleReset && idleFor > idleSeconds && inDb <= FloorDb && s.hasMax) { s.hasMax = false; s.maxDb = FloorDb; }
        }

        /// <summary>Where a level sits along the meter, 0..1 (<see cref="FloorDb"/> to <see cref="TopDb"/>, even in dB).</summary>
        public static float Position(float db) => Mathf.Clamp01((db - FloorDb) / (TopDb - FloorDb));
    }

    /// <summary>
    /// One feed for every meter: the engine's peak is taken (and cleared) once per step, so the Klip editor, the Zequence
    /// editor and the Zounds window all show the same thing without stealing each other's peaks.
    /// </summary>
    internal static class LevelMeterFeed {
        static LevelMeterMath.State state = LevelMeterMath.Cleared;
        static double last = -1d;

        public static LevelMeterMath.State State => state;

        public static void Step() {
            double now = Time.realtimeSinceStartupAsDouble;
            if (last >= 0d && now - last < 0.02d) return;
            float dt = last < 0d ? 0f : (float)(now - last);
            last = now;
            float peak = SapVoiceRegistry.ReadOutputPeak();
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            double idleFor = ZoundEngine.LastTriggerTime >= 0d ? now - ZoundEngine.LastTriggerTime : double.MaxValue;
            LevelMeterMath.Step(ref state, peak, now, dt, es.levelMeterIdleReset, Mathf.Max(0.5f, es.levelMeterIdleSeconds), idleFor);
        }

        public static void Clear() { state = LevelMeterMath.Cleared; }
    }

    /// <summary>
    /// The level meter (owner, 2026-10-09): a thin bar along the bottom edge of the Klip editor, the Zequence editor and
    /// the Zounds window, showing how hot everything Zounds plays is, with a peak mark that holds two seconds and then
    /// falls, and the loudest peak since it was cleared in decibels. A click clears it; it also clears itself once no
    /// sound has been triggered for a while (Settings: Level meter). Shown or hidden from the Settings tab.
    /// </summary>
    internal sealed class LevelMeterTK : VisualElement {

        readonly VisualElement bar;
        readonly Label readout;
        string shownText;

        public LevelMeterTK() {
            AddToClassList("zs-level-meter");
            tooltip = "How loud everything Zounds plays is, before the mixer: the bar is the level now, the white mark the loudest of the last two seconds, and the number the loudest peak since the meter was cleared (red above 0 dB: clipping). Click to clear it. It also clears once no sound has been triggered for the time set in Settings > Level meter.";
            bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("zs-level-meter__bar");
            bar.generateVisualContent += Paint;
            Add(bar);
            readout = new Label("Peak –") { pickingMode = PickingMode.Ignore };
            readout.AddToClassList("zs-lbl"); readout.AddToClassList("zs-level-meter__readout");
            Add(readout);
            RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; LevelMeterFeed.Clear(); Sync(); e.StopPropagation(); });
            schedule.Execute(Tick).Every(33);
            Sync();
        }

        void Tick() {
            bool shown = ZoundsProject.Instance.projectSettings.editorStyle.levelMeterShown;
            var want = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (style.display != want) style.display = want;
            if (!shown) return;
            LevelMeterFeed.Step();
            Sync();
        }

        void Sync() {
            var s = LevelMeterFeed.State;
            string t = s.hasMax ? "Peak " + s.maxDb.ToString("+0.0;-0.0;0.0") + " dB" : "Peak –";
            if (t != shownText) {
                shownText = t;
                readout.text = t;
                readout.EnableInClassList("zs-level-meter__readout--hot", s.hasMax && s.maxDb > 0f);
            }
            bar.MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext ctx) {
            var r = bar.contentRect;
            if (r.width < 4f || r.height < 2f) return;
            var p = ctx.painter2D;
            var s = LevelMeterFeed.State;
            Fill(p, 0f, 0f, r.width, r.height, new Color(0.07f, 0.08f, 0.1f, 1f));
            // The level, coloured by zone: green to -12 dB, yellow to 0 dB, red above.
            float x = LevelMeterMath.Position(s.levelDb) * r.width;
            float xg = LevelMeterMath.Position(-12f) * r.width, x0 = LevelMeterMath.Position(0f) * r.width;
            Fill(p, 0f, 1f, Mathf.Min(x, xg), r.height - 2f, new Color(0.25f, 0.75f, 0.35f, 1f));
            if (x > xg) Fill(p, xg, 1f, Mathf.Min(x, x0) - xg, r.height - 2f, new Color(0.9f, 0.8f, 0.25f, 1f));
            if (x > x0) Fill(p, x0, 1f, x - x0, r.height - 2f, new Color(0.95f, 0.3f, 0.25f, 1f));
            // 0 dB, then the held peak.
            Fill(p, x0, 0f, 1f, r.height, new Color(1f, 1f, 1f, 0.35f));
            if (s.holdDb > LevelMeterMath.FloorDb) {
                float xh = LevelMeterMath.Position(s.holdDb) * r.width;
                Fill(p, Mathf.Clamp(xh - 1f, 0f, r.width - 2f), 0f, 2f, r.height, s.holdDb > 0f ? new Color(1f, 0.35f, 0.3f, 1f) : Color.white);
            }
        }

        static void Fill(Painter2D p, float x, float y, float w, float h, Color c) {
            if (w <= 0f || h <= 0f) return;
            p.fillColor = c;
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }
    }
}
