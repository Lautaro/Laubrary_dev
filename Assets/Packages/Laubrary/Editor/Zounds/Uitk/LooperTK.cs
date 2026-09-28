using Laubrary.Zounds.Dsp;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Klip editor's Looper row (T-0476): the Looper toggle and, while it is on, the crossmix length as one two-handle
    /// range (the same fixed-or-range control the Klip's volume and pitch use: both handles together = one fixed length,
    /// apart = every loop draws its own length within them), bounded by the longest crossmix this loop can have, which
    /// is half the loop. Edits go through the project's undo paths and are heard at once by a Looper already playing.
    /// The waveform shows which parts of the start and the end the crossmix uses.
    /// </summary>
    public class LooperTK : VisualElement {

        const float RowH = 20f;
        readonly Klip klip;
        float builtLimit = -1f;
        bool builtEnabled;
        bool dragging;

        public LooperTK(Klip klip) {
            this.klip = klip;
            style.flexShrink = 0;
            Build();
            // The limit follows the loop's length, so a trim edit elsewhere in the window re-bounds the range.
            schedule.Execute(Sync).Every(250);
        }

        ZoundLoop Loop {
            get { if (klip.loop == null) klip.loop = new ZoundLoop(); return klip.loop; }
        }

        /// <summary>The loop's length in seconds: the trimmed region, or the whole source when trim is off.</summary>
        float LoopSeconds() {
            var clip = ZoundSapPlayback.LoadSourceClip(klip);
            if (clip == null) return 0f;
            float from = klip.trimEnabled ? klip.trimStart : 0f;
            float to = klip.trimEnabled && klip.trimEnd > klip.trimStart ? Mathf.Min(klip.trimEnd, clip.length) : clip.length;
            return Mathf.Max(0f, to - from);
        }

        void Sync() {
            if (dragging || panel == null) return;
            float limit = ZoundLoop.MaxCrossmix(LoopSeconds());
            if (Loop.enabled != builtEnabled || Mathf.Abs(limit - builtLimit) > 1e-4f) Rebuild();
        }

        void Rebuild() { Clear(); Build(); }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }

        void Build() {
            var lp = Loop;
            float limit = ZoundLoop.MaxCrossmix(LoopSeconds());
            builtLimit = limit; builtEnabled = lp.enabled;

            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = RowH; r.style.flexShrink = 0;
            r.Add(ZS.Toggle("Looper", lp.enabled
                    ? "Stop looping: the sound plays its (trimmed) source once and ends, like any Klip."
                    : "Make this a Looper: it plays its trimmed region over and over until it is stopped. The trim points are the loop points. Applies from the next play.",
                lp.enabled, on => {
                    ZoundsWindow.ModifyAndSaveZoundsProject(on ? "make looper" : "stop looper", () => lp.enabled = on);
                    Rebuild();
                }, "RichToggle", ZUICornerMask.All, 64f, RowH));

            if (lp.enabled && limit > 0f) {
                r.Add(Gap(8f));
                lp.Effective(LoopSeconds(), out float lo, out float hi);
                var undo = ZS.DragUndo(r, "looper crossmix", () => { dragging = false; Sync(); });
                r.Add(ZS.MinMax("Crossmix s", lo, hi, 0f, limit,
                    "How long the old copy fades out while the new one fades in, near the end of each pass. 0 = the loop simply starts over at the end. " +
                    "Handles together: the same length every time. Apart: every loop picks its own length between them. " +
                    "Longest possible: half the loop (" + limit.ToString("0.00") + " s). Heard at once, even on a Looper already playing." +
                    (TimeStretchTK.StretcherRuns(klip, out string why)
                        ? " NOTE: the live stretcher runs for this sound (" + why + "), and through it the loop wraps without the crossmix."
                        : ""),
                    (a, b) => {
                        dragging = true;
                        undo();
                        lp.crossmixMin = a; lp.crossmixMax = b;
                        EditorUtility.SetDirty(ZoundsProject.Instance);
                        SapVoiceRegistry.PushLoop(klip);
                    },
                    "Default", ZuiSkinMinMax.LabelMode.LabelAndValues, false, 240f, RowH - 2f));
            }
            var flex = new VisualElement(); flex.style.flexGrow = 1; r.Add(flex);
            Add(r);
        }
    }
}
