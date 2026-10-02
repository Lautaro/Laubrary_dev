using System.Linq;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// UI Toolkit twin of ZoundInspector.DrawSimple (T-0461): Mute/Solo (unless the sound is local to a Zequence), Name,
    /// Volume, Pitch, Chance and Tags on one row, in equal columns. The column arithmetic is the old one exactly: the
    /// Mute/Solo pair takes 44 px, the rest is split into equal columns and each control is 4 px narrower than its
    /// column. Every edit goes through the same project-modification path as the old row, so Undo and saving behave
    /// identically.
    /// </summary>
    public class ZoundFieldsRowTK : VisualElement {

        readonly Zound zound;
        readonly bool isLocal, drawName, drawTags;
        readonly ZuiToggleButton mute, solo;
        readonly TextField nameField;
        readonly ZuiSkinMinMax volume, pitch;
        readonly ZuiSkinSlider chance;
        // The fixed boost into the effects (T-0521, Klips only): a number you drag, backed by a whole-number field in
        // tenths (10..100) so ZUI's drag gives one step per few pixels and the value always has exactly one decimal.
        readonly Label boost;
        readonly IntegerField boostTenths;
        const float BoostW = 46f;
        readonly Button tags;
        readonly System.Action onRenamed;

        static readonly Color MuteOn = new Color(.70f, .42f, .08f, 1f), SoloOnFallback = new Color(.14f, .34f, .14f, 1f);

        public ZoundFieldsRowTK(Zound zound, bool isLocal, System.Action onRenamed = null, bool drawName = true, bool drawTags = true) {
            this.zound = zound; this.isLocal = isLocal; this.drawName = drawName; this.drawTags = drawTags; this.onRenamed = onRenamed;
            style.height = UnityEditor.EditorGUIUtility.singleLineHeight;
            AddToClassList("zs-zound-fields-row__root");
            var bs = ZoundsProject.Instance.browserSettings;
            SetEnabled(!(zound is ClipZound));

            if (!isLocal) {
                // The old row takes Mute's colour from the sheet's "Warning" palette entry, and Solo's from "Confirm",
                // which the Zounds sheet does not define (so its built-in fallback green is what shows).
                mute = ZS.Toggle("M", "Mute/Unmute", zound.mute, _ => ToggleMute(), "ZoundBtnFlatToggle", ZUICornerMask.Left, -1f, -1f, new Color32(107, 50, 48, 255));
                solo = ZS.Toggle("S", "Toggle Solo", zound.solo, _ => ToggleSolo(), "ZoundBtnFlatToggle", ZUICornerMask.Right, -1f, -1f, SoloOnFallback);
                Add(Abs(mute)); Add(Abs(solo));
            }
            if (drawName) {
                nameField = new TextField { isDelayed = true, value = zound.name, tooltip = "Rename this sound when you confirm the field." };
                nameField.AddToClassList("zs-namefield");
                nameField.RegisterValueChangedCallback(e => Rename(e.newValue));
                Add(Abs(nameField));
            }
            var vMode = bs.vpcShowSliderType ? ZuiSkinMinMax.LabelMode.LabelAndValues : ZuiSkinMinMax.LabelMode.ValuesOnly;
            volume = ZS.MinMax("Volume", zound.minVolume * 100f, zound.maxVolume * 100f, Zound.MinVolumeRange * 100f, Zound.MaxVolumeRange * 100f,
                               "Choose the loudness range; each play draws a value between these limits.", (lo, hi) => ZoundsWindow.ModifyZoundsProject("change zound volume", () => {
                                   zound.minVolume = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(lo / 100f);
                                   zound.maxVolume = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(hi / 100f);
                               }), "MinMax", vMode, bs.vpcShowInputBoxes);
            pitch = ZS.MinMax("Pitch", zound.minPitch * 100f, zound.maxPitch * 100f, Zound.MinPitchRange * 100f, Zound.MaxPitchRange * 100f,
                              "Choose the pitch range; each play draws a value between these limits.", (lo, hi) => ZoundsWindow.ModifyZoundsProject("change zound pitch", () => {
                                  zound.minPitch = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(lo / 100f);
                                  zound.maxPitch = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(hi / 100f);
                              }), "MinMaxPitch", vMode, bs.vpcShowInputBoxes);
            chance = ZS.Slider("Chance", zound.chance * 100f, Zound.MinChanceRange * 100f, Zound.MaxChanceRange * 100f, "Set the percentage chance that a trigger plays this sound.",
                               v => ZoundsWindow.ModifyZoundsProject("change zound chance", () => zound.chance = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(v / 100f)),
                               bs.vpcShowSliderType ? ZuiSkinSlider.LabelMode.LabelAndValue : ZuiSkinSlider.LabelMode.ValueOnly, null, "Chance");
            Add(Abs(volume)); Add(Abs(pitch)); Add(Abs(chance));
            if (zound is Klip klip) {
                boostTenths = new IntegerField { value = Tenths(klip.BoostApplied) };
                boostTenths.style.display = DisplayStyle.None;   // never shown: the label is the control
                Add(boostTenths);
                boost = new Label(BoostText(klip.BoostApplied)) {
                    tooltip = "Boost: how much louder the sound goes into its effects, from x1 (as recorded) to x10, in tenths. " +
                              "It multiplies Drive, whatever moves Drive. Drag left or right to change it (Shift for fine, Ctrl for coarse). " +
                              "Plays already under way follow it at once.",
                };
                boost.AddToClassList("zs-boost");
                Add(Abs(boost));
                ZuiScrub.AttachToLabel(boost, boostTenths, 10, 100);
                boostTenths.RegisterValueChangedCallback(e => {
                    int t = Mathf.Clamp(e.newValue, 10, 100);
                    if (t != e.newValue) boostTenths.SetValueWithoutNotify(t);
                    float v = t / 10f;
                    boost.text = BoostText(v);
                    if (Mathf.Approximately(klip.boost, v)) return;
                    ZoundsWindow.ModifyZoundsProject("change zound boost", () => klip.boost = v);
                    Dsp.SapVoiceRegistry.SetBoostLive(klip);
                });
            }
            if (drawTags) {
                tags = new Button(() => TagsEditorWindow.OpenWindow(zound)) { text = BrowserTab.GetZoundTagsString(zound), tooltip = "Open the tag editor to organise and filter this sound." };
                tags.AddToClassList("zs-tagsfield");
                tags.AddToClassList("zs-text-zounds-tags");
                Add(Abs(tags));
            }
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        static int Tenths(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 10f), 10, 100);
        /// <summary>Always one decimal ("x1.0" .. "x10.0"); the label's width is fixed, so no length ever moves anything.</summary>
        static string BoostText(float v) => "x" + v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        static VisualElement Abs(VisualElement e) { e.AddToClassList("zs-zound-fields-row__positioned-control"); return e; }

        void Place(VisualElement e, float x, float w) { if (e == null) return; e.style.left = x; e.style.width = Mathf.Max(0f, w); }

        void Layout() {
            float width = resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) return;
            int fieldCount = 3 + (drawName ? 1 : 0) + (drawTags ? 1 : 0);
            float muteSoloWidth = isLocal ? 0f : 44f;
            float fieldWidth = (width - muteSoloWidth) / fieldCount;
            if (!isLocal) {
                const float gap = 1f;
                float half = (muteSoloWidth - gap) * 0.5f;
                Place(mute, 0f, half);
                Place(solo, half + gap, muteSoloWidth - half - gap);
            }
            float x = muteSoloWidth, w = fieldWidth - 4f;
            if (drawName) { Place(nameField, x, w); x += fieldWidth; }
            if (boost != null) {
                // Beside the volume range, inside its column, so the other columns keep their places.
                Place(volume, x, Mathf.Max(0f, w - BoostW - 2f));
                Place(boost, x + Mathf.Max(0f, w - BoostW), BoostW);
            }
            else Place(volume, x, w);
            x += fieldWidth;
            Place(pitch, x, w); x += fieldWidth;
            Place(chance, x, w); x += fieldWidth;
            if (drawTags) Place(tags, x, w);
        }

        /// <summary>Brings every control up to date with the sound (an edit made elsewhere, an undo).</summary>
        public void Sync() {
            if (mute != null) { mute.SetValueWithoutNotify(zound.mute); ZS.ApplyOnColor(mute, new Color32(107, 50, 48, 255)); }
            if (solo != null) { solo.SetValueWithoutNotify(zound.solo); ZS.ApplyOnColor(solo, SoloOnFallback); }
            if (nameField != null && nameField.focusController?.focusedElement != nameField) nameField.SetValueWithoutNotify(zound.name);
            volume.SetValuesWithoutNotify(zound.minVolume * 100f, zound.maxVolume * 100f);
            pitch.SetValuesWithoutNotify(zound.minPitch * 100f, zound.maxPitch * 100f);
            chance.SetValueWithoutNotify(zound.chance * 100f);
            if (boost != null && zound is Klip bk) { boostTenths.SetValueWithoutNotify(Tenths(bk.BoostApplied)); boost.text = BoostText(bk.BoostApplied); }
            if (tags != null) tags.text = BrowserTab.GetZoundTagsString(zound);
        }

        void Rename(string newName) {
            newName = ZoundDictionary.EnsureUniqueZoundName(newName, zound);
            ZoundsWindow.ModifyZoundsProject("rename zound", () => {
                zound.name = newName;
                if (ZoundEngine.IsInitialized()) ZoundDictionary.ValidateZoundRuntime(zound);
                var lib = ZoundsProject.Instance.zoundLibrary;
                if (zound is Klip k && lib.klips.Contains(k)) lib.klips = lib.klips.OrderBy(it => it.name).ToList();
                else if (zound is Zequence z && lib.zequences.Contains(z)) lib.zequences = lib.zequences.OrderBy(it => it.name).ToList();
            });
            onRenamed?.Invoke();
        }

        void ToggleSolo() {
            ZoundsWindow.ModifyZoundsProject("solo zound", () => {
                zound.solo = !zound.solo;
                if (zound.solo) zound.mute = false;
                ZoundsProject.Instance.zoundLibrary.soloStatusNeedsUpdate = true;
            });
            Sync();
        }

        void ToggleMute() {
            ZoundsWindow.ModifyZoundsProject("mute zound", () => {
                zound.mute = !zound.mute;
                if (zound.mute) zound.solo = false;
                ZoundsProject.Instance.zoundLibrary.soloStatusNeedsUpdate = true;
            });
            Sync();
        }
    }
}
