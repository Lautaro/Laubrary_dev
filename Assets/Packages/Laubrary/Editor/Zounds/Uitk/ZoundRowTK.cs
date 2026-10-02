using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>What a Browser row asks of whoever shows it (the Browser, the Monitor tab): open an editor, and rebuild
    /// after a change that alters the list itself (a remove, a duplicate, a conversion, a rename).</summary>
    internal interface IZoundRowHost {
        void OpenZoundEditor(Zound zound);
        void ListChanged(Zound select = null);
    }

    /// <summary>
    /// The per-sound controls of a Browser row, as ZoundBrowserEditor draws them (T-0470): Edit (or Add for a missing
    /// sound, Convert for an audio file), Mute and Solo, the name box, Volume, Pitch and Chance, the action group (Route,
    /// Convert to Zequence, Duplicate, Remove) and the tags. Built once into a container; laid out either as a list row
    /// (<see cref="LayoutRow"/>) or as the grid mode's inspector panel (<see cref="LayoutGrid"/>). Every edit goes through
    /// the old code's own paths.
    /// </summary>
    internal class ZoundListRowTKParts {

        internal static readonly Color MuteOn = new Color32(107, 50, 48, 255);           // the sheet's "Warning" palette entry
        internal static readonly Color SoloOn = new Color(.14f, .34f, .14f, 1f);          // "Confirm" is not in the sheet: the old fallback

        readonly Zound zound;
        readonly IZoundRowHost host;
        readonly bool isClip, isMissing;
        readonly Button editButton;
        readonly ZuiToggleButton mute, solo;
        readonly TextField nameField;
        readonly ZuiSkinMinMax volume, pitch;
        readonly ZuiSkinSlider chance;
        readonly List<Button> rightButtons = new List<Button>();
        readonly Button tags;

        public ZoundListRowTKParts(Zound zound, IZoundRowHost host, VisualElement into, bool vertical) {
            this.zound = zound; this.host = host;
            var bs = ZoundsProject.Instance.browserSettings;
            isClip = zound.IsClipOrLocalZound();
            isMissing = !isClip && zound.id == 0;
            bool enabledFields = !isClip;   // DrawZoundSinglecolumn / DrawMulticolumn: GUI.enabled = !IsClipOrLocalZound

            if (bs.showOpenEditor) {
                editButton = MakeEditButton();
                editButton.AddToClassList("zs-iconbutton");
                editButton.SetEnabled(enabledFields);
                into.Add(Abs(editButton));
            }
            if (!isMissing && (bs.showMute || bs.showSolo)) {
                bool both = bs.showMute && bs.showSolo;
                ZUICornerMask mMask = !both ? ZUICornerMask.All : vertical ? ZUICornerMask.Top : ZUICornerMask.Left;
                ZUICornerMask sMask = !both ? ZUICornerMask.All : vertical ? ZUICornerMask.Bottom : ZUICornerMask.Right;
                if (vertical && !both) mMask = sMask = ZUICornerMask.None;
                if (bs.showMute) {
                    mute = ZS.Toggle("M", "Mute/Unmute", zound.mute, _ => { ZoundBrowserEditor<Zound>.ToggleMute(zound); Sync(); },
                                     "ZoundBtnFlatToggle", mMask, -1f, -1f, MuteOn);
                    mute.SetEnabled(enabledFields); into.Add(Abs(mute));
                }
                if (bs.showSolo) {
                    solo = ZS.Toggle("S", "Toggle Solo", zound.solo, _ => { ZoundBrowserEditor<Zound>.ToggleSolo(zound); Sync(); },
                                     "ZoundBtnFlatToggle", sMask, -1f, -1f, SoloOn);
                    solo.SetEnabled(enabledFields); into.Add(Abs(solo));
                }
            }
            if (!isMissing) {
                if (bs.showNameField) {
                    nameField = new TextField { isDelayed = true, value = zound.name, tooltip = "Rename this sound when you confirm the field." };
                    nameField.AddToClassList("zs-namefield"); nameField.AddToClassList("zs-rowname");
                    nameField.RegisterValueChangedCallback(e => Rename(e.newValue));
                    nameField.SetEnabled(enabledFields);
                    into.Add(Abs(nameField));
                }
                var vMode = bs.vpcShowSliderType ? ZuiSkinMinMax.LabelMode.LabelAndValues : ZuiSkinMinMax.LabelMode.ValuesOnly;
                if (bs.showVolume) {
                    volume = ZS.MinMax("Volume", zound.minVolume * 100f, zound.maxVolume * 100f, Zound.MinVolumeRange * 100f, Zound.MaxVolumeRange * 100f, "Volume",
                        (lo, hi) => ZoundsWindow.ModifyZoundsProject("change zound volume", () => {
                            zound.minVolume = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(lo / 100f);
                            zound.maxVolume = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(hi / 100f);
                        }), "MinMax", vMode, bs.vpcShowInputBoxes);
                    volume.SetEnabled(enabledFields); into.Add(Abs(volume));
                }
                if (bs.showPitch) {
                    pitch = ZS.MinMax("Pitch", zound.minPitch * 100f, zound.maxPitch * 100f, Zound.MinPitchRange * 100f, Zound.MaxPitchRange * 100f, "Pitch",
                        (lo, hi) => ZoundsWindow.ModifyZoundsProject("change zound pitch", () => {
                            zound.minPitch = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(lo / 100f);
                            zound.maxPitch = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(hi / 100f);
                        }), "MinMaxPitch", vMode, bs.vpcShowInputBoxes);
                    pitch.SetEnabled(enabledFields); into.Add(Abs(pitch));
                }
                if (bs.showChance) {
                    chance = ZS.Slider("Chance", zound.chance * 100f, Zound.MinChanceRange * 100f, Zound.MaxChanceRange * 100f, "Chance",
                        v => ZoundsWindow.ModifyZoundsProject("change zound chance", () => zound.chance = ZoundBrowserEditor<Zound>.RoundTo3DecimalPlaces(v / 100f)),
                        bs.vpcShowSliderType ? ZuiSkinSlider.LabelMode.LabelAndValue : ZuiSkinSlider.LabelMode.ValueOnly, null, "Chance");
                    chance.SetEnabled(enabledFields); into.Add(Abs(chance));
                }
            }
            MakeRightGroup(into, enabledFields);
            if (!isMissing && bs.showTags) {
                tags = new Button(() => TagsEditorWindow.OpenWindow(zound)) { text = BrowserTab.GetZoundTagsString(zound), tooltip = "Open the tag editor to assign or remove this sound's tags." };
                tags.AddToClassList("zs-tagsfield");
                tags.AddToClassList("zs-text-zounds-tags");
                tags.SetEnabled(enabledFields);
                into.Add(Abs(tags));
            }
        }

        static T Abs<T>(T e) where T : VisualElement { e.AddToClassList("zs-zound-row__positioned-control"); return e; }

        internal static void Place(VisualElement e, Rect r) {
            if (e == null) return;
            bool show = r.width > 1f && r.height > 0f;
            e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            e.style.left = r.x; e.style.top = r.y; e.style.width = Mathf.Max(0f, r.width); e.style.height = Mathf.Max(0f, r.height);
        }

        Button MakeEditButton() {
            if (isMissing)
                return ZequenceEditorWindowTK.IconButton("add-new", "Add as a new zound.", "ZoundBtnFlat", ZUICornerMask.None, -1f, -1f, () => {
                    ZoundBrowserEditor<Zound>.RemoveMissingZound(zound);
                    BrowserTab.OpenAddNewZoundMenu(zound.name);
                    host.ListChanged();
                });
            if (zound is ClipZound clip)
                return ZequenceEditorWindowTK.IconButton("convert", "Convert to Klip.", "ZoundBtnFlat", ZUICornerMask.None, -1f, -1f, () => {
                    if (EditorUtility.DisplayDialog("Convert to Klip: " + clip.name, "Convert this into audio clip a Klip?\n" + clip.name, "Convert", "Cancel")) {
                        BrowserTab.ConvertClipToKlipShared(clip);
                        host.ListChanged();
                    }
                });
            string icon = zound is Klip ? "open-editor-klip" : zound is Zequence ? "open-editor-zequence" : "open-editor";
            string tip = zound is Klip ? "Open Klip editor." : zound is Zequence ? "Open Zequence editor." : "Open editor.";
            return ZequenceEditorWindowTK.IconButton(icon, tip, "ZoundBtnFlat", ZUICornerMask.None, -1f, -1f, () => host.OpenZoundEditor(zound));
        }

        /// <summary>ZoundBrowserEditor.DrawRemoveButton: Route, Convert to Zequence, Duplicate, Remove, equal slots with gaps.
        /// A missing sound keeps only Remove; in Play mode the group is disabled, as is Convert for anything but a top-level Klip.</summary>
        void MakeRightGroup(VisualElement into, bool enabledFields) {
            var bs = ZoundsProject.Instance.browserSettings;
            bool rowEnabled = isMissing || (enabledFields && !Application.isPlaying);
            int count = 0;
            if (!isMissing && bs.showRouting) count++;
            if (!isMissing && bs.showConvertToZequence) count++;
            if (!isMissing && bs.showDuplicate) count++;
            if (bs.showRemove) count++;
            if (count == 0) return;
            int idx = 0;
            ZUICornerMask Mask(int i) => count == 1 ? ZUICornerMask.All : i == 0 ? ZUICornerMask.Left : i == count - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
            void AddBtn(Button b, bool en) { b.AddToClassList("zs-iconbutton"); b.SetEnabled(en); rightButtons.Add(b); into.Add(Abs(b)); idx++; }
            if (!isMissing) {
                if (bs.showRouting) {
                    var b = ZequenceEditorWindowTK.IconButton(zound.editor_hasManuallySetRouting ? "routing-on" : "routing-off", "Set manual routing.", "ZoundBtnFlat", Mask(idx), -1f, -1f,
                        () => ZoundBrowserEditor<Zound>.OpenManualRoutingDropdown(zound));
                    b.userData = "routing";
                    AddBtn(b, rowEnabled);
                }
                if (bs.showConvertToZequence) {
                    bool can = zound is Klip k && k.parentId == 0;
                    AddBtn(ZequenceEditorWindowTK.IconButton("convert-zequence", "Convert this Klip to Zequence.", "ZoundBtnFlat", Mask(idx), -1f, -1f, () => {
                        if (EditorUtility.DisplayDialog("Convert to Zequence: " + zound.name,
                            "Convert this Klip into a Zequence containing it as a local klip?\n" + zound.name, "Convert", "Cancel")) {
                            BrowserTab.ConvertKlipToZequenceShared((Klip)zound);
                            host.ListChanged();
                        }
                    }), rowEnabled && can);
                }
                if (bs.showDuplicate)
                    AddBtn(ZequenceEditorWindowTK.IconButton("duplicate", "Duplicate this zound.", "ZoundBtnFlat", Mask(idx), -1f, -1f, () => {
                        Zound copy = null;
                        BrowserTab.DuplicateZoundShared(zound, d => copy = d);
                        host.ListChanged(copy);
                    }), rowEnabled);
            }
            if (bs.showRemove)
                AddBtn(ZequenceEditorWindowTK.IconButton("remove", "Remove this zound.", "ZoundBtnFlat", Mask(idx), -1f, -1f, () => {
                    if (isMissing) { ZoundBrowserEditor<Zound>.RemoveMissingZound(zound); host.ListChanged(); }
                    else if (AudioAssetUtility.DisplayZoundRemoveDialog(zound)) { BrowserTab.RemoveZoundShared(zound); host.ListChanged(); }
                }), rowEnabled);
        }

        // ─────────────────────────── layouts ───────────────────────────

        void PlaceRightGroup(Rect rg) {
            if (rightButtons.Count == 0) return;
            float gap = ZS.ItemSpacing;
            float bw = (rg.width - gap * (rightButtons.Count - 1)) / rightButtons.Count;
            for (int i = 0; i < rightButtons.Count; i++) Place(rightButtons[i], new Rect(rg.x + i * (bw + gap), rg.y, bw, rg.height));
        }

        /// <summary>The list row: rects from BrowserTab.ComputeRowRects; Mute and Solo side by side, 1 px apart.</summary>
        public void LayoutRow(BrowserTab.ZoundListRowLayout layout) {
            Place(editButton, layout.editButtonRect);
            var ms = layout.muteSoloRect;
            if (mute != null && solo != null) {
                float half = (ms.width - 1f) * 0.5f;
                Place(mute, new Rect(ms.x, ms.y, half, ms.height));
                Place(solo, new Rect(ms.x + half + 1f, ms.y, ms.width - half - 1f, ms.height));
            }
            else { Place(mute, ms); Place(solo, ms); }
            Place(nameField, layout.nameInputRect);
            Place(volume, layout.volumeRect);
            Place(pitch, layout.pitchRect);
            Place(chance, layout.chanceRect);
            PlaceRightGroup(layout.rightGroupRect);
            Place(tags, layout.tagsInlineRect);
        }

        /// <summary>
        /// The grid inspector (DrawZoundFields with fillButtonHeight and twoRowFields): Mute over Solo (the sheet's 10 px
        /// gap between them) when the zone is 24 px or narrower; the fields split into two bottom-anchored rows of one
        /// line each (the upper row takes the extra one), each 4 px narrower than its share; the tags fill their own zone.
        /// </summary>
        public void LayoutGrid(Rect edit, Rect ms, Rect fields, Rect tagsRect, Rect remove) {
            var bs = ZoundsProject.Instance.browserSettings;
            Place(editButton, edit);
            if (mute != null && solo != null) {
                float gap = ZS.MediumSpacing;
                if (ms.width > 24f) {
                    float w = (ms.width - gap) / 2f;
                    Place(mute, new Rect(ms.x, ms.y, w, ms.height));
                    Place(solo, new Rect(ms.x + w + gap, ms.y, w, ms.height));
                }
                else {
                    float h = (ms.height - gap) / 2f;
                    Place(mute, new Rect(ms.x, ms.y, ms.width, h));
                    Place(solo, new Rect(ms.x, ms.y + h + gap, ms.width, h));
                }
            }
            else { Place(mute, ms); Place(solo, ms); }
            PlaceRightGroup(remove);
            Place(nameField, Rect.zero); Place(volume, Rect.zero); Place(pitch, Rect.zero); Place(chance, Rect.zero);
            if (!isMissing && fields.width > 1f) {
                bool tagsSeparate = tagsRect.width > 1f;
                var order = new List<VisualElement>();
                if (bs.showNameField) order.Add(nameField);
                if (bs.showVolume) order.Add(volume);
                if (bs.showPitch) order.Add(pitch);
                if (bs.showChance) order.Add(chance);
                int count = order.Count + (bs.showTags && !tagsSeparate ? 1 : 0);
                if (count > 0) {
                    const float lh = 18f, rowGap = 2f;
                    int row0 = Mathf.CeilToInt(count / 2f), row1 = count - row0;
                    float fw0 = row0 > 0 ? (fields.width - 4f) / row0 : 0f;
                    float fw1 = row1 > 0 ? (fields.width - 4f) / row1 : 0f;
                    float bottomY = fields.yMax - lh;
                    float row1Y = row1 > 0 ? bottomY : fields.y;
                    float row0Y = row1 > 0 ? row1Y - lh - rowGap : bottomY;
                    for (int i = 0; i < order.Count; i++) {
                        if (i < row0) Place(order[i], new Rect(fields.x + i * fw0, row0Y, fw0 - 4f, lh));
                        else Place(order[i], new Rect(fields.x + (i - row0) * fw1, row1Y, fw1 - 4f, lh));
                    }
                    if (bs.showTags && !tagsSeparate) {
                        int i = order.Count;
                        var r = i < row0 ? new Rect(fields.x + i * fw0, row0Y, fw0 - 4f, lh) : new Rect(fields.x + (i - row0) * fw1, row1Y, fw1 - 4f, lh);
                        Place(tags, r);
                    }
                }
                if (bs.showTags && tagsSeparate) {
                    tags?.AddToClassList("zs-tagsfield--multiline");
                    Place(tags, new Rect(tagsRect.x, tagsRect.y, tagsRect.width, Mathf.Max(TagsHeight(tagsRect.width), 1f)));
                }
            }
            else Place(tags, Rect.zero);
        }

        /// <summary>The tags text's wrapped height at <paramref name="width"/> (UpdateInspectorHeight's measure).</summary>
        public float TagsHeight(float width) {
            if (tags == null || width <= 0f) return 0f;
            return tags.MeasureTextSize(tags.text, width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
        }

        // ─────────────────────────── refresh ───────────────────────────

        /// <summary>Values from the sound (an edit elsewhere, an undo).</summary>
        public void Sync() {
            if (mute != null) { mute.SetValueWithoutNotify(zound.mute); ZS.ApplyOnColor(mute, MuteOn); }
            if (solo != null) { solo.SetValueWithoutNotify(zound.solo); ZS.ApplyOnColor(solo, SoloOn); }
            if (nameField != null && nameField.focusController?.focusedElement != nameField) nameField.SetValueWithoutNotify(zound.name);
            volume?.SetValuesWithoutNotify(zound.minVolume * 100f, zound.maxVolume * 100f);
            pitch?.SetValuesWithoutNotify(zound.minPitch * 100f, zound.maxPitch * 100f);
            chance?.SetValueWithoutNotify(zound.chance * 100f);
            if (tags != null) tags.text = BrowserTab.GetZoundTagsString(zound);
            foreach (var b in rightButtons) {
                if (!"routing".Equals(b.userData)) continue;
                var img = b.Q<Image>();
                string icon = zound.editor_hasManuallySetRouting ? "routing-on" : "routing-off";
                if (img != null) img.image = ZUI.FindIcon(icon) ?? Resources.Load<Texture>("ZoundsWindowIcons/" + icon);
            }
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
            host.ListChanged();
        }
    }

    /// <summary>
    /// The UI Toolkit twin of one Browser list row (T-0470): ZoundListItemView.Draw plus the list half of
    /// ZoundBrowserEditor.DrawZoundSinglecolumn. Every rect comes from the old code's own
    /// <see cref="BrowserTab.ComputeRowRects"/>, so the two rows agree.
    ///
    /// Drawing order is the old one: the play pulse and the mute/solo wash behind, then the controls, then the mute/solo
    /// stripe on top. <see cref="Sync"/> brings values up to date; <see cref="Live"/> moves the play pulse and the name
    /// button's tint while something plays. With tags on their own row, the row grows to hold them.
    /// </summary>
    internal class ZoundListRowTK : VisualElement {

        internal static readonly Color MuteWash = new Color32(107, 50, 48, 255);          // PaletteColor("Warning", …)
        internal static readonly Color SoloWash = new Color(0f, 0.7f, 0.2f, 1f);          // PaletteColor("Confirm", fallback)
        internal static readonly Color ZoundBtnText = new Color32(217, 207, 181, 255);

        readonly Zound zound;
        readonly IZoundRowHost host;
        readonly Func<BrowserTab.ZoundListRowLayout> layoutOf;
        readonly bool isClip, isMissing, tagsOwnRow;
        readonly VisualElement pulseFill, pulseBorder, wash, stripe, zeqStripe;
        readonly Button nameButton, tagsBelow;
        readonly Label missingLabel;
        readonly ZoundListRowTKParts parts;
        Color nameTint = Color.white;

        public Zound Zound => zound;

        public ZoundListRowTK(Zound zound, IZoundRowHost host, Func<BrowserTab.ZoundListRowLayout> layoutOf) {
            this.zound = zound; this.host = host; this.layoutOf = layoutOf;
            var bs = ZoundsProject.Instance.browserSettings;
            isClip = zound.IsClipOrLocalZound();
            isMissing = !isClip && zound.id == 0;
            tagsOwnRow = !isMissing && bs.showTags && bs.tagsOnOwnRow;
            AddToClassList("zs-zound-row__list-row");
            AddToClassList("zs-zound-row__list-height");

            pulseFill = Plain(); pulseBorder = Plain();
            pulseBorder.AddToClassList("zs-zound-row__list-pulse-border");
            wash = Plain();
            Add(pulseFill); Add(pulseBorder); Add(wash);

            if (isMissing) {
                missingLabel = new Label(zound.name) { pickingMode = PickingMode.Ignore };
                missingLabel.AddToClassList("zs-lbl");
                missingLabel.AddToClassList("zs-zound-row__list-missing-label");
                Add(missingLabel);
            }
            else {
                nameButton = ZS.Button(zound.name, zound.name + ": Left click to play. Right click to open edit mode. Middle click or Alt left click to copy the name to clipboard.",
                                       "ZoundBtn", null, ZUICornerMask.None, -1f, -1f);
                nameButton.clickable = null;
                nameButton.AddToClassList("zs-zound-row__list-name-button");
                nameButton.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) e.StopPropagation(); });
                nameButton.RegisterCallback<PointerUpEvent>(OnNameClick);
                Add(nameButton);
            }

            var fields = new VisualElement { pickingMode = PickingMode.Ignore };
            fields.AddToClassList("zs-zound-row__list-fields");
            Add(fields);
            parts = new ZoundListRowTKParts(zound, host, fields, vertical: false);

            // Tags on their own row: a full-width wrapping strip below row 1, then the sheet's "V Zounds Tag Gap" (4.24 px).
            if (tagsOwnRow) {
                tagsBelow = new Button(() => TagsEditorWindow.OpenWindow(zound)) { text = BrowserTab.GetZoundTagsString(zound), tooltip = "Open the tag editor to assign or remove this sound's tags." };
                tagsBelow.AddToClassList("zs-tagsfield"); tagsBelow.AddToClassList("zs-tagsfield--multiline"); tagsBelow.AddToClassList("zs-text-zounds-tags");
                tagsBelow.AddToClassList("zs-zound-row__list-tags-below");
                tagsBelow.SetEnabled(!isClip);
                fields.Add(tagsBelow);
            }

            stripe = Plain(); zeqStripe = Plain();
            Add(stripe); Add(zeqStripe);

            RegisterCallback<GeometryChangedEvent>(_ => Layout());
            Sync();
            Live();
        }

        static VisualElement Plain() {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("zs-zound-row__plain-element");
            return e;
        }

        const float TagGap = 4.24f;   // the sheet's "V Zounds Tag Gap" (vertical spacing 10 × 0.424)

        void Layout() {
            float w = resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0f) return;
            var layout = layoutOf();
            var rowRect = new Rect(0f, 0f, w, BrowserTab.ROW_HEIGHT);
            BrowserTab.ComputeRowRects(ref layout, rowRect, isMissing, ZS.ItemSpacing);
            Rect item = rowRect;
            float total = BrowserTab.ROW_HEIGHT;
            if (tagsBelow != null) {
                float th = tagsBelow.MeasureTextSize(tagsBelow.text, w, MeasureMode.Exactly, 0, MeasureMode.Undefined).y;
                if (th > 1f) {
                    var tr = new Rect(0f, BrowserTab.ROW_HEIGHT + BrowserTab.MULTICOLUMN_V_GAP, w, th);
                    ZoundListRowTKParts.Place(tagsBelow, tr);
                    item = new Rect(0f, 0f, w, tr.yMax);
                    total = tr.yMax + TagGap;
                }
                else ZoundListRowTKParts.Place(tagsBelow, Rect.zero);
            }
            if (!Mathf.Approximately(resolvedStyle.height, total)) style.height = total;
            ZoundListRowTKParts.Place(pulseFill, item); ZoundListRowTKParts.Place(pulseBorder, item); ZoundListRowTKParts.Place(wash, item);
            ZoundListRowTKParts.Place(nameButton, layout.nameButtonRect);
            ZoundListRowTKParts.Place(missingLabel, layout.nameButtonRect);
            parts.LayoutRow(layout);
            ZoundListRowTKParts.Place(stripe, new Rect(item.x + 1f, item.y, item.width - 2f, 2f));
            ZoundListRowTKParts.Place(zeqStripe, new Rect(item.x + 1f, item.yMax - 1.5f, item.width - 2f, 1.5f));
            Sync();
        }

        /// <summary>Values and the mute/solo marks, from the sound (an edit elsewhere, an undo).</summary>
        public void Sync() {
            parts.Sync();
            if (nameButton != null && nameButton.text != zound.name) nameButton.text = zound.name;
            if (tagsBelow != null) tagsBelow.text = BrowserTab.GetZoundTagsString(zound);
            bool ms = zound.mute || zound.solo;
            Color c = zound.mute ? MuteWash : SoloWash;
            wash.style.visibility = ms ? Visibility.Visible : Visibility.Hidden;
            wash.style.backgroundColor = c;
            stripe.style.visibility = ms ? Visibility.Visible : Visibility.Hidden;
            stripe.style.backgroundColor = c;
            bool zeqMark = zound is Zequence zeq && zeq.HasLocalMuteOrSoloEntry();
            zeqStripe.style.visibility = zeqMark ? Visibility.Visible : Visibility.Hidden;
            zeqStripe.style.backgroundColor = Color.yellow;
        }

        /// <summary>The play pulse and the name button's tint (ZoundListItemView), from the engine's current tokens.</summary>
        public void Live() {
            Color fill = Color.clear, border = Color.clear, tint = Color.white;
            if (!isMissing) {
                ZoundBrowserPlaybackVisuals.TryGetAnyInstanceToken(zound, out var token);
                ZoundBrowserPlaybackVisuals.UpdateZoundButtonPulse(zound, isClip, token != null, token);
                float intensity = ZUI.GetPulseIntensity(ZoundBrowserPlaybackVisuals.ZoundPulseKey(zound));
                if (intensity > 0f && token != null) {
                    var bc = isClip ? ZoundsEditorColors.clipFlashColorEnd
                           : token.audioSource.mute ? ZoundsEditorColors.flashColorEndMuted : ZoundsEditorColors.flashColorEnd;
                    fill = new Color(bc.r, bc.g, bc.b, 0.25f * intensity);
                    border = new Color(bc.r, bc.g, bc.b, intensity);
                }
                if (token != null) {
                    if (token.state == ZoundToken.State.Paused) tint = new Color(0.9f, 0.5f, 0.9f, 1f);
                    else if (token.audioSource.volume < Mathf.Epsilon) tint = new Color(0.9f, 0.5f, 0.1f, 1f);
                    else tint = isClip ? ZoundsEditorColors.clipFlashColorStart : ZoundsEditorColors.flashColorStart;
                }
                else if (isClip) tint = Color.cyan;
                if (intensity > 0f) tint = Color.Lerp(tint, Color.black, intensity * 0.6f);
            }
            pulseFill.style.backgroundColor = fill;
            pulseBorder.style.borderTopColor = pulseBorder.style.borderBottomColor = pulseBorder.style.borderLeftColor = pulseBorder.style.borderRightColor = border;
            if (nameButton != null && tint != nameTint) { nameTint = tint; TintButton(nameButton, tint, ZoundBtnText); }
        }

        /// <summary>GUI.color over a sheet button: its background image, text and border are all multiplied by it.</summary>
        internal static void TintButton(VisualElement b, Color tint, Color textColor, Color? borderColor = null) {
            if (tint == Color.white) {
                b.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                b.style.color = StyleKeyword.Null;
                b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = StyleKeyword.Null;
                return;
            }
            b.style.unityBackgroundImageTintColor = tint;
            b.style.color = textColor * tint;
            Color bc = borderColor ?? new Color32(139, 114, 83, 255);
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = bc * tint;
        }

        void OnNameClick(PointerUpEvent e) {
            if (!nameButton.ContainsPoint(e.localPosition)) return;
            var bs = ZoundsProject.Instance.browserSettings;
            if (e.button == 0) {
                if (e.altKey) ZoundBrowserPlaybackVisuals.CopyToClipboard(zound.name);
                else if (e.ctrlKey) InfoViewWindow.OpenWindow(zound);
                else ZoundBrowserPlaybackVisuals.PlayOrStopFromBrowser(zound);
            }
            else if (e.button == 1) host.OpenZoundEditor(zound);
            else if (e.button == 2) ZoundBrowserPlaybackVisuals.CopyToClipboard(zound.name);
        }
    }
}
