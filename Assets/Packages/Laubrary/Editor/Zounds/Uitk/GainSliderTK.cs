using System;
using System.Globalization;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A Klip's Gain: one fixed factor on how loud the sound goes into its effects, 50 % to 800 %, as a slider (owner,
    /// 2026-10-10: "a static overall scaler ... a slider"). The track is logarithmic (each doubling is the same distance,
    /// so 100 % sits a quarter of the way along), the value reads as a whole percent, a tick marks 100 %, a double-click
    /// returns to it and Ctrl+click types a value. One drag is one Undo step; a shared sound is first handed to the
    /// edit guard (a copy, per Settings) through <c>guard</c>. Plays under way follow at once. The same control in the
    /// Klip editor's top row and on a local track's left column.
    /// </summary>
    internal static class GainSliderTK {

        static readonly float Lo = Mathf.Log(Klip.MinGain, 2f), Hi = Mathf.Log(Klip.MaxGain, 2f);

        /// <summary>The slider's position for a gain (log2 of it).</summary>
        internal static float ToSlider(float gain) => Mathf.Log(Mathf.Clamp(gain, Klip.MinGain, Klip.MaxGain), 2f);
        /// <summary>The gain at a slider position, rounded to a whole percent.</summary>
        internal static float ToGain(float s) => Mathf.Clamp(Mathf.Round(Mathf.Pow(2f, s) * 100f) / 100f, Klip.MinGain, Klip.MaxGain);
        internal static string Percent(float gain) => Mathf.RoundToInt(gain * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        const string Tip = "Gain: how loud this sound goes into its effects, from 50 % to 800 % of the level as recorded (100 %). " +
                           "One fixed factor for the whole sound; the waveform is drawn with it, and red marks along its edges show where the audio then goes past full scale. " +
                           "Volume is applied after the effects. Drag to change it, double-click for 100 %, Ctrl+click to type a value. Plays already under way follow at once.";

        /// <param name="klip">The sound whose Gain this edits (read again on every edit: after a copy-on-edit swap it is the copy).</param>
        /// <param name="guard">Before the first change of an edit: false cancels it (a shared sound's copy-on-edit). Null: no guard.</param>
        /// <param name="onEdited">After a change has been written (a host's refresh).</param>
        internal static ZuiSkinSlider Create(Func<Klip> klip, Func<bool> guard, Action onEdited, ZuiSkinSlider.LabelMode mode, float width, float height) {
            ZuiSkinSlider s = null;
            bool pressOpen = false, pressOk = false;
            void Apply(float sliderValue) {
                var k = klip();
                if (k == null) return;
                float g = ToGain(sliderValue);
                s.SetValueWithoutNotify(ToSlider(g));
                if (Mathf.Approximately(k.BoostApplied, g) && Mathf.Approximately(k.boost, g)) return;
                ZoundsWindow.ModifyZoundsProject("change zound gain", () => k.boost = g);
                Dsp.SapVoiceRegistry.SetBoostLive(k);
                onEdited?.Invoke();
            }
            void EndPress() {
                if (!pressOpen) return;
                pressOpen = false;
                if (pressOk) ZoundsWindow.EndDragUndo();
                pressOk = false;
            }
            s = new ZuiSkinSlider("Gain", ToSlider(klip()?.BoostApplied ?? 1f), Lo, Hi, Tip,
                v => {
                    if (!pressOk) { var k = klip(); s.SetValueWithoutNotify(ToSlider(k != null ? k.BoostApplied : 1f)); return; }
                    Apply(v);
                },
                mode, 0f, v => Percent(ToGain(v)),
                onBeforeMutate: () => {
                    if (pressOpen) return;
                    pressOpen = true;
                    pressOk = guard == null || guard();
                    if (pressOk) ZoundsWindow.BeginDragUndo("change zound gain");
                });
            s.RegisterCallback<PointerUpEvent>(_ => EndPress(), TrickleDown.TrickleDown);
            s.RegisterCallback<PointerCaptureOutEvent>(_ => EndPress(), TrickleDown.TrickleDown);
            s.WithDefaultMark();
            s.WithTypeIn(text => {
                text = text.Replace("%", "").Replace("×", "").Replace("x", "").Trim();
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float pct) || pct <= 0f) return null;
                return ToSlider(pct / 100f);
            }, v => {
                if (guard != null && !guard()) { var k0 = klip(); s.SetValueWithoutNotify(ToSlider(k0 != null ? k0.BoostApplied : 1f)); return; }
                Apply(v);
            });
            s.AddToClassList("zs-slider-default");
            s.AddToClassList("zs-gain");
            ZS.Size(s, width, height);
            return s;
        }

        /// <summary>Brings the slider up to date with the sound (an undo, an edit made elsewhere).</summary>
        internal static void Sync(ZuiSkinSlider s, Klip k) { if (s != null && k != null) s.SetValueWithoutNotify(ToSlider(k.BoostApplied)); }
    }
}
