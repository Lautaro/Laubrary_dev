using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>
    /// The level meter's arithmetic, kept apart from the drawing so a check can measure it: decibels, the level that
    /// falls back smoothly, the peak mark that holds for two seconds and then falls, and the loudest peak since the meter
    /// was last cleared (by a click, or by nothing being played for a while). First written for Zounds (2026-10-09);
    /// moved to ZUI (T-0635) so every audio tool shows the same meter.
    /// </summary>
    public static class ZuiLevelMeterMath
    {
        public const float FloorDb = -60f, TopDb = 6f, HoldSeconds = 2f, PeakFallDbPerSecond = 12f, LevelFallDbPerSecond = 30f;

        public struct State
        {
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
        /// the seconds since it. <paramref name="idleFor"/> is how long nothing has been played (negative: unknown).</summary>
        public static void Step(ref State s, float peak, double now, float dt, bool idleReset, float idleSeconds, double idleFor)
        {
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
    /// One meter feed: takes (and clears) the engine's peak at most every 20 ms, so every meter showing the same feed
    /// shows the same thing without stealing each other's peaks. The host supplies how to read the peak and, optionally,
    /// how long nothing has played and whether an idle meter clears itself.
    /// </summary>
    public sealed class ZuiLevelFeed
    {
        readonly Func<float> takePeak;
        readonly Func<double> idleFor;
        readonly Func<bool> idleReset;
        readonly Func<float> idleSeconds;
        ZuiLevelMeterMath.State state = ZuiLevelMeterMath.Cleared;
        double last = -1d;

        /// <param name="takePeak">The loudest sample (linear, 1 = full scale) since the last call; the call clears it.</param>
        /// <param name="idleFor">Seconds since anything was last played, or null when unknown.</param>
        /// <param name="idleReset">Whether an idle meter clears its number (null: never).</param>
        /// <param name="idleSeconds">How long idle before it clears (null: 3 s).</param>
        public ZuiLevelFeed(Func<float> takePeak, Func<double> idleFor = null, Func<bool> idleReset = null, Func<float> idleSeconds = null)
        {
            this.takePeak = takePeak ?? (() => 0f); this.idleFor = idleFor; this.idleReset = idleReset; this.idleSeconds = idleSeconds;
        }

        public ZuiLevelMeterMath.State State => state;

        public void Step()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (last >= 0d && now - last < 0.02d) return;
            float dt = last < 0d ? 0f : (float)(now - last);
            last = now;
            float peak = takePeak();
            ZuiLevelMeterMath.Step(ref state, peak, now, dt, idleReset != null && idleReset(), Mathf.Max(0.5f, idleSeconds?.Invoke() ?? 3f), idleFor?.Invoke() ?? -1d);
        }

        public void Clear() { state = ZuiLevelMeterMath.Cleared; }
    }

    /// <summary>
    /// A thin level meter: a bar coloured green to -12 dB, yellow to 0 dB and red above, a mark at 0 dB, a peak mark that
    /// holds two seconds and then falls, and the loudest peak since it was cleared in decibels (red above 0 dB). A click
    /// clears it. Used by Zounds (Klip, Zequence and Zounds windows) and ZTracker; each host gives it its own feed.
    /// </summary>
    public class ZuiLevelMeter : VisualElement
    {
        public static readonly Color HotText = new Color(1f, 95f / 255f, 80f / 255f, 1f);
        static readonly Color NormalText = new Color(190f / 255f, 190f / 255f, 200f / 255f, 1f);

        readonly ZuiLevelFeed feed;
        readonly Func<bool> shown;
        readonly VisualElement bar;
        readonly Label readout;
        string shownText;
        bool hot;

        /// <param name="feed">Where the level comes from.</param>
        /// <param name="tip">The meter's tooltip (what it measures, in the host's words).</param>
        /// <param name="shown">Whether the meter is shown right now (null: always); checked about 30 times a second.</param>
        public ZuiLevelMeter(ZuiLevelFeed feed, string tip, Func<bool> shown = null)
        {
            this.feed = feed; this.shown = shown;
            AddToClassList("zui-level-meter");
            tooltip = tip;
            style.flexDirection = FlexDirection.Row; style.flexShrink = 0; style.height = 12; style.alignItems = Align.Center;
            style.marginTop = style.marginBottom = 2; style.marginLeft = style.marginRight = 4;
            bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("zui-level-meter__bar");
            bar.style.flexGrow = 1; bar.style.height = 8;
            bar.generateVisualContent += Paint;
            Add(bar);
            readout = new Label("Peak –") { pickingMode = PickingMode.Ignore };
            readout.AddToClassList("zui-level-meter__readout");
            readout.style.width = 84; readout.style.flexShrink = 0; readout.style.fontSize = 10; readout.style.unityTextAlign = TextAnchor.MiddleRight;
            readout.style.color = NormalText;
            readout.style.paddingLeft = readout.style.paddingRight = readout.style.paddingTop = readout.style.paddingBottom = 0;
            readout.style.marginLeft = readout.style.marginRight = readout.style.marginTop = readout.style.marginBottom = 0;
            Add(readout);
            RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; feed.Clear(); Sync(); e.StopPropagation(); });
            schedule.Execute(Tick).Every(33);
            Sync();
        }

        /// <summary>The readout label (a host may add its own text class to it).</summary>
        public Label Readout => readout;
        public ZuiLevelFeed Feed => feed;

        void Tick()
        {
            bool visible = shown == null || shown();
            var want = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (style.display != want) style.display = want;
            if (!visible) return;
            feed.Step();
            Sync();
        }

        void Sync()
        {
            var s = feed.State;
            string t = s.hasMax ? "Peak " + s.maxDb.ToString("+0.0;-0.0;0.0") + " dB" : "Peak –";
            if (t != shownText)
            {
                shownText = t;
                readout.text = t;
                bool nowHot = s.hasMax && s.maxDb > 0f;
                if (nowHot != hot) { hot = nowHot; readout.style.color = hot ? HotText : NormalText; }
                readout.EnableInClassList("zui-level-meter__readout--hot", nowHot);
            }
            bar.MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext ctx)
        {
            var r = bar.contentRect;
            if (r.width < 4f || r.height < 2f) return;
            var p = ctx.painter2D;
            var s = feed.State;
            Fill(p, 0f, 0f, r.width, r.height, new Color(0.07f, 0.08f, 0.1f, 1f));
            float x = ZuiLevelMeterMath.Position(s.levelDb) * r.width;
            float xg = ZuiLevelMeterMath.Position(-12f) * r.width, x0 = ZuiLevelMeterMath.Position(0f) * r.width;
            Fill(p, 0f, 1f, Mathf.Min(x, xg), r.height - 2f, new Color(0.25f, 0.75f, 0.35f, 1f));
            if (x > xg) Fill(p, xg, 1f, Mathf.Min(x, x0) - xg, r.height - 2f, new Color(0.9f, 0.8f, 0.25f, 1f));
            if (x > x0) Fill(p, x0, 1f, x - x0, r.height - 2f, new Color(0.95f, 0.3f, 0.25f, 1f));
            Fill(p, x0, 0f, 1f, r.height, new Color(1f, 1f, 1f, 0.35f));
            if (s.holdDb > ZuiLevelMeterMath.FloorDb)
            {
                float xh = ZuiLevelMeterMath.Position(s.holdDb) * r.width;
                Fill(p, Mathf.Clamp(xh - 1f, 0f, r.width - 2f), 0f, 2f, r.height, s.holdDb > 0f ? new Color(1f, 0.35f, 0.3f, 1f) : Color.white);
            }
        }

        static void Fill(Painter2D p, float x, float y, float w, float h, Color c)
        {
            if (w <= 0f || h <= 0f) return;
            p.fillColor = c;
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }
    }
}
