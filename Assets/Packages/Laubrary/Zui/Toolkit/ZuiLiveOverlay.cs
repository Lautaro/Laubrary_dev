using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>What is moving a value away from where it was set: a modulator (an oscillator, an envelope…), or game code.</summary>
    public enum ZuiLiveKind { Modulated = 0, Driven = 1 }

    /// A layer laid over a slider-like track that shows where a value IS, as opposed to where it was SET (added for Zounds
    /// ZPOC, T-0492, and meant for any tool whose values move while something runs):
    ///
    /// - the set value as a thin dark-and-bright marker, always, even at rest;
    /// - the live value as a band from the marker to it: blue above / dark below for a modulator, amber for game code;
    /// - where a code-driven value is heading, as a tick, while it is still easing there;
    /// - a brief amber pulse around the track each time code sends a new value, fading within half a second, so a value
    ///   sent once flashes once and one sent every frame reads as continuously lit;
    /// - hollow (outline only) while a person holds the control and their hand wins over code;
    /// - for many instances at once, the spread between the lowest and highest live value as a faint band with a tick per
    ///   instance, given level-meter ballistics: it widens at once and narrows slowly, so short-lived instances leave a
    ///   readable trace instead of flicker, and nothing ever whips from one instance to another.
    ///
    /// Positions are 0..1 along the track. The layer never takes a click (the control under it keeps working) and paints
    /// nothing itself: every colour is a USS class in ZuiToolkit.uss (.zui-live*). It only lays itself out when something
    /// changed or while a pulse or the spread is still moving.
    public class ZuiLiveOverlay : VisualElement
    {
        /// <summary>How long the pulse after a new value takes to fade out, in seconds.</summary>
        public const float PulseSeconds = 0.5f;
        /// <summary>How quickly the spread narrows back once no instance holds it open (time constant, seconds).</summary>
        public const float SpreadRelease = 0.3f;

        readonly VisualElement spreadBand, band, markDark, markBright, tick, pulse;
        readonly List<VisualElement> spreadTicks = new List<VisualElement>();
        float authored = -1f, live = -1f, target = -1f;
        ZuiLiveKind kind;
        bool hollow, hasSpread, dirty = true;
        float spreadLo, spreadHi, shownLo, shownHi;
        readonly List<float> ticks = new List<float>();
        float pulseAt = -100f, lastAnimate;

        public ZuiLiveOverlay()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("zui-live");
            style.position = Position.Absolute;
            style.left = 1f; style.right = 1f; style.top = 1f; style.bottom = 1f;
            spreadBand = Part("zui-live__spread");
            band = Part("zui-live__band");
            markDark = Part("zui-live__mark-dark");
            markBright = Part("zui-live__mark-bright");
            tick = Part("zui-live__tick");
            pulse = Part("zui-live__pulse");
            pulse.style.left = 0; pulse.style.right = 0; pulse.style.width = StyleKeyword.Auto;
            RegisterCallback<GeometryChangedEvent>(_ => { dirty = true; Relayout(); });
            lastAnimate = Now;
            schedule.Execute(Animate).Every(33);
        }

        VisualElement Part(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            e.style.position = Position.Absolute; e.style.top = 0; e.style.bottom = 0;
            e.style.display = DisplayStyle.None;
            Add(e);
            return e;
        }

        static float Now => Time.realtimeSinceStartup;

        /// <summary>Where the value was set (0..1). Below nought hides the marker.</summary>
        public void SetAuthored(float t) { if (t != authored) { authored = t; dirty = true; Relayout(); } }

        /// <summary>Where the value is right now, and what is moving it. Hidden while it sits on the set value.</summary>
        public void SetLive(float t, ZuiLiveKind k) { if (t != live || k != kind) { live = t; kind = k; dirty = true; Relayout(); } }
        public void ClearLive() { if (live >= 0f) { live = -1f; dirty = true; Relayout(); } }

        /// <summary>Where a code-driven value is heading while it eases there.</summary>
        public void SetTarget(float t) { if (t != target) { target = t; dirty = true; Relayout(); } }
        public void ClearTarget() { SetTarget(-1f); }

        /// <summary>Flashes the track: a new value just arrived from code.</summary>
        public void Pulse() { pulseAt = Now; dirty = true; Relayout(); }

        /// <summary>A person is holding the control: their hand wins, so the code band is drawn as an outline only.</summary>
        public void SetHollow(bool on) { if (on != hollow) { hollow = on; dirty = true; Relayout(); } }

        /// <summary>
        /// Several instances at once: the lowest and highest live value and one tick per instance. The band shown follows
        /// <see cref="Ballistics"/>; call every refresh while instances play.
        /// </summary>
        public void SetSpread(float lo, float hi, IReadOnlyList<float> instanceTicks, ZuiLiveKind k)
        {
            kind = k;
            if (!hasSpread) { shownLo = lo; shownHi = hi; }
            hasSpread = true; spreadLo = lo; spreadHi = hi;
            ticks.Clear();
            if (instanceTicks != null) for (int i = 0; i < instanceTicks.Count; i++) ticks.Add(instanceTicks[i]);
            dirty = true;
        }

        public void ClearSpread() { if (hasSpread) { hasSpread = false; dirty = true; Relayout(); } }

        /// <summary>
        /// Level-meter ballistics for a spread band: an edge moving OUT follows at once; an edge moving IN closes the gap
        /// with time constant <paramref name="release"/>. Pure, so it can be checked on its own.
        /// </summary>
        public static void Ballistics(ref float shownLo, ref float shownHi, float lo, float hi, float dt, float release)
        {
            float k = release > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, dt) / release) : 1f;
            if (lo <= shownLo) shownLo = lo; else shownLo += (lo - shownLo) * k;
            if (hi >= shownHi) shownHi = hi; else shownHi += (hi - shownHi) * k;
        }

        void Animate()
        {
            float now = Now, dt = now - lastAnimate;
            lastAnimate = now;
            bool moving = false;
            if (hasSpread)
            {
                float lo0 = shownLo, hi0 = shownHi;
                Ballistics(ref shownLo, ref shownHi, spreadLo, spreadHi, dt, SpreadRelease);
                if (Mathf.Abs(shownLo - lo0) > 1e-4f || Mathf.Abs(shownHi - hi0) > 1e-4f) moving = true;
            }
            if (now - pulseAt < PulseSeconds + 0.05f) moving = true;
            if (moving || dirty) { dirty = true; Relayout(); }
        }

        void Relayout()
        {
            if (!dirty) return;
            float w = resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0f) return;
            dirty = false;

            // The set value's marker: a dark line with a bright one beside it, readable on any fill.
            if (authored >= 0f)
            {
                float xm = Mathf.Clamp(w * authored, 1f, w - 1f);
                markDark.style.display = DisplayStyle.Flex; markDark.style.left = xm - 1f; markDark.style.width = 1f;
                markBright.style.display = DisplayStyle.Flex; markBright.style.left = xm; markBright.style.width = 1f;
            }
            else { markDark.style.display = DisplayStyle.None; markBright.style.display = DisplayStyle.None; }

            // The live band, from the set value to where the value is.
            float a = authored >= 0f ? authored : live;
            if (live >= 0f && Mathf.Abs(live - a) >= 0.001f)
            {
                float xa = w * a, xl = w * live;
                band.style.display = DisplayStyle.Flex;
                band.style.left = Mathf.Min(xa, xl); band.style.width = Mathf.Max(1f, Mathf.Abs(xl - xa));
                band.EnableInClassList("zui-live__band--driven", kind == ZuiLiveKind.Driven);
                band.EnableInClassList("zui-live__band--up", kind == ZuiLiveKind.Modulated && live > a);
                band.EnableInClassList("zui-live__band--down", kind == ZuiLiveKind.Modulated && live <= a);
                band.EnableInClassList("zui-live__band--hollow", hollow);
            }
            else band.style.display = DisplayStyle.None;

            // Where code is taking it.
            if (target >= 0f && (live < 0f || Mathf.Abs(target - live) >= 0.002f))
            {
                tick.style.display = DisplayStyle.Flex;
                tick.style.left = Mathf.Clamp(w * target, 1f, w - 2f) - 1f; tick.style.width = 2f;
            }
            else tick.style.display = DisplayStyle.None;

            // The pulse after a new value.
            float age = Now - pulseAt;
            if (age >= 0f && age < PulseSeconds)
            {
                pulse.style.display = DisplayStyle.Flex;
                pulse.style.opacity = 1f - age / PulseSeconds;
            }
            else pulse.style.display = DisplayStyle.None;

            // Many instances: the spread, with a tick per instance.
            if (hasSpread)
            {
                spreadBand.style.display = DisplayStyle.Flex;
                spreadBand.style.left = w * Mathf.Clamp01(shownLo);
                spreadBand.style.width = Mathf.Max(1f, w * (Mathf.Clamp01(shownHi) - Mathf.Clamp01(shownLo)));
                spreadBand.EnableInClassList("zui-live__spread--driven", kind == ZuiLiveKind.Driven);
                while (spreadTicks.Count < ticks.Count) spreadTicks.Add(Part("zui-live__spread-tick"));
                for (int i = 0; i < spreadTicks.Count; i++)
                {
                    var t = spreadTicks[i];
                    if (i < ticks.Count)
                    {
                        t.style.display = DisplayStyle.Flex;
                        t.style.left = Mathf.Clamp(w * ticks[i], 0f, w - 1f); t.style.width = 1f;
                        t.EnableInClassList("zui-live__spread-tick--driven", kind == ZuiLiveKind.Driven);
                    }
                    else t.style.display = DisplayStyle.None;
                }
            }
            else
            {
                spreadBand.style.display = DisplayStyle.None;
                for (int i = 0; i < spreadTicks.Count; i++) spreadTicks[i].style.display = DisplayStyle.None;
            }
        }
    }
}
