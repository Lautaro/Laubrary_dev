using System;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The audition card (T-0486), opened by right-clicking a Zounds editor's Play button: a ZUI overlay anchored to the
    /// button, in the window's own panel. Two rows, wide rather than tall:
    ///
    ///   [Play on change] [Burst] [Loop]
    ///   [Plays] [Gap] [Steady | From start | From end]
    ///
    /// Every control is a setting or a start/stop latch, so none of them closes the card (a click outside or Esc does).
    /// It only plays the sound; nothing here edits it or is saved with it.
    /// </summary>
    public static class AuditionPopupTK {

        const float RowH = 20f;

        public static ZuiPopover Show(VisualElement anchor, ZoundAudition session) {
            if (anchor == null || session == null || session.IsDisposed) return null;
            Action refresh = null;
            var pop = Z.Popover(anchor, panel => refresh = Build(panel, session),
                                new ZuiPopover.Options { preferredSide = ZuiPopover.Side.Above, onClosed = () => session.changed -= refresh });
            session.changed += refresh;
            return pop;
        }

        static VisualElement Row() {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = RowH; r.style.flexShrink = 0;
            return r;
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }

        static string BurstLabel(ZoundAudition s) => s.BurstRunning ? "Burst " + s.BurstDone + "/" + s.settings.count : "Burst";

        /// <summary>Fills the card and returns its refresh, called whenever the session's state changes.</summary>
        static Action Build(VisualElement panel, ZoundAudition s) {
            // ── row 1: the three helpers ──
            var r1 = Row();
            var onChange = ZS.Toggle("Play on change", "", s.playOnChange, v => { s.playOnChange = v; s.changed?.Invoke(); },
                                     "RichToggle", ZUICornerMask.All, 110f, RowH);
            r1.Add(onChange);
            r1.Add(Gap(8f));
            ZuiToggleButton burst = null, loop = null;
            burst = ZS.Toggle(BurstLabel(s), "", s.BurstRunning, v => { if (v) s.StartBurst(); else s.StopRun(); },
                              "RichToggle", ZUICornerMask.Left, 84f, RowH);
            loop = ZS.Toggle("Loop", "", s.LoopRunning, v => { if (v) s.StartLoop(); else s.StopRun(); },
                             "RichToggle", ZUICornerMask.Right, 56f, RowH);
            r1.Add(burst);
            r1.Add(loop);
            panel.Add(r1);
            panel.Add(Gap(0f).WithHeight(4f));

            // ── row 2: what Burst and Loop share ──
            var r2 = Row();
            ZuiSkinSlider count = null, gap = null;
            count = ZS.Slider("Plays", s.settings.count, 1f, ZoundAudition.MaxCount,
                "How many times Burst plays the sound. Loop ignores it and keeps going until stopped.",
                v => { s.settings.count = Mathf.Clamp(Mathf.RoundToInt(v), 1, ZoundAudition.MaxCount); s.SaveSettings(); s.changed?.Invoke(); },
                ZuiSkinSlider.LabelMode.LabelAndValue, 4f, "Default", 96f, RowH - 2f,
                v => Mathf.RoundToInt(v).ToString());
            r2.Add(count);
            r2.Add(Gap(6f));
            gap = ZS.Slider("Gap", s.settings.gap, 0f, ZoundAudition.MaxGap, "",
                v => { s.settings.gap = v; s.SaveSettings(); s.changed?.Invoke(); },
                ZuiSkinSlider.LabelMode.LabelAndValue, 0.5f, "Default", 120f, RowH - 2f,
                v => v.ToString("0.00") + " s");
            r2.Add(gap);
            r2.Add(Gap(6f));
            var mode = Z.Segmented((int)s.settings.mode, new[] { "Steady", "From start", "From end" },
                "How the gap between plays is counted.\n\n" +
                "Steady: a fixed rhythm, set once when Burst or Loop starts: the first play's length plus the gap. Later plays keep that rhythm even when they turn out longer or shorter.\n\n" +
                "From start: the gap counts from when the previous play started, whatever its length, so plays can overlap.\n\n" +
                "From end: the gap counts from when the previous play really finished, however long it took.",
                i => { s.settings.mode = (AuditionGap)i; s.SaveSettings(); s.changed?.Invoke(); });
            mode.style.height = RowH; mode.style.flexShrink = 0;
            r2.Add(mode);
            panel.Add(r2);

            void Refresh() {
                bool never = s.NeverEnds;
                onChange.SetValueWithoutNotify(s.playOnChange);
                onChange.tooltip = s.playOnChange
                    ? "Stop playing the sound after every change. (Now: every change you make in this window and let go of plays it again from the start.)"
                    : "Play the sound from the start every time you change something in this window and let go of the control (the end of a drag, a click, a typed value accepted). Not while Burst or Loop runs: they already let you hear every change live.";
                burst.SetValueWithoutNotify(s.BurstRunning);
                burst.text = BurstLabel(s);
                loop.SetValueWithoutNotify(s.LoopRunning);
                burst.SetEnabled(!never || s.BurstRunning);
                loop.SetEnabled(!never || s.LoopRunning);
                string gapWord = s.settings.mode == AuditionGap.FromEnd ? "after each play finishes"
                               : s.settings.mode == AuditionGap.FromStart ? "after each play starts"
                               : "on top of the first play's length";
                burst.tooltip = never
                    ? "Unavailable: this sound never ends by itself (it is a Looper, or contains one), so each play would carry on forever. Press Play instead; it loops until you stop it."
                    : s.BurstRunning ? "Stop the burst now, including the play that is sounding."
                    : "Play the sound " + s.settings.count + " times, " + s.settings.gap.ToString("0.00") + " s " + gapWord + ".";
                loop.tooltip = never
                    ? "Unavailable: this sound never ends by itself (it is a Looper, or contains one). Press Play instead; it loops until you stop it."
                    : s.LoopRunning ? "Stop repeating, including the play that is sounding."
                    : "Play the sound over and over, " + s.settings.gap.ToString("0.00") + " s " + gapWord + ", until you press this again.";
                gap.tooltip = s.settings.mode == AuditionGap.FromStart
                    ? "Time from one play's start to the next (at least " + ZoundAudition.MinStartGap.ToString("0.00") + " s). Shorter than the sound: they overlap."
                    : s.settings.mode == AuditionGap.FromEnd
                        ? "Silence between the end of one play and the start of the next."
                        : "Added to the first play's length to make the fixed rhythm every play keeps to.";
                count.SetEnabled(!never);
                gap.SetEnabled(!never);
                mode.SetEnabled(!never);
                mode.SetOn(i => i == (int)s.settings.mode);
            }
            Refresh();
            return Refresh;
        }

        static VisualElement WithHeight(this VisualElement e, float h) { e.style.height = h; return e; }
    }
}
