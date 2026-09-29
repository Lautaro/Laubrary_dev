using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using W = Laubrary.Zounds.Uitk.ZequenceEditorWindowTK;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// One entry card of the Zequence twin (T-0469), laid out with the old window's rect maths (DrawEntry /
    /// DrawEntryLeftSection / DrawEntryRightSection / DrawEntryGroup): a plain entry — name, play, duration, the V/P/C
    /// sliders (with override toggles for a shared entry), the delay slider, the waveform strip with its volume envelope,
    /// playheads and flash, the delay labels and the button column — or a local Zequence as a group with its own header,
    /// fields, buttons, delay, master envelope and, when expanded, its child entries and add buttons.
    /// </summary>
    public class ZequenceEntryTK : VisualElement {

        readonly W win;
        readonly CompositeZound parent;
        readonly CompositeZound.ZoundEntry entry;
        readonly int index;
        readonly float parentPitch, parentDelay;
        readonly bool isGroupChild, found;
        readonly Zound zound;
        readonly List<Action<float>> layouts = new List<Action<float>>();
        readonly VisualElement flash;
        Rect flashRect;

        static readonly Color DurationBlue = new Color32(121, 183, 255, 255);
        static readonly Color MuteOn = new Color32(107, 50, 48, 255), SoloOn = new Color(.14f, .34f, .14f, 1f);

        float LH => EditorGUIUtility.singleLineHeight;
        const float FieldBoxWidth = 50f;   // EditorGUIUtility.fieldWidth at the time the old right section is drawn

        public ZequenceEntryTK(W win, CompositeZound parent, CompositeZound.ZoundEntry entry, int index, float parentPitch, float parentDelay, bool darker, bool isGroupChild) {
            this.win = win; this.parent = parent; this.entry = entry; this.index = index;
            this.parentPitch = parentPitch; this.parentDelay = parentDelay; this.isGroupChild = isGroupChild;
            style.flexShrink = 0;
            found = parent.TryGetEntryZound(entry, out zound);

            // Height: a plain entry is fixed; a local Zequence group grows with its master envelope and its children.
            float h = W.EntryHeight;
            if (found && entry.local && zound is CompositeZound comp) {
                h = W.GroupHeaderHeight;
                if (entry.editor_foldoutExpanded) h += comp.zoundEntries.Count * (W.EntryHeight + 4f) + LH + 10f;
                if (comp is Zequence) h += entry.volumeEnvelope.enabled ? LH * 4f : LH;
            }
            style.height = h;

            // Background band.
            var bg = new VisualElement { pickingMode = PickingMode.Ignore };
            bg.style.position = Position.Absolute; bg.style.top = 0; bg.style.bottom = 0;
            if (isGroupChild) {
                // The old bgRect: x + (offset − 2), width − (offset − 4): starts 8 in, ends 2 past the right edge.
                bg.style.left = W.GroupEntryLeftOffset - 2f; bg.style.right = -2f;
                bg.style.backgroundColor = darker ? new Color(0.25f, 0.25f, 0.25f, 0.4f) : new Color(0.45f, 0.45f, 0.45f, 0.4f);
            }
            else {
                bg.style.left = 0; bg.style.right = 0;
                bg.style.backgroundColor = darker ? new Color(0.25f, 0.25f, 0.30f, 0.22f) : new Color(0.35f, 0.35f, 0.42f, 0.15f);
            }
            Add(bg);

            if (!found) BuildBroken();
            else if (entry.local && zound is CompositeZound composite) BuildGroup(composite);
            else BuildPlain();

            // The entry flash while it plays (the old FlashEntry: white, 0 → 0.25 → 0 every half second).
            flash = new VisualElement { pickingMode = PickingMode.Ignore };
            flash.style.position = Position.Absolute; flash.style.display = DisplayStyle.None;
            Add(flash);

            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        void Layout() {
            float w = layout.width;
            if (float.IsNaN(w) || w <= 0f) return;
            foreach (var l in layouts) l(w);
        }

        static void Place(VisualElement e, Rect r) => W.Place(e, r.x, r.y, r.width, r.height);

        Label Duration() {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-greymini");
            l.style.color = DurationBlue;
            Add(l);
            return l;
        }

        // ─────────────────────────── broken entry ───────────────────────────

        void BuildBroken() {
            var tint = new VisualElement { pickingMode = PickingMode.Ignore };
            tint.style.position = Position.Absolute; tint.style.left = 0; tint.style.right = 0; tint.style.top = 0; tint.style.bottom = 0;
            tint.style.backgroundColor = new Color(1f, 0.4f, 0.4f, 0.2f);
            Add(tint);
            var title = new Label("BROKEN ENTRY"); title.AddToClassList("zs-lbl"); title.AddToClassList("zs-bold"); Add(title);
            var help = new HelpBox("Zound data is missing or invalid for this entry.", HelpBoxMessageType.Error); Add(help);
            var remove = ZS.Button("Remove", "", "RichButton", () => { CompositeZoundEditing.RemoveEntry(parent, index); }, ZUICornerMask.All, 60f, 20f);
            Add(remove);
            layouts.Add(w => {
                var left = new Rect(0f, 0f, W.LeftSectionWidth, W.EntryHeight);
                var right = new Rect(left.xMax + 5f, 0f, w - left.width - 5f, W.EntryHeight);
                var label = new Rect(left.x + 5f, left.y + 5f, left.width - 10f, LH);
                Place(title, label);
                Place(help, new Rect(left.x + 5f, label.yMax, left.width - 10f, W.EntryHeight - 30f));
                Place(remove, new Rect(right.xMax - 64f, right.y + 5f, 60f, 20f));
            });
        }

        // ─────────────────────────── plain entry ───────────────────────────

        void BuildPlain() {
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            bool randomizer = parent.mode == CompositeZound.Mode.Randomizer;

            IntegerField weight = null;
            if (randomizer) {
                weight = new IntegerField { value = entry.chanceWeight };
                weight.AddToClassList("zs-imgui-field"); weight.AddToClassList("zs-bare-int");
                weight.RegisterValueChangedCallback(e => win.Modify("changed entry chance weight", () => entry.chanceWeight = e.newValue));
                Add(weight);
            }

            // Name (opens the sound's own UI Toolkit editor) and play.
            var name = ZS.Button(zound.name, "", "RichButton", OpenChild, ZUICornerMask.Left, -1f, LH);
            Add(name);
            var play = ZS.Button("►", "Play", "RichButton", () => { CompositeZoundEditing.ToggleEntryPlay(win.zeq, ref win.entryTokens, entry); }, ZUICornerMask.Right, 18f, LH);
            Add(play);
            win.liveRefreshers.Add(() => {
                bool p = CompositeZoundEditing.IsEntryPlaying(win.entryTokens, entry);
                play.text = p ? "⏹" : "►"; play.tooltip = p ? "Stop" : "Play";
            });
            var duration = Duration();

            // The track's ZPOC id (T-0495): how game code reaches this track by name through a play's token.
            var idChip = new Label { tooltip = "" };
            idChip.AddToClassList("zs-zpocchip"); idChip.AddToClassList("zs-zpocchip__text");
            idChip.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0 && e.button != 1) return;
                TrackIdPopup.Show(idChip.worldBound, parent, entry, () => win.refreshers.ForEach(r => r()));
                e.StopPropagation();
            });
            Add(idChip);
            win.refreshers.Add(() => {
                bool on = !string.IsNullOrEmpty(entry.zpocId);
                idChip.EnableInClassList("zs-zpocchip--on", on);
                idChip.text = on ? "⚡ " + entry.zpocId : "⚡";
                idChip.tooltip = on
                    ? "Game code reaches this track as '" + entry.zpocId + "': token.Track(\"" + entry.zpocId + "\") -- its volume, pitch, fades, mute, solo and whether it can be picked. Click to change."
                    : "Game code reaches this track by its number (token.Track(" + index + ")). Click to give it a name as well.";
            });

            // V / P / C.
            var sliders = new List<(VisualElement e, int row)>();
            var toggles = new List<(Toggle t, int row)>();
            var geo = SliderGeometry();
            Func<float, float, string> compact = EditorFieldsUtility.VpcCompactLabel ? (Func<float, float, string>)CompactText : null;
            float scale = EditorFieldsUtility.VpcPercentage ? 100f : 1f;
            ZuiSkinRangeSlider Range(string label, float lo, float hi, float absMin, float absMax, Action<float, float> set, Func<(float, float)> read) {
                var s = new ZuiSkinRangeSlider(label, lo * scale, hi * scale, absMin * scale, absMax * scale, geo,
                    (a, b) => {
                        a /= scale; b /= scale;
                        if (scale != 1f) { a = Mathf.Round(a * 100f) / 100f; b = Mathf.Round(b * 100f) / 100f; }
                        set(a, b);
                    }, null, compact == null ? null : (a, b) => compact(a / scale, b / scale));
                s.AddToClassList("zs-slider-minmax");
                if (compact != null) s.CompactWidth = CompactWidth;
                win.refreshers.Add(() => { var (a, b) = read(); s.SetValuesWithoutNotify(a * scale, b * scale); });
                Add(s);
                return s;
            }
            if (entry.local) {
                var z = zound;
                sliders.Add((Range("V", z.minVolume, z.maxVolume, Zound.MinVolumeRange, Zound.MaxVolumeRange, (a, b) => win.Modify("change entry volume", () => {
                    z.minVolume = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(a); z.maxVolume = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(b); }), () => (z.minVolume, z.maxVolume)), 0));
                sliders.Add((Range("P", z.minPitch, z.maxPitch, Zound.MinPitchRange, Zound.MaxPitchRange, (a, b) => win.Modify("change entry pitch", () => {
                    z.minPitch = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(a); z.maxPitch = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(b); }), () => (z.minPitch, z.maxPitch)), 1));
                sliders.Add((Range("C", z.chance, z.chance, Zound.MinChanceRange, Zound.MaxChanceRange, (a, b) => win.Modify("change entry chance", () => {
                    z.chance = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(!Mathf.Approximately(a, z.chance) ? a : b); }), () => (z.chance, z.chance)), 2));
            }
            else {
                sliders.Add((Range("V", entry.volume, entry.volume, Zound.MinVolumeRange, Zound.MaxVolumeRange, (a, b) => win.Modify("change entry volume", () => entry.volume = !Mathf.Approximately(a, entry.volume) ? a : b), () => (entry.volume, entry.volume)), 0));
                sliders.Add((Range("P", entry.pitch, entry.pitch, Zound.MinPitchRange, Zound.MaxPitchRange, (a, b) => win.Modify("change entry pitch", () => entry.pitch = !Mathf.Approximately(a, entry.pitch) ? a : b), () => (entry.pitch, entry.pitch)), 1));
                sliders.Add((Range("C", entry.chance, entry.chance, Zound.MinChanceRange, Zound.MaxChanceRange, (a, b) => win.Modify("change entry chance", () => entry.chance = !Mathf.Approximately(a, entry.chance) ? a : b), () => (entry.chance, entry.chance)), 2));
                Toggle Over(bool v, string undo, Action<bool> set, Func<bool> read) {
                    var t = new Toggle("O") { value = v, tooltip = "Override.\n\nIf checked, then this will override the original value of the zound. If unchecked, then this will act as a multiplier of the original value." };
                    t.AddToClassList("zs-imgui-field"); t.AddToClassList("zs-label-16");
                    t.RegisterValueChangedCallback(e => win.Modify(undo, () => set(e.newValue)));
                    win.refreshers.Add(() => t.SetValueWithoutNotify(read()));
                    Add(t);
                    return t;
                }
                toggles.Add((Over(entry.overrideVolume, "toggle override entry volume", v => entry.overrideVolume = v, () => entry.overrideVolume), 0));
                toggles.Add((Over(entry.overridePitch, "toggle override entry pitch", v => entry.overridePitch = v, () => entry.overridePitch), 1));
                toggles.Add((Over(entry.overrideChance, "toggle override entry pitch", v => entry.overrideChance = v, () => entry.overrideChance), 2));
            }

            // Right section: delay, timeline, waveform, envelope, playheads, labels, buttons.
            var delay = DelaySlider(0f, 1f, v => win.Modify("change zequence entry delay", () => {
                entry.delay = v * parentPitch;
                CompositeZoundEditing.RecalculateMaxDuration(win.zeq, win.AutoDuration);
            }));
            var timelineBG = Abs(new Color(1f, 1f, 1f, 0.1f));
            var spectrumBG = Abs(es.klipWaveformBGColor);
            var wave = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            wave.style.position = Position.Absolute;
            Add(wave);
            // Clicking the waveform plays/stops the entry, identical to the play button.
            var waveHit = new VisualElement();
            waveHit.style.position = Position.Absolute;
            waveHit.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0) return;
                CompositeZoundEditing.ToggleEntryPlay(win.zeq, ref win.entryTokens, entry);
                e.StopPropagation();
            });
            Add(waveHit);
            EnvelopeTK curve = null;
            if (entry.volumeEnvelope.enabled) {
                var copy = entry.volumeEnvelope.DeepCopy();
                curve = new EnvelopeTK(copy, es.volumeEnvelopeColor) { thickness = es.volumeEnvelopeThickness };
                curve.style.position = Position.Absolute;
                curve.onChanged = () => win.Modify("modify entry volume envelope", () => { entry.volumeEnvelope = copy.DeepCopy(); entry.volumeEnvelope.enabled = true; });
                Add(curve);
            }
            var heads = new VisualElement { pickingMode = PickingMode.Ignore };
            heads.style.position = Position.Absolute; heads.style.left = 0; heads.style.top = 0; heads.style.right = 0; heads.style.bottom = 0;
            Add(heads);
            var preLabel = Duration(); var postLabel = Duration();

            Button dup = W.IconButton("duplicate", "Duplicate this zound entry.", "RichButton", ZUICornerMask.Left, -1f, 20f, () => CompositeZoundEditing.DuplicateEntry(parent, index));
            Button rem = W.IconButton("remove", "Remove this zound entry.", "RichButton", ZUICornerMask.Right, -1f, 20f, () => CompositeZoundEditing.RemoveEntry(parent, index));
            Add(dup); Add(rem);
            var mute = ZS.Toggle("M", "Mute/Unmute", entry.mute, v => win.Modify("toggle mute", () => { entry.mute = v; if (entry.mute) entry.solo = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Left, -1f, 20f, MuteOn);
            var solo = ZS.Toggle("S", "Toggle Solo", entry.solo, v => win.Modify("toggle solo", () => { entry.solo = v; if (entry.solo) entry.mute = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Right, -1f, 20f, SoloOn);
            win.refreshers.Add(() => { mute.SetValueWithoutNotify(entry.mute); ZS.ApplyOnColor(mute, MuteOn); solo.SetValueWithoutNotify(entry.solo); ZS.ApplyOnColor(solo, SoloOn); });
            Add(mute); Add(solo);
            Button convert = ConversionButton();
            if (convert != null) Add(convert);
            var up = ZS.Button("↑", "Reorder up.", "RichButton", () => Swap(index - 1), ZUICornerMask.Top, -1f, 20f);
            var down = ZS.Button("↓", "Reorder down.", "RichButton", () => Swap(index + 1), ZUICornerMask.Bottom, -1f, 20f);
            up.SetEnabled(index > 0); down.SetEnabled(index < parent.zoundEntries.Count - 1);
            Add(up); Add(down);

            layouts.Add(w => {
                var rect = new Rect(0f, 0f, w, W.EntryHeight);
                var content = isGroupChild ? new Rect(rect.x, rect.y + 4f, rect.width, rect.height - 8f) : new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);
                flashRect = content;
                float leftOffset = isGroupChild ? W.GroupEntryLeftOffset : 0f;
                var left = new Rect(content.x + leftOffset, content.y, W.LeftSectionWidth - leftOffset, content.height);
                var right = new Rect(left.xMax + 5f, content.y + LH, content.width - left.width - 5f - leftOffset, content.height - LH);
                if (weight != null) { Place(weight, new Rect(left.position, new Vector2(22f, 20f))); left.x += 24f; left.width -= 24f; }

                Place(name, new Rect(left.x, content.y, (content.width - 18f) * 0.8f, LH));
                float chipX = left.x + (content.width - 18f) * 0.8f + 2f;
                Place(idChip, new Rect(chipX, content.y, Mathf.Max(18f, content.xMax - 18f - 2f - chipX), LH));
                Place(play, new Rect(content.xMax - 18f, content.y, 18f, LH));
                float y = content.y + LH - 3f;
                float dur = CompositeZoundEditing.GetEntryDuration(parent, entry, parentPitch);
                Place(duration, new Rect(left.x, y, left.width * 0.75f, LH));
                float d = parentDelay + entry.delay;
                duration.text = dur.ToString("0.00") + " sec" + (d > 0f ? " (" + (dur + d).ToString("0.00") + " sec)" : "");
                if (entry.local) {
                    for (int i = 0; i < sliders.Count; i++) Place(sliders[i].e, new Rect(left.x, y + LH + 1f + i * (LH + 5f), left.width - 1f, LH));
                }
                else {
                    for (int i = 0; i < sliders.Count; i++) {
                        var sr = new Rect(left.x, y + LH + 1f + i * (LH + 3f), left.width - 36f, LH);
                        Place(sliders[i].e, sr);
                        Place(toggles[i].t, new Rect(sr.xMax + 4f, sr.y, 20f, LH));
                    }
                }

                // Right section.
                float globalMax = win.zeq.editor_maxDuration / parentPitch;
                float total = right.width - FieldBoxWidth - 15f;
                float parentOffset = parentDelay / globalMax * total;
                float delayRectWidth = total - parentOffset;
                Place(delay, new Rect(right.x + parentOffset, right.y, delayRectWidth + FieldBoxWidth + 15f, LH));
                delay.lowValue = 0f; delay.highValue = globalMax - parentDelay;
                if (delay.focusController?.focusedElement == null || !delay.Contains(delay.focusController.focusedElement as VisualElement))
                    delay.SetValueWithoutNotify(entry.delay / parentPitch);
                var timeline = new Rect(right.x + 5f, right.y + 18f, total, right.height - 18f);
                var tbg = new Rect(right.x + parentOffset + 5f, right.y + 18f, delayRectWidth, right.height - 18f);
                Place(timelineBG, tbg);
                float accDelay = parentDelay + entry.delay / parentPitch;
                float sx = accDelay / globalMax * timeline.width, sw = dur / globalMax * timeline.width;
                if (sx + sw > timeline.width) {
                    CompositeZoundEditing.RecalculateMaxDuration(win.zeq, win.AutoDuration);
                    globalMax = win.zeq.editor_maxDuration / parentPitch;
                    sx = accDelay / globalMax * timeline.width; sw = dur / globalMax * timeline.width;
                }
                var spectrum = new Rect(timeline.x + sx, timeline.y, sw, timeline.height);
                Place(spectrumBG, spectrum); Place(waveHit, spectrum);
                spectrumBG.style.display = zound is Klip || zound is Zequence ? DisplayStyle.Flex : DisplayStyle.None;
                wave.style.display = DisplayStyle.None;
                if (zound is Klip klip && klip.GetAudioClipReference().editorAsset is AudioClip clip) {
                    var tex = AudioWaveformUtility.GetWaveformSpectrumTexture(clip, Mathf.FloorToInt(spectrum.width), Mathf.FloorToInt(spectrum.height), es.waveformColor, klip.id.ToString());
                    if (tex != null) { wave.image = tex; Place(wave, spectrum); wave.style.display = DisplayStyle.Flex; }
                }
                if (curve != null) Place(curve, spectrum);
                heads.userData = (spectrum, timeline);

                preLabel.style.display = entry.delay >= Mathf.Epsilon ? DisplayStyle.Flex : DisplayStyle.None;
                Place(preLabel, new Rect(tbg.x + 2f, tbg.center.y + 10f, 50f, 20f));
                preLabel.text = entry.delay.ToString("0.00") + " s";
                bool post = entry.delay + dur < win.zeq.editor_maxDuration;
                postLabel.style.display = post ? DisplayStyle.Flex : DisplayStyle.None;
                Place(postLabel, new Rect(tbg.xMax - 52f, tbg.center.y + 10f, 50f, 20f));
                postLabel.text = (win.zeq.editor_maxDuration - entry.delay - dur).ToString("0.00") + " s";

                // Button column.
                var col = new Rect(timeline.xMax + 5f, timeline.y, right.width - timeline.width - 10f, 20f);
                float bw = (col.width - 2f) / 2f;
                Place(dup, new Rect(col.x, col.y, bw, 20f)); Place(rem, new Rect(col.x + bw + 2f, col.y, bw, 20f));
                var ms = new Rect(col.x, col.yMax + 2f, col.width, 20f);
                Place(mute, new Rect(ms.x, ms.y, bw, 20f)); Place(solo, new Rect(ms.x + bw + 2f, ms.y, bw, 20f));
                var conv = new Rect(ms.x, ms.yMax + 2f, ms.width, 20f);
                if (convert != null) Place(convert, conv);
                var ro = new Rect(conv.x, conv.yMax + 2f, conv.width, 20f);
                Place(up, new Rect(ro.x, ro.y, ro.width / 2f, 20f)); Place(down, new Rect(ro.x + ro.width / 2f, ro.y, ro.width / 2f, 20f));
            });

            // Playheads (one per sounding token of this entry) and the flash.
            var headPool = new List<VisualElement>();
            win.liveRefreshers.Add(() => {
                int n = 0;
                bool flashing = false;
                if (heads.userData is ValueTuple<Rect, Rect> geom && ZoundEngine.CullingGroups.TryGetValue(parent, out var playingTokens)) {
                    var (spectrum, timeline) = geom;
                    foreach (var token in playingTokens) {
                        if (token == null || token.state == ZoundToken.State.Killed) continue;
                        if (!token.TryGetEntryToken(entry, out var child) || child.state == ZoundToken.State.Killed) continue;
                        if (token.IsEntryMuted(entry)) continue;
                        if (token.soloOverride != null && token.soloOverride != entry) continue;
                        if (token.zound is Zequence tz && token.isRealtime && tz.mode != CompositeZound.Mode.Parallel && token.playedEntryIndex != index) continue;
                        flashing = true;
                        if (child.duration > Mathf.Epsilon) {
                            if (n >= headPool.Count) { var h = new VisualElement { pickingMode = PickingMode.Ignore }; h.style.position = Position.Absolute; heads.Add(h); headPool.Add(h); }
                            var head = headPool[n++];
                            head.style.display = DisplayStyle.Flex;
                            head.style.backgroundColor = ZoundsProject.Instance.projectSettings.editorStyle.playerHeadColor;
                            Place(head, new Rect(spectrum.x + child.time / child.duration * spectrum.width, timeline.y, 1.5f, timeline.height));
                        }
                    }
                }
                for (int i = n; i < headPool.Count; i++) headPool[i].style.display = DisplayStyle.None;
                Flash(flashing);
            });
        }

        void Flash(bool on) {
            flash.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            Place(flash, flashRect);
            float t = (float)(EditorApplication.timeSinceStartup % 0.5) / 0.5f;
            t = 4f * t * (1f - t);
            flash.style.backgroundColor = Color.Lerp(new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.25f), t);
            flash.BringToFront();
        }

        VisualElement Abs(Color c) {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute; e.style.backgroundColor = c;
            Add(e);
            return e;
        }

        /// <summary>EditorGUI.Slider: UI Toolkit's own slider with its number box (the editor's 50 px field, 5 px after the track).</summary>
        Slider DelaySlider(float lo, float hi, Action<float> onChanged) {
            var s = new Slider(lo, hi) { showInputField = true };
            s.AddToClassList("zs-imgui-slider");
            s.RegisterValueChangedCallback(e => onChanged(e.newValue));
            Add(s);
            return s;
        }

        Button ConversionButton() {
            string icon = null, tip = null;
            if (zound is Klip k) {
                if (entry.local) { icon = k.originalId == 0 ? "make-shared" : "reconnect-shared"; tip = k.originalId == 0 ? MakeSharedTip : ReconnectTip; }
                else { icon = "break-to-local"; tip = BreakTip; }
            }
            else if (zound is Zequence && !entry.local) { icon = "break-to-local"; tip = BreakTip; }
            if (icon == null) return null;
            return W.IconButton(icon, tip, "RichButton", ZUICornerMask.All, -1f, 20f, () => CompositeZoundEditing.ConvertEntry(parent, index));
        }

        const string MakeSharedTip = "<b>Convert to Shared Klip</b>\n\nConvert this Klip into a Shared Klip where it will be listed in Klip browser. Shared Klips can be used across different Zequence.";
        const string BreakTip = "<b>Break as Local Klip</b>\n\nConvert this Klip into a Local Klip where it will only be available internally in this Zequence. This will break the dependency from the original configuration of the Shared Klip, and the Shared Klip's configuration will also no longer affected by this Klip.";
        const string ReconnectTip = "<b>Reconnect to Original Shared Klip</b>\n\nConvert this Klip back into its original Shared Klip. If the original Shared Klip has been removed, then this will fallback into creating a new Shared Klip.";

        void Swap(int other) {
            var list = parent.zoundEntries;
            if (other < 0 || other >= list.Count) return;
            win.Modify(other < index ? "reorder up" : "reorder down", () => { var t = list[other]; list[other] = list[index]; list[index] = t; });
        }

        void OpenChild() {
            // Opens the child's UI Toolkit editor, so a session in the new windows stays in them (the old windows are one
            // tab-menu click away from their own editors).
            if (zound is Klip k) KlipEditorWindowTK.Open(k, entry.local);
            else if (zound is Zequence z) W.Open(z, entry.local);
        }

        static ZuiSkinRangeSlider.Geometry SliderGeometry() => new ZuiSkinRangeSlider.Geometry {
            // The Zounds sheet's "MinMax" slider def, copied (2026-09-28): thumb 1 (drawn at least 4) × 20, track 6,
            // label inline and auto-sized, value fields on, 40 wide, not bipolar, automatic format.
            thumbWidth = 1f, thumbHeight = 20f, trackHeight = 6f, valueWidth = 40f, labelWidth = 0f,
            showValueField = true, bipolar = false, bipolarCenter = float.NaN, valueFormat = null,
        };

        // The compact label (EditorFieldsUtility): "min-max", or one value when both show the same.
        static string CompactText(float min, float max) {
            string a, b;
            if (EditorFieldsUtility.VpcPercentage) { a = Mathf.RoundToInt(min * 100f).ToString(); b = Mathf.RoundToInt(max * 100f).ToString(); }
            else { a = min.ToString("G4"); b = max.ToString("G4"); }
            return a == b ? a : a + "-" + b;
        }
        static float CompactWidth {
            get {
                var st = EditorStyles.label;
                float pct = st.CalcSize(new GUIContent("99-100")).x, frac = st.CalcSize(new GUIContent("0.98-0.99")).x + 10f;
                return 10f + Mathf.Max(pct, frac) + 5f;
            }
        }

        // ─────────────────────────── group (a local Zequence) ───────────────────────────

        void BuildGroup(CompositeZound comp) {
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            float lh = LH;

            // Left: the chance weight when the parent is a randomizer (the Zequence window adds it to groups too), then the
            // foldout header, rename field, play; No-Play and Mode; duration.
            IntegerField weight = null;
            if (parent.mode == CompositeZound.Mode.Randomizer) {
                weight = new IntegerField { value = entry.chanceWeight };
                weight.AddToClassList("zs-imgui-field"); weight.AddToClassList("zs-bare-int");
                weight.RegisterValueChangedCallback(e => win.Modify("changed entry chance weight", () => entry.chanceWeight = e.newValue));
                Add(weight);
            }
            var fold = new Foldout { text = comp.name, value = entry.editor_foldoutExpanded };
            fold.AddToClassList("zs-foldoutheader");
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) win.Modify("toggle foldout expand", () => entry.editor_foldoutExpanded = e.newValue); });
            Add(fold);
            TextField rename = null;
            if (entry.editor_isRenaming) {
                rename = new TextField { value = comp.name };
                rename.AddToClassList("zs-namefield");
                rename.RegisterValueChangedCallback(e => {
                    var n = ZoundDictionary.EnsureUniqueZoundName(e.newValue);
                    win.Modify("change local zequence name", () => comp.name = n);
                });
                Add(rename);
            }
            var play = ZS.Button("►", "Play", "RichButton", () => CompositeZoundEditing.ToggleEntryPlay(win.zeq, ref win.entryTokens, entry), ZUICornerMask.All, 18f, lh);
            Add(play);
            win.liveRefreshers.Add(() => { bool p = CompositeZoundEditing.IsEntryPlaying(win.entryTokens, entry); play.text = p ? "⏹" : "►"; });
            IntegerField noPlay = null;
            if (comp.mode == CompositeZound.Mode.Randomizer) {
                noPlay = new IntegerField { value = comp.noPlayWeight };
                noPlay.AddToClassList("zs-imgui-field"); noPlay.AddToClassList("zs-bare-int");
                noPlay.RegisterValueChangedCallback(e => win.Modify("change local zequence no-play weight", () => comp.noPlayWeight = e.newValue));
                Add(noPlay);
            }
            var mode = new EnumField(comp.mode);
            mode.AddToClassList("zs-imgui-field");
            mode.RegisterValueChangedCallback(e => win.Modify("change local zequence mode", () => comp.mode = (CompositeZound.Mode)e.newValue));
            Add(mode);
            var duration = Duration();

            // Right: the local Zequence's own fields, the button strip, the delay, its master envelope.
            var fieldsRow = new ZoundFieldsRowTK(comp, true, null, drawName: false, drawTags: false);
            fieldsRow.style.position = Position.Absolute;
            Add(fieldsRow);
            win.refreshers.Add(fieldsRow.Sync);
            var renameBtn = ZS.Button(entry.editor_isRenaming ? "Done" : "Rename", "", "RichButton", () => {
                entry.editor_isRenaming = !entry.editor_isRenaming;
                EditorUtility.SetDirty(ZoundsProject.Instance);
            }, ZUICornerMask.All, 60f, 20f);
            Add(renameBtn);
            var dup = W.IconButton("duplicate", "Duplicate this zound entry.", "RichButton", ZUICornerMask.Left, -1f, 20f, () => CompositeZoundEditing.DuplicateEntry(parent, index));
            var rem = W.IconButton("remove", "Remove this zound entry.", "RichButton", ZUICornerMask.Right, -1f, 20f, () => CompositeZoundEditing.RemoveEntry(parent, index));
            var mute = ZS.Toggle("M", "Mute/Unmute", entry.mute, v => win.Modify("toggle mute", () => { entry.mute = v; if (entry.mute) entry.solo = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Left, -1f, 20f, MuteOn);
            var solo = ZS.Toggle("S", "Toggle Solo", entry.solo, v => win.Modify("toggle solo", () => { entry.solo = v; if (entry.solo) entry.mute = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Right, -1f, 20f, SoloOn);
            var conv = W.IconButton(comp.originalId == 0 ? "make-shared" : "reconnect-shared", comp.originalId == 0 ? MakeSharedTip : ReconnectTip, "RichButton", ZUICornerMask.All, -1f, 20f, () => CompositeZoundEditing.ConvertEntry(parent, index));
            var up = ZS.Button("↑", "Reorder up.", "RichButton", () => Swap(index - 1), ZUICornerMask.Left, -1f, 20f);
            var down = ZS.Button("↓", "Reorder down.", "RichButton", () => Swap(index + 1), ZUICornerMask.Right, -1f, 20f);
            up.SetEnabled(index > 0); down.SetEnabled(index < parent.zoundEntries.Count - 1);
            foreach (var b in new VisualElement[] { dup, rem, mute, solo, conv, up, down }) Add(b);
            win.refreshers.Add(() => { mute.SetValueWithoutNotify(entry.mute); ZS.ApplyOnColor(mute, MuteOn); solo.SetValueWithoutNotify(entry.solo); ZS.ApplyOnColor(solo, SoloOn); });
            var delay = DelaySlider(0f, Mathf.Max(0.0001f, win.zeq.editor_maxDuration), v => win.Modify("change zequence entry delay", () => {
                entry.delay = v;
                CompositeZoundEditing.RecalculateMaxDuration(win.zeq, win.AutoDuration);
            }));
            VisualElement envBG = null; EnvelopeTK curve = null;
            if (comp is Zequence && entry.volumeEnvelope.enabled) {
                envBG = Abs(new Color(0.75f, 0.75f, 0.75f, 0.1f));
                var copy = entry.volumeEnvelope.DeepCopy();
                curve = new EnvelopeTK(copy, es.volumeEnvelopeColor) { thickness = es.volumeEnvelopeThickness };
                curve.style.position = Position.Absolute;
                curve.onChanged = () => win.Modify("modify group volume envelope", () => { entry.volumeEnvelope = copy.DeepCopy(); entry.volumeEnvelope.enabled = true; });
                Add(curve);
            }

            // Children and the group's add buttons.
            var children = new List<ZequenceEntryTK>();
            Button addKlip = null, addShared = null;
            if (entry.editor_foldoutExpanded) {
                bool darker = false;
                for (int i = 0; i < comp.zoundEntries.Count; i++) {
                    float childPitch = win.zeq.minPitch * comp.minPitch;
                    var child = new ZequenceEntryTK(win, comp, comp.zoundEntries[i], i, childPitch, entry.delay / childPitch, darker, true);
                    child.style.position = Position.Absolute;
                    Add(child); children.Add(child);
                    darker = !darker;
                }
                addKlip = ZS.Button("+ Local Klip", "", "RichButton", () => win.AddLocalKlip(comp, addKlip), ZUICornerMask.Left, 85f, lh);
                addShared = ZS.Button("+ Shared Zound", "", "RichButton", () => win.AddShared(comp, addShared), ZUICornerMask.Right, 105f, lh);
                Add(addKlip); Add(addShared);
            }

            layouts.Add(w => {
                var content = new Rect(4f, 4f, w - 8f, layout.height - 8f);
                var left = new Rect(content.x, content.y, W.LeftSectionWidth, content.height);
                var right = new Rect(left.xMax + 5f, content.y, content.width - left.width - 5f, content.height);
                if (weight != null) { Place(weight, new Rect(left.position, new Vector2(22f, 20f))); left.x += 24f; left.width -= 24f; }
                float y = left.y;
                var label = new Rect(left.x, y, Mathf.Min((content.width - 18f) * 0.8f, left.width), lh);
                if (entry.editor_isRenaming) label.width = 14f;
                Place(fold, label);
                if (rename != null) Place(rename, new Rect(label.xMax, y, left.width - label.width, lh));
                Place(play, new Rect(content.xMax - 18f, y, 18f, lh));
                y += lh + 2f;
                float xOff = 0f;
                if (noPlay != null) { xOff += W.GroupEntryLeftOffset; Place(noPlay, new Rect(left.x + xOff, y, 22f, 20f)); xOff += 24f; }
                Place(mode, new Rect(left.x + xOff, y + 1f, left.width - xOff, 20f));
                y += 22f;
                float dur = CompositeZoundEditing.GetEntryDuration(parent, entry, win.zeq.minPitch);
                Place(duration, new Rect(left.x, y, left.width * 0.75f, lh));
                duration.text = dur.ToString("0.00") + " sec" + (entry.delay > 0f ? " (" + (dur + entry.delay).ToString("0.00") + " sec)" : "");

                float ry = right.y;
                Place(fieldsRow, new Rect(right.x, ry, right.width, lh));
                ry += lh + 2f;
                Place(renameBtn, new Rect(right.x, ry, 60f, 20f));
                float bw = (right.width - 60f - 2f) / 7f - 2f, bx = right.x + 62f;
                foreach (var b in new VisualElement[] { dup, rem, mute, solo, conv, up, down }) { Place(b, new Rect(bx, ry, bw, 20f)); bx += bw + 2f; }
                ry += 22f;
                Place(delay, new Rect(right.x, ry, right.width, lh));
                delay.highValue = Mathf.Max(0.0001f, win.zeq.editor_maxDuration);
                if (delay.focusController?.focusedElement == null || !delay.Contains(delay.focusController.focusedElement as VisualElement)) delay.SetValueWithoutNotify(entry.delay);
                ry += lh + 2f;
                if (comp is Zequence) {
                    if (entry.volumeEnvelope.enabled) {
                        float globalMax = win.zeq.editor_maxDuration / win.zeq.minPitch, globalDelay = entry.delay / win.zeq.minPitch;
                        float total = right.width - FieldBoxWidth - 15f;
                        float off = globalDelay / globalMax * total;
                        var bgR = new Rect(right.x + off + 5f, ry, dur / globalMax * total, lh * 4f);
                        Place(envBG, bgR); Place(curve, bgR);
                        ry += lh * 4f;
                    }
                    else ry += lh;
                }
                flashRect = new Rect(content.x, content.y, content.width, ry - content.y);

                float cy = content.y + W.GroupHeaderHeight + (comp is Zequence ? (entry.volumeEnvelope.enabled ? lh * 4f : lh) : 0f);
                foreach (var c in children) { W.Place(c, content.x, cy, content.width, W.EntryHeight); cy += W.EntryHeight + 4f; }
                if (addKlip != null) {
                    float ay = ry + children.Count * (W.EntryHeight + 4f) + 6f;
                    float xo = right.xMax - (right.x + 85f + 4f + 105f);
                    Place(addKlip, new Rect(right.x + xo, ay, 85f, lh));
                    Place(addShared, new Rect(right.x + 85f + 4f + xo, ay, 105f, lh));
                }
            });

            // The group flashes while its local Zequence plays (every token, as the old one).
            win.liveRefreshers.Add(() => {
                bool on = false;
                if (ZoundEngine.CullingGroups.TryGetValue(comp, out var tokens))
                    foreach (var t in tokens) if (t != null && t.state != ZoundToken.State.Killed && !t.audioSource.mute) on = true;
                Flash(on);
            });
        }
    }
}
