using Laubrary.Zounds.Dsp;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Klip editor's speed strip (T-0481; twin of TimeStretchGUI): everything that decides how fast a play moves
    /// through its source, and how long the play lasts.
    ///
    /// Row one is the live stretcher: Live speed (the sound's own speed, and game code's), and -- whenever the stretcher
    /// will run for this sound, whatever switched it on (Live speed, a time curve, keep length, an old stretch setting) --
    /// its window, Keep hits and algorithm. At the right, the length a play will actually have, from the same plan the
    /// engine plays from.
    ///
    /// Row two appears only for a Klip that still carries an old Uniform / Region / Curve stretch. Those settings used to
    /// be computed only for the editor and were never heard; they are now played through the live stretcher at every
    /// play, and this row says so, with Convert (make it permanent as a Speed or a time curve) and Remove. They are no
    /// longer edited here: the time curve on the waveform and the Speed slider are where that is done now.
    /// </summary>
    public class TimeStretchTK : VisualElement {

        const float RowH = 20f;
        readonly Klip klip;
        Label length;

        public TimeStretchTK(Klip klip) {
            this.klip = klip;
            AddToClassList("zs-time-stretch__root");
            Build();
            // The real length follows every edit anywhere in the window (trim, pitch and time curves, speed).
            schedule.Execute(UpdateLength).Every(250);
        }

        ZoundTimeStretch TS {
            get { if (klip.timeStretch == null) klip.timeStretch = new ZoundTimeStretch(); return klip.timeStretch; }
        }

        void Set(string undo, System.Action a) {
            ZoundsWindow.ModifyAndSaveZoundsProject(undo, a);
            Rebuild();
        }

        void Rebuild() { Clear(); Build(); }

        static VisualElement Row() {
            var r = new VisualElement();
            r.AddToClassList("zs-time-stretch__row");
            return r;
        }
        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.AddToClassList("zs-time-stretch__gap"); return e; }
        static VisualElement Flex() { var e = new VisualElement(); e.AddToClassList("zs-time-stretch__spacer"); return e; }

        /// <summary>Whether the live stretcher runs for a play of this sound, and why (for the tooltips).</summary>
        internal static bool StretcherRuns(Klip klip, out string why) {
            why = null;
            var clip = ZoundSapPlayback.LoadSourceClip(klip);
            double freq = clip != null && clip.frequency > 0 ? clip.frequency : 48000;
            double frames = clip != null ? clip.samples : freq;
            var plan = ZoundSapPlayback.Plan(klip, 0, frames, freq);
            if (!plan.stretched) return false;
            var ts = klip.timeStretch;
            if (ts != null && ts.liveEnabled) why = "Live speed is on";
            else if (plan.legacyStretch) why = "an old stretch setting is being played";
            else if (plan.keepLength) why = "the pitch curve keeps the length";
            else why = "a time curve is on";
            return true;
        }

        /// <summary>"Plays 0.54 s" and what that is made of -- the length the engine will give a play (at the sound's
        /// own pitch of one; its Pitch range and game code's speed scale it further).</summary>
        internal static bool PlayLengthText(Klip klip, out string text, out string tip) {
            text = ""; tip = "";
            if (klip.IsLooper) { text = "Loops"; tip = "A Looper plays until it is stopped."; return true; }
            if (!ZoundSapPlayback.TryGetPlayLength(klip, out float seconds)) return false;
            bool varies = false;
            var ch = ZoundDspPlayback.ResolveChain(klip, out _);
            if (ch != null) foreach (var m in ch.modifiers) if (m.enabled && EnvelopeRandom.HasRandom(m.curve)) varies = true;
            text = "Plays " + (varies ? "≈" : "") + seconds.ToString("0.00") + " s";
            bool runs = StretcherRuns(klip, out string why);
            tip = "How long one play lasts, as the engine will play it: the (trimmed) source, through the pitch curve"
                + (runs ? ", the time curve and the speed (the live stretcher runs: " + why + ")" : " (tape-style: raising the pitch shortens the sound)")
                + ". At the sound's own pitch of one; its Pitch setting" + (runs ? " and game code's speed" : "") + " scale it further."
                + (varies ? " A curve has random points, so each play's length differs a little; this is the length with every point where it is drawn." : "");
            return true;
        }

        bool builtRuns, builtLegacy;

        void UpdateLength() {
            if (length == null || panel == null) return;
            // Something elsewhere in the window (a time curve, keep length, trim) can start or stop the stretcher.
            if (StretcherRuns(klip, out _) != builtRuns || (KlipChainEnvelopes.DescribeLegacyStretch(klip) != null) != builtLegacy) { Rebuild(); return; }
            if (PlayLengthText(klip, out string t, out string tip)) { length.text = t; length.tooltip = tip; }
            else { length.text = ""; length.tooltip = ""; }
        }

        void Build() {
            var ts = TS;
            builtRuns = StretcherRuns(klip, out _);
            string old = KlipChainEnvelopes.DescribeLegacyStretch(klip);
            builtLegacy = old != null;
            Add(BuildLive(ts));
            if (old != null) Add(BuildLegacy(old));
        }

        /// <summary>The old stretch setting's row: what it does, that it is now heard, Convert and Remove.</summary>
        VisualElement BuildLegacy(string what) {
            var r = Row();
            var l = new Label("Old stretch: " + what) {
                tooltip = "This sound carries a stretch setting from before the live stretcher. It used to be computed only for the editor and was never heard when the sound played; it is now played through the live stretcher at every play, so this is what you hear. It is not edited here any more: Convert moves it into the Speed slider (Uniform) or the time curve on the waveform (Region and Curve), where it can be edited."
            };
            l.AddToClassList("zs-lbl");
            l.AddToClassList("zs-time-stretch__legacy-label");
            r.Add(l);
            r.Add(Gap(6f));
            r.Add(ZS.Button("Convert", "Makes it permanent in the current controls, sounding the same: a Uniform stretch becomes this sound's Speed (Live speed on), a Region or Curve stretch becomes its time curve following the waveform. The old setting is then switched off.", "RichButton",
                () => Set("convert old stretch", () => KlipChainEnvelopes.ConvertLegacyStretch(klip)), ZUICornerMask.Left, 70f, RowH - 2f));
            r.Add(ZS.Button("Remove", "Switches the old stretch setting off: the sound plays at its own length again.", "RichButton",
                () => Set("remove old stretch", () => { TS.enabled = false; ZoundDspPlayback.InvalidateLayout(klip); }), ZUICornerMask.Right, 70f, RowH - 2f));
            r.Add(Flex());
            return r;
        }

        /// <summary>The live stretcher's row.</summary>
        VisualElement BuildLive(ZoundTimeStretch ts) {
            var r = Row();
            bool runs = StretcherRuns(klip, out string why);
            r.Add(ZS.Toggle("Live speed", ts.liveEnabled
                    ? "Stop using this sound's own Speed and game code's speed. The live stretcher still runs if a time curve, keep length or an old stretch setting needs it."
                    : "Give this sound its own speed, and let game code change it while it plays (a bullet-time slowdown) -- without changing its pitch. Costs about one percent of a CPU core per playing copy.",
                ts.liveEnabled, on => Set(on ? "enable live speed" : "disable live speed", () => ts.liveEnabled = on), "RichToggle", ZUICornerMask.All, 84f, RowH));
            if (ts.liveEnabled) {
                r.Add(Gap(6f));
                var spd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Speed];
                float lmin = Mathf.Log(spd.min), lmax = Mathf.Log(spd.max);
                float t = Mathf.InverseLerp(lmin, lmax, Mathf.Log(Mathf.Clamp(ts.liveSpeed, spd.min, spd.max)));
                var undo = ZS.DragUndo(r, "live speed");
                ZuiSkinSlider s = null;
                s = ZS.Slider("Speed ×" + ts.liveSpeed.ToString("0.00"), t, 0f, 1f,
                    "How fast the sound moves through its source, without changing its pitch: 0.5 is half speed (twice as long), 2 is double. Heard immediately, even on a sound already playing. Game code's speed and the time curve multiply on top. Double-click resets to 1.",
                    nt => { undo(); float sp = Mathf.Exp(Mathf.Lerp(lmin, lmax, nt)); ts.liveSpeed = sp; EditorUtility.SetDirty(ZoundsProject.Instance); SapVoiceRegistry.PushAuthoredSpeed(klip, sp); s.text = "Speed ×" + sp.ToString("0.00"); },
                    ZuiSkinSlider.LabelMode.LabelOnly, Mathf.InverseLerp(lmin, lmax, 0f), "Default", 150f, RowH - 2f);
                r.Add(s);
            }
            if (runs) {
                string from = " (The live stretcher runs for this sound because " + why + ".)";
                r.Add(Gap(6f));
                var wundo = ZS.DragUndo(r, "live stretch window");
                ZuiSkinSlider ws = null;
                ws = ZS.Slider("Window " + ts.liveWindowMs.ToString("0") + " ms", ts.liveWindowMs, 10f, 100f,
                    "Length of the pieces the sound is cut into to stretch it. 20-30 ms suits speech and hits; 40-50 ms suits pads, chords and engines. Applies from the next play." + from,
                    w => { wundo(); ts.liveWindowMs = Mathf.Round(w); EditorUtility.SetDirty(ZoundsProject.Instance); ws.text = "Window " + ts.liveWindowMs.ToString("0") + " ms"; },
                    ZuiSkinSlider.LabelMode.LabelOnly, 30f, "Default", 120f, RowH - 2f);
                r.Add(ws);
                r.Add(Gap(6f));
                r.Add(ZS.Toggle("Keep hits", (ts.liveKeepHits
                        ? "Stretch hits too: attacks are then smeared, and at slow speeds can repeat like a machine gun. Applies from the next play."
                        : "Play each hit (attack) once, whole, at normal speed, and stretch only the material between hits, so shots and clicks stay sharp and never double. Applies from the next play.") + from,
                    ts.liveKeepHits, on => Set("live stretch keep hits", () => ts.liveKeepHits = on), "RichToggle", ZUICornerMask.All, 72f, RowH));
                r.Add(Gap(6f));
                string[] names = { "WSOLA", "Granular" };
                string[] tips = {
                    "Lines each piece up with the previous one, so tones stay clean and pitch stays exact. The right choice for almost everything. Applies from the next play.",
                    "Overlaps pieces without lining them up: rougher and grainier, which can suit magic, roars and textures as a deliberate character. Applies from the next play."
                };
                for (int a = 0; a < 2; a++) {
                    var alg = (LiveStretchAlgorithm)a;
                    r.Add(ZS.Toggle(names[a], tips[a] + from, (int)ts.liveAlgorithm == a, _ => {
                        if (ts.liveAlgorithm != alg) Set("live stretch algorithm", () => ts.liveAlgorithm = alg); else Rebuild();
                    }, "RichToggle", a == 0 ? ZUICornerMask.Left : ZUICornerMask.Right, a == 0 ? 60f : 70f, RowH));
                }
            }
            r.Add(Flex());
            length = new Label();
            length.AddToClassList("zs-minilabel");
            length.AddToClassList("zs-time-stretch__live-length");
            r.Add(length);
            UpdateLength();
            return r;
        }
    }
}
