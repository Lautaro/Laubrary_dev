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
    /// One track card of the Zequence editor (T-0469; laid out afresh 2026-10-08 on the owner's review). A plain track is two
    /// rows: a header (reorder grip, play, name and length, the track's id, its start time, the curve bar for a local
    /// Klip, then Mute / Solo / duplicate / delete / convert) and, under it, the V/P/C sliders down the left with the
    /// track's lane on the shared timeline filling the rest. A grip along the bottom edge sets the track's own height.
    /// A local Zequence is a group with its own header, fields, buttons, delay, master envelope and, when expanded, its
    /// child tracks and add buttons.
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
        readonly VisualElement flash, dropLine;
        Rect flashRect;
        CurveBarTK curveBar;
        TrackStripTK strip;

        static readonly Color MuteOn = new Color32(107, 50, 48, 255), SoloOn = new Color(.14f, .34f, .14f, 1f);

        float LH => EditorGUIUtility.singleLineHeight;
        const float Pad = 4f, HeaderH = 18f, GripH = 5f, Btn = 20f, LeftW = 150f;
        /// <summary>A plain track's default height, the least it can be (its three sliders need the room) and the most.</summary>
        internal const float DefaultHeight = 94f, MinHeight = 94f, MaxHeight = 600f;

        /// <summary>Where a top-level card's timeline lane starts and how much it leaves at the right.</summary>
        internal const float LaneLeft = Pad + LeftW + 5f, LaneRight = Pad;
        internal bool IsGroupChild => isGroupChild;
        internal CompositeZound.ZoundEntry Entry => entry;
        internal CompositeZound Parent => parent;

        /// <summary>A track's height: its own, or the default.</summary>
        internal static float HeightOf(CompositeZound.ZoundEntry e) => e.editor_height > 0f ? Mathf.Clamp(e.editor_height, MinHeight, MaxHeight) : DefaultHeight;

        public ZequenceEntryTK(W win, CompositeZound parent, CompositeZound.ZoundEntry entry, int index, float parentPitch, float parentDelay, bool darker, bool isGroupChild) {
            this.win = win; this.parent = parent; this.entry = entry; this.index = index;
            this.parentPitch = parentPitch; this.parentDelay = parentDelay; this.isGroupChild = isGroupChild;
            AddToClassList("zs-zequence-entry__root");
            found = parent.TryGetEntryZound(entry, out zound);
            var es = ZoundsProject.Instance.projectSettings.editorStyle;

            // Height: a plain entry has its own (or the default); a local Zequence group grows with its master envelope and its children.
            float h = HeightOf(entry);
            if (found && entry.local && zound is CompositeZound comp) {
                h = W.GroupHeaderHeight;
                if (entry.editor_foldoutExpanded) { foreach (var c in comp.zoundEntries) h += HeightOf(c) + 4f; h += LH + 10f; }
                if (comp is Zequence) h += entry.volumeEnvelope.enabled ? LH * 4f : LH;
            }
            style.height = h;

            // Background band (the two alternating colours are the owner's, in the Settings tab).
            var bg = new VisualElement { pickingMode = PickingMode.Ignore };
            bg.AddToClassList("zs-zequence-entry__bg");
            bg.AddToClassList(isGroupChild ? "zs-zequence-entry__background--group-child" : "zs-zequence-entry__background--plain");
            bg.style.backgroundColor = darker ? es.trackBackgroundColor : es.trackAltBackgroundColor;
            Add(bg);

            if (!found) BuildBroken();
            else if (entry.local && zound is CompositeZound composite) BuildGroup(composite);
            else BuildPlain();

            // The entry flash while it plays (the old FlashEntry: white, 0 → 0.25 → 0 every half second).
            flash = new VisualElement { pickingMode = PickingMode.Ignore };
            flash.AddToClassList("zs-zequence-entry__flash"); flash.style.display = DisplayStyle.None;
            Add(flash);
            // Where a dragged track would land: a line along this card's top or bottom edge.
            dropLine = new VisualElement { pickingMode = PickingMode.Ignore };
            dropLine.AddToClassList("zs-zequence-entry__drop-line"); dropLine.style.display = DisplayStyle.None;
            Add(dropLine);

            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        void Layout() {
            float w = layout.width;
            if (float.IsNaN(w) || w <= 0f) return;
            foreach (var l in layouts) l(w);
        }

        static void Place(VisualElement e, Rect r) => W.Place(e, r.x, r.y, r.width, r.height);

        internal void ShowDropLine(bool show, bool below) {
            dropLine.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            dropLine.style.top = below ? StyleKeyword.Auto : 0f;
            dropLine.style.bottom = below ? 0f : StyleKeyword.Auto;
        }

        // ─────────────────────────── broken entry ───────────────────────────

        void BuildBroken() {
            var tint = new VisualElement { pickingMode = PickingMode.Ignore };
            tint.AddToClassList("zs-zequence-entry__broken-tint");
            Add(tint);
            var title = new Label("BROKEN ENTRY"); title.AddToClassList("zs-lbl"); title.AddToClassList("zs-bold"); Add(title);
            var help = new HelpBox("Zound data is missing or invalid for this entry.", HelpBoxMessageType.Error); Add(help);
            var remove = ZS.Button("Remove", "", "RichButton", () => { CompositeZoundEditing.RemoveEntry(parent, index); }, ZUICornerMask.All, 60f, 20f);
            Add(remove);
            layouts.Add(w => {
                float hh = HeightOf(entry);
                var left = new Rect(0f, 0f, LeftW + 40f, hh);
                var right = new Rect(left.xMax + 5f, 0f, w - left.width - 5f, hh);
                var label = new Rect(left.x + 5f, left.y + 5f, left.width - 10f, LH);
                Place(title, label);
                Place(help, new Rect(left.x + 5f, label.yMax, left.width - 10f, hh - 30f));
                Place(remove, new Rect(right.xMax - 64f, right.y + 5f, 60f, 20f));
            });
        }

        // ─────────────────────────── shared pieces of a header ───────────────────────────

        /// <summary>The reorder grip (⋮⋮): drag a track up or down past the others; one undo step on release.</summary>
        VisualElement Grip() {
            var g = new Label("⋮⋮") { tooltip = "Drag up or down to reorder this track." };
            g.AddToClassList("zs-lbl"); g.AddToClassList("zs-zequence-entry__grip");
            ZequenceEntryTK target = null; bool below = false;
            g.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; g.CapturePointer(e.pointerId); e.StopPropagation(); });
            g.RegisterCallback<PointerMoveEvent>(e => {
                if (!g.HasPointerCapture(e.pointerId)) return;
                target?.ShowDropLine(false, false); target = null;
                foreach (var c in Siblings()) {
                    var wb = c.worldBound;
                    if (e.position.y < wb.yMin || e.position.y > wb.yMax) continue;
                    target = c; below = e.position.y > wb.center.y;
                    if (!ReferenceEquals(c, this)) c.ShowDropLine(true, below);
                    break;
                }
            });
            g.RegisterCallback<PointerUpEvent>(e => {
                if (!g.HasPointerCapture(e.pointerId)) return;
                g.ReleasePointer(e.pointerId);
                var t = target; target?.ShowDropLine(false, false); target = null;
                if (t == null || ReferenceEquals(t, this)) return;
                var list = parent.zoundEntries;
                int from = list.IndexOf(entry), to = list.IndexOf(t.entry);
                if (from < 0 || to < 0) return;
                if (below) to++;
                if (to > from) to--;
                if (to == from) return;
                win.Modify("reorder track", () => { list.RemoveAt(from); list.Insert(to, entry); });
            });
            return g;
        }

        /// <summary>The cards of the same list as this one (the tracks of one Zequence), in order.</summary>
        IEnumerable<ZequenceEntryTK> Siblings() {
            if (parent == null || this.parent == null) yield break;
            foreach (var c in this.hierarchy.parent.Children()) if (c is ZequenceEntryTK z && ReferenceEquals(z.parent, parent)) yield return z;
        }

        /// <summary>The bottom-edge grip: drag it to set this track's height (kept with the track).</summary>
        VisualElement HeightGrip() {
            var g = new VisualElement { tooltip = "Drag to change this track's height." };
            g.AddToClassList("zs-zequence-entry__height-grip");
            float downY = 0f, downH = 0f;
            g.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; downY = e.position.y; downH = HeightOf(entry); g.CapturePointer(e.pointerId); e.StopPropagation(); });
            g.RegisterCallback<PointerMoveEvent>(e => {
                if (!g.HasPointerCapture(e.pointerId)) return;
                float hh = Mathf.Clamp(downH + (e.position.y - downY), MinHeight, MaxHeight);
                if (Mathf.Approximately(hh, HeightOf(entry))) return;
                entry.editor_height = hh;
                EditorUtility.SetDirty(ZoundsProject.Instance);
                style.height = hh;
                win.OnTrackHeightChanged(this);
            });
            g.RegisterCallback<PointerUpEvent>(e => { if (g.HasPointerCapture(e.pointerId)) g.ReleasePointer(e.pointerId); win.RefreshNow(); });
            return g;
        }

        Button SmallIcon(string icon, string tip, ZUICornerMask corners, Action onClick) => W.IconButton(icon, tip, "RichButton", corners, Btn, HeaderH, onClick);

        // ─────────────────────────── plain entry ───────────────────────────

        void BuildPlain() {
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            bool randomizer = parent.mode == CompositeZound.Mode.Randomizer;

            var grip = Grip(); Add(grip);
            IntegerField weight = null;
            if (randomizer) {
                weight = new IntegerField { value = entry.chanceWeight, tooltip = "This track's weight when the randomizer picks one track: the higher, the likelier." };
                weight.AddToClassList("zs-imgui-field"); weight.AddToClassList("zs-bare-int");
                weight.RegisterValueChangedCallback(e => win.Modify("changed entry chance weight", () => entry.chanceWeight = e.newValue));
                Add(weight);
            }

            // Play, name (opens the sound's own editor) and the length.
            var play = ZS.Button("►", "Play", "RichButton", () => { CompositeZoundEditing.ToggleEntryPlay(win.zeq, ref win.entryTokens, entry, win); }, ZUICornerMask.Left, 18f, HeaderH);
            Add(play);
            win.liveRefreshers.Add(() => {
                bool p = ZoundPreviewPlayback.IsLoopPlaying(win, entry);
                play.text = p ? "⏹" : "►"; play.tooltip = p ? "Stop loop" : "Play this track as part of the Zequence (its delay, volume and pitch applied).";
            });
            var name = ZS.Button(zound.name, "", "RichButton", OpenChild, ZUICornerMask.Right, -1f, HeaderH);
            name.AddToClassList("zs-zequence-entry__name");
            Add(name);
            var duration = new Label { pickingMode = PickingMode.Ignore };
            duration.AddToClassList("zs-lbl"); duration.AddToClassList("zs-greymini"); duration.AddToClassList("zs-zequence-entry__duration-label");
            Add(duration);

            // The track's ZPOC id (T-0495): how game code reaches this track by name through a play's token.
            var idChip = new VisualElement { tooltip = "" };
            idChip.AddToClassList("zs-zpocchip");
            idChip.AddToClassList("zs-zequence-entry__plain-id-chip");
            var idBolt = new ZpocBolt { pickingMode = PickingMode.Ignore };
            idBolt.AddToClassList("zs-zequence-entry__plain-id-bolt");
            var idText = new Label { pickingMode = PickingMode.Ignore };
            idText.AddToClassList("zs-zpocchip__text");
            idText.AddToClassList("zs-zequence-entry__plain-id-text");
            idChip.Add(idBolt); idChip.Add(idText);
            idChip.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0 && e.button != 1) return;
                TrackIdPopup.Show(idChip.worldBound, parent, entry, () => win.refreshers.ForEach(r => r()));
                e.StopPropagation();
            });
            Add(idChip);
            win.refreshers.Add(() => {
                bool on = !string.IsNullOrEmpty(entry.zpocId);
                idChip.EnableInClassList("zs-zpocchip--on", on);
                idText.text = on ? entry.zpocId : "";
                idBolt.Color = on ? ZpocBolt.Amber : ZpocBolt.Unlit;
                idChip.tooltip = on
                    ? "Game code reaches this track as '" + entry.zpocId + "': token.Track(\"" + entry.zpocId + "\") -- its volume, pitch, fades, mute, solo and whether it can be picked. Click to change."
                    : "Game code reaches this track by its number (token.Track(" + index + ")). Click to give it a name as well.";
                Layout();   // the chip is sized to its text
            });

            // Start: when this track starts, in seconds after the Zequence starts (the piece can also be dragged by its top strip).
            var startLabel = new Label("Start") { pickingMode = PickingMode.Ignore };
            startLabel.AddToClassList("zs-lbl"); startLabel.AddToClassList("zs-mini"); startLabel.AddToClassList("zs-zequence-entry__start-label");
            Add(startLabel);
            var start = Z.Float(entry.delay / parentPitch, "When this track starts, in seconds after the Zequence starts. Drag to scrub, or type. Or drag the thin strip along the top of its piece.",
                v => win.Modify("change zequence entry delay", () => {
                    entry.delay = Mathf.Max(0f, v) * parentPitch;
                    CompositeZoundEditing.RecalculateMaxDuration(win.zeq, win.AutoDuration);
                }), 56f, 2);
            start.AddToClassList("zs-imgui-field"); start.AddToClassList("zs-bare-int"); start.AddToClassList("zs-zequence-entry__start");
            win.refreshers.Add(() => { if (start.focusController?.focusedElement == null || !start.Contains(start.focusController.focusedElement as VisualElement)) start.SetValueWithoutNotify(entry.delay / parentPitch); });
            Add(start);

            // V / P / C.
            var sliders = new List<VisualElement>();
            var toggles = new List<Toggle>();
            var geo = SliderGeometry();
            Func<float, float, string> compact = EditorFieldsUtility.VpcCompactLabel ? (Func<float, float, string>)CompactText : null;
            float scale = EditorFieldsUtility.VpcPercentage ? 100f : 1f;
            ZuiSkinRangeSlider Range(string label, float lo, float hi, float absMin, float absMax, Action<float, float> set, Func<(float, float)> read, string tip) {
                var s = new ZuiSkinRangeSlider(label, lo * scale, hi * scale, absMin * scale, absMax * scale, geo,
                    (a, b) => {
                        a /= scale; b /= scale;
                        if (scale != 1f) { a = Mathf.Round(a * 100f) / 100f; b = Mathf.Round(b * 100f) / 100f; }
                        set(a, b);
                    }, null, compact == null ? null : (a, b) => compact(a / scale, b / scale));
                s.AddToClassList("zs-slider-minmax");
                s.tooltip = tip;
                if (compact != null) s.CompactWidth = CompactWidth;
                win.refreshers.Add(() => { var (a, b) = read(); s.SetValuesWithoutNotify(a * scale, b * scale); });
                Add(s);
                return s;
            }
            if (entry.local) {
                var z = zound;
                sliders.Add(Range("V", z.minVolume, z.maxVolume, Zound.MinVolumeRange, Zound.MaxVolumeRange, (a, b) => win.Modify("change entry volume", () => {
                    z.minVolume = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(a); z.maxVolume = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(b); }), () => (z.minVolume, z.maxVolume), "Volume: each play draws a level between these."));
                sliders.Add(Range("P", z.minPitch, z.maxPitch, Zound.MinPitchRange, Zound.MaxPitchRange, (a, b) => win.Modify("change entry pitch", () => {
                    z.minPitch = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(a); z.maxPitch = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(b); }), () => (z.minPitch, z.maxPitch), "Pitch: each play draws a pitch between these."));
                sliders.Add(Range("C", z.chance, z.chance, Zound.MinChanceRange, Zound.MaxChanceRange, (a, b) => win.Modify("change entry chance", () => {
                    z.chance = ZoundBrowserEditor<Klip>.RoundTo3DecimalPlaces(!Mathf.Approximately(a, z.chance) ? a : b); }), () => (z.chance, z.chance), "Chance that a trigger plays this track."));
            }
            else {
                sliders.Add(Range("V", entry.volume, entry.volume, Zound.MinVolumeRange, Zound.MaxVolumeRange, (a, b) => win.Modify("change entry volume", () => entry.volume = !Mathf.Approximately(a, entry.volume) ? a : b), () => (entry.volume, entry.volume), "Volume on this track: multiplies the shared sound's own, or overrides it (O)."));
                sliders.Add(Range("P", entry.pitch, entry.pitch, Zound.MinPitchRange, Zound.MaxPitchRange, (a, b) => win.Modify("change entry pitch", () => entry.pitch = !Mathf.Approximately(a, entry.pitch) ? a : b), () => (entry.pitch, entry.pitch), "Pitch on this track: multiplies the shared sound's own, or overrides it (O)."));
                sliders.Add(Range("C", entry.chance, entry.chance, Zound.MinChanceRange, Zound.MaxChanceRange, (a, b) => win.Modify("change entry chance", () => entry.chance = !Mathf.Approximately(a, entry.chance) ? a : b), () => (entry.chance, entry.chance), "Chance on this track: multiplies the shared sound's own, or overrides it (O)."));
                Toggle Over(bool v, string undo, Action<bool> set, Func<bool> read) {
                    var t = new Toggle("O") { value = v, tooltip = "Override.\n\nIf checked, then this will override the original value of the zound. If unchecked, then this will act as a multiplier of the original value." };
                    t.AddToClassList("zs-imgui-field"); t.AddToClassList("zs-label-16");
                    t.RegisterValueChangedCallback(e => win.Modify(undo, () => set(e.newValue)));
                    win.refreshers.Add(() => t.SetValueWithoutNotify(read()));
                    Add(t);
                    return t;
                }
                toggles.Add(Over(entry.overrideVolume, "toggle override entry volume", v => entry.overrideVolume = v, () => entry.overrideVolume));
                toggles.Add(Over(entry.overridePitch, "toggle override entry pitch", v => entry.overridePitch = v, () => entry.overridePitch));
                toggles.Add(Over(entry.overrideChance, "toggle override entry chance", v => entry.overrideChance = v, () => entry.overrideChance));
            }

            // The track's lane on the shared timeline (T-0560..T-0566): the piece where and how it sounds, and its gestures.
            strip = new TrackStripTK(win, entry);
            Add(strip);
            win.strips.Add(strip);

            // The curve bar: only a LOCAL Klip's curves may be edited from here (a shared sound is used elsewhere too; edit it
            // in its own editor). The same bar as the Klip editor's.
            if (entry.local && zound is Klip localKlip) {
                curveBar = new CurveBarTK(TrackCurves(localKlip),
                    () => localKlip.trimEnabled, v => win.Modify("toggle klip trim", () => { KlipChainEnvelopes.EnsureSourceAnchored(localKlip); localKlip.trimEnabled = v; Dsp.ZoundDspPlayback.InvalidateLayout(localKlip); }),
                    () => localKlip.trimEnabled ? "This track plays its sound's trimmed part. Click to play the whole recording." : "This track plays the whole recording. Click to play only its trimmed part (drag the piece's edges to set it).",
                    () => { int p = strip.EditingParam; return p == SourceStageParam.Speed ? 0 : p == SourceStageParam.Pitch ? 1 : p == SourceStageParam.Volume ? 2 : -1; },
                    i => strip.SetEditing(i == 0 ? SourceStageParam.Speed : i == 1 ? SourceStageParam.Pitch : i == 2 ? SourceStageParam.Volume : -1));
                curveBar.AddToClassList("zs-zequence-entry__curve-bar");
                Add(curveBar);
                win.refreshers.Add(curveBar.Sync);
            }

            Button dup = SmallIcon("duplicate", "Duplicate this track (a local sound is copied, never shared).", ZUICornerMask.Left, () => CompositeZoundEditing.DuplicateEntry(parent, index));
            Button rem = SmallIcon("remove", "Delete this track" + (entry.local ? " and its local sound." : "."), ZUICornerMask.Right, () => CompositeZoundEditing.RemoveEntry(parent, index));
            Add(dup); Add(rem);
            var mute = ZS.Toggle("M", "Mute/Unmute", entry.mute, v => win.Modify("toggle mute", () => { entry.mute = v; if (entry.mute) entry.solo = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Left, Btn, HeaderH, MuteOn);
            var solo = ZS.Toggle("S", "Toggle Solo", entry.solo, v => win.Modify("toggle solo", () => { entry.solo = v; if (entry.solo) entry.mute = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Right, Btn, HeaderH, SoloOn);
            win.refreshers.Add(() => { mute.SetValueWithoutNotify(entry.mute); ZS.ApplyOnColor(mute, MuteOn); solo.SetValueWithoutNotify(entry.solo); ZS.ApplyOnColor(solo, SoloOn); });
            Add(mute); Add(solo);
            Button convert = ConversionButton();
            if (convert != null) Add(convert);
            var heightGrip = HeightGrip(); Add(heightGrip);

            layouts.Add(w => {
                var rect = new Rect(0f, 0f, w, float.IsNaN(layout.height) || layout.height < MinHeight ? HeightOf(entry) : layout.height);
                var content = isGroupChild ? new Rect(rect.x + W.GroupEntryLeftOffset, rect.y + Pad, rect.width - W.GroupEntryLeftOffset, rect.height - Pad * 2f)
                                           : new Rect(rect.x + Pad, rect.y + Pad, rect.width - Pad * 2f, rect.height - Pad * 2f);
                flashRect = content;

                // ── header row, from the right end backwards so the name takes what is left ──
                float y = content.y;
                float xr = content.xMax;
                if (convert != null) { Place(convert, new Rect(xr - Btn, y, Btn, HeaderH)); xr -= Btn + 2f; }
                Place(rem, new Rect(xr - Btn, y, Btn, HeaderH)); Place(dup, new Rect(xr - Btn * 2f, y, Btn, HeaderH)); xr -= Btn * 2f + 2f;
                Place(solo, new Rect(xr - Btn, y, Btn, HeaderH)); Place(mute, new Rect(xr - Btn * 2f, y, Btn, HeaderH)); xr -= Btn * 2f + 6f;
                if (curveBar != null) {
                    float bw = CurveBarTK.TrimW + 4f + 3f * (CurveBarTK.NameW + CurveBarTK.IconW * 2f) + 2f * 4f + 14f;
                    Place(curveBar, new Rect(xr - bw, y + 1f, bw, CurveBarTK.H)); xr -= bw + 6f;
                }
                Place(start, new Rect(xr - 56f, y, 56f, HeaderH)); xr -= 56f;
                Place(startLabel, new Rect(xr - 30f, y, 30f, HeaderH)); xr -= 30f + 4f;

                float xl = content.x;
                Place(grip, new Rect(xl, y, 10f, HeaderH)); xl += 12f;
                if (weight != null) { Place(weight, new Rect(xl, y, 22f, HeaderH)); xl += 24f; }
                Place(play, new Rect(xl, y, 18f, HeaderH)); xl += 18f;
                // The id chip is sized to its text (a bolt, or a bolt and the id), the length label is fixed; the name takes the rest.
                float textW = string.IsNullOrEmpty(idText.text) ? 0f : idText.MeasureTextSize(idText.text, 0f, MeasureMode.Undefined, HeaderH, MeasureMode.Exactly).x + 6f;
                float chipW = Mathf.Clamp(14f + textW, 16f, 120f);
                const float durW = 52f;
                float nameW = Mathf.Max(40f, xr - xl - chipW - durW - 4f);
                Place(name, new Rect(xl, y, nameW, HeaderH)); xl += nameW;
                Place(duration, new Rect(xl, y, durW, HeaderH)); xl += durW + 2f;
                Place(idChip, new Rect(xl, y, chipW, HeaderH));
                // The length a play really lasts (the live plan: trim or excerpt, curves, stretch), at the drawn speed (T-0502).
                float dur = PlacedLength(out float endsAt);
                duration.text = dur.ToString("0.00") + " s";
                duration.tooltip = "How long this track plays, from its original audio through its live effects, at the middle of its pitch range" + (endsAt > dur + 1e-3f ? "; it ends at " + endsAt.ToString("0.00") + " s on the Zequence's timeline." : ".");
                name.tooltip = "Open this sound's own editor." + (entry.local ? "" : "\n\nA shared sound: its curves and effects are edited there, since other sounds use it too.");

                // ── the body: sliders down the left, the lane filling the rest, the grip along the bottom ──
                float by = content.y + HeaderH + 2f;
                float bh = content.yMax - GripH - by;
                for (int i = 0; i < sliders.Count; i++) {
                    float sy = by + i * (LH + 3f);
                    if (entry.local) Place(sliders[i], new Rect(content.x, sy, LeftW - 1f, LH));
                    else { Place(sliders[i], new Rect(content.x, sy, LeftW - 24f, LH)); Place(toggles[i], new Rect(content.x + LeftW - 22f, sy, 20f, LH)); }
                }
                var lane = new Rect(content.x + LeftW + 5f, by, content.width - LeftW - 5f, Mathf.Max(8f, bh));
                // The strip keeps to the shared lane horizontally (it places itself); here only its rows.
                strip.style.top = lane.y; strip.style.height = lane.height;
                if (win.timeline == null || win.timeline.laneWorld.width < 2f) { strip.style.left = lane.x; strip.style.width = lane.width; }
                Place(heightGrip, new Rect(content.x, content.yMax - GripH, content.width, GripH));
            });

            // The flash while this entry sounds (its playheads are the strip's, where each play reads its source).
            win.liveRefreshers.Add(() => {
                bool flashing = false;
                if (ZoundEngine.CullingGroups.TryGetValue(parent, out var playingTokens)) {
                    foreach (var token in playingTokens) {
                        if (token == null || token.state == ZoundToken.State.Killed) continue;
                        if (!token.TryGetEntryToken(entry, out var child) || child.state == ZoundToken.State.Killed) continue;
                        if (token.IsEntryMuted(entry)) continue;
                        if (token.soloOverride != null && token.soloOverride != entry) continue;
                        if (token.zound is Zequence tz && token.isRealtime && tz.mode != CompositeZound.Mode.Parallel && token.playedEntryIndex != index) continue;
                        flashing = true;
                    }
                }
                Flash(flashing);
            });
        }

        /// <summary>The local Klip's three curves as the shared bar sees them (on / selected / shown), written through the project's modify path.</summary>
        List<CurveBarTK.Curve> TrackCurves(Klip k) {
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            ZoundModifier Mod(Func<Zound, bool, Envelope> curveOf) {
                var env = curveOf(k, false);
                int mi = env != null ? KlipChainEnvelopes.ModifierIndexOf(k, env) : -1;
                var chain = Dsp.ZoundDspPlayback.ResolveChain(k, out _);
                return mi >= 0 && chain != null && mi < chain.modifiers.Count ? chain.modifiers[mi] : null;
            }
            CurveBarTK.Curve Make(string label, Color colour, Func<Zound, bool, Envelope> curveOf, Action<bool> setOn) => new CurveBarTK.Curve {
                label = label, colour = colour,
                enabled = () => { var e = curveOf(k, false); return e != null && e.enabled; },
                setEnabled = v => { win.Modify("toggle klip " + label.ToLowerInvariant() + " curve", () => setOn(v)); strip?.Sync(); },
                shown = () => CurveView.IsVisible(Mod(curveOf)),
                setShown = v => { CurveView.SetVisible(Mod(curveOf), v); strip?.Sync(); },
            };
            return new List<CurveBarTK.Curve> {
                Make("Time", AudioSpectrumView.TimeCurveColor, KlipChainEnvelopes.TimeCurve, v => KlipChainEnvelopes.SetTimeEnabled(k, v)),
                Make("Pitch", es.pitchEnvelopeColor, KlipChainEnvelopes.PitchCurve, v => KlipChainEnvelopes.SetPitchEnabled(k, v)),
                Make("Vol", es.volumeEnvelopeColor, KlipChainEnvelopes.VolumeCurve, v => KlipChainEnvelopes.SetVolumeEnabled(k, v)),
            };
        }

        /// <summary>This track's drawn length and where it ends on the Zequence timeline, from the shared placements.</summary>
        float PlacedLength(out float endsAt) {
            endsAt = 0f;
            if (win.timeline != null && win.timeline.byEntry.TryGetValue(entry, out var p) && p.found) { endsAt = p.End; return p.PlayLength; }
            float dur = CompositeZoundEditing.GetEntryDuration(parent, entry, parentPitch);
            endsAt = parentDelay + entry.delay / Mathf.Max(parentPitch, 0.01f) + dur;
            return dur;
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
            e.AddToClassList("zs-zequence-entry__positioned-control"); e.style.backgroundColor = c;
            Add(e);
            return e;
        }

        Button ConversionButton() {
            string icon = null, tip = null;
            if (zound is Klip k) {
                if (entry.local) { icon = k.originalId == 0 ? "make-shared" : "reconnect-shared"; tip = k.originalId == 0 ? MakeSharedTip : ReconnectTip; }
                else { icon = "break-to-local"; tip = BreakTip; }
            }
            else if (zound is Zequence && !entry.local) { icon = "break-to-local"; tip = BreakTip; }
            if (icon == null) return null;
            return SmallIcon(icon, tip, ZUICornerMask.All, () => CompositeZoundEditing.ConvertEntry(parent, index));
        }

        const string MakeSharedTip = "<b>Convert to Shared Klip</b>\n\nConvert this Klip into a Shared Klip where it will be listed in Klip browser. Shared Klips can be used across different Zequence.";
        const string BreakTip = "<b>Break as Local Klip</b>\n\nConvert this Klip into a Local Klip where it will only be available internally in this Zequence. This will break the dependency from the original configuration of the Shared Klip, and the Shared Klip's configuration will also no longer affected by this Klip.";
        const string ReconnectTip = "<b>Reconnect to Original Shared Klip</b>\n\nConvert this Klip back into its original Shared Klip. If the original Shared Klip has been removed, then this will fallback into creating a new Shared Klip.";

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
            var grip = Grip(); Add(grip);
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
            var play = ZS.Button("►", "Play", "RichButton", () => CompositeZoundEditing.ToggleEntryPlay(win.zeq, ref win.entryTokens, entry, win), ZUICornerMask.All, 18f, lh);
            Add(play);
            win.liveRefreshers.Add(() => { bool p = ZoundPreviewPlayback.IsLoopPlaying(win, entry); play.text = p ? "⏹" : "►"; play.tooltip = p ? "Stop loop" : "Play this entry."; });
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
            var duration = new Label { pickingMode = PickingMode.Ignore };
            duration.AddToClassList("zs-lbl"); duration.AddToClassList("zs-greymini"); duration.AddToClassList("zs-zequence-entry__duration-label");
            Add(duration);

            // Right: the local Zequence's own fields, the button strip, the delay, its master envelope.
            var fieldsRow = new ZoundFieldsRowTK(comp, true, null, drawName: false, drawTags: false);
            fieldsRow.AddToClassList("zs-zequence-entry__group-fields-row");
            Add(fieldsRow);
            win.refreshers.Add(fieldsRow.Sync);
            var renameBtn = ZS.Button(entry.editor_isRenaming ? "Done" : "Rename", "", "RichButton", () => {
                entry.editor_isRenaming = !entry.editor_isRenaming;
                EditorUtility.SetDirty(ZoundsProject.Instance);
            }, ZUICornerMask.All, 60f, 20f);
            Add(renameBtn);
            var dup = W.IconButton("duplicate", "Duplicate this zound entry.", "RichButton", ZUICornerMask.Left, -1f, 20f, () => CompositeZoundEditing.DuplicateEntry(parent, index));
            var rem = W.IconButton("remove", "Delete this zound entry.", "RichButton", ZUICornerMask.Right, -1f, 20f, () => CompositeZoundEditing.RemoveEntry(parent, index));
            var mute = ZS.Toggle("M", "Mute/Unmute", entry.mute, v => win.Modify("toggle mute", () => { entry.mute = v; if (entry.mute) entry.solo = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Left, -1f, 20f, MuteOn);
            var solo = ZS.Toggle("S", "Toggle Solo", entry.solo, v => win.Modify("toggle solo", () => { entry.solo = v; if (entry.solo) entry.mute = false; }), "ZoundBtnFlatToggle", ZUICornerMask.Right, -1f, 20f, SoloOn);
            var conv = W.IconButton(comp.originalId == 0 ? "make-shared" : "reconnect-shared", comp.originalId == 0 ? MakeSharedTip : ReconnectTip, "RichButton", ZUICornerMask.All, -1f, 20f, () => CompositeZoundEditing.ConvertEntry(parent, index));
            foreach (var b in new VisualElement[] { dup, rem, mute, solo, conv }) Add(b);
            win.refreshers.Add(() => { mute.SetValueWithoutNotify(entry.mute); ZS.ApplyOnColor(mute, MuteOn); solo.SetValueWithoutNotify(entry.solo); ZS.ApplyOnColor(solo, SoloOn); });
            var startLabel = new Label("Start") { pickingMode = PickingMode.Ignore };
            startLabel.AddToClassList("zs-lbl"); startLabel.AddToClassList("zs-mini"); startLabel.AddToClassList("zs-zequence-entry__start-label");
            Add(startLabel);
            var start = Z.Float(entry.delay, "When this group starts, in seconds after the Zequence starts. Drag to scrub, or type.", v => win.Modify("change zequence entry delay", () => {
                entry.delay = Mathf.Max(0f, v);
                CompositeZoundEditing.RecalculateMaxDuration(win.zeq, win.AutoDuration);
            }), 56f, 2);
            start.AddToClassList("zs-imgui-field"); start.AddToClassList("zs-bare-int");
            win.refreshers.Add(() => { if (start.focusController?.focusedElement == null || !start.Contains(start.focusController.focusedElement as VisualElement)) start.SetValueWithoutNotify(entry.delay); });
            Add(start);
            VisualElement envBG = null; EnvelopeTK curve = null;
            if (comp is Zequence && entry.volumeEnvelope.enabled) {
                envBG = Abs(new Color(0.75f, 0.75f, 0.75f, 0.1f));
                var copy = entry.volumeEnvelope.DeepCopy();
                curve = new EnvelopeTK(copy, es.volumeEnvelopeColor) { thickness = es.volumeEnvelopeThickness };
                curve.AddToClassList("zs-zequence-entry__group-curve");
                curve.onChanged = () => win.Modify("modify group volume envelope", () => { entry.volumeEnvelope = copy.DeepCopy(); entry.volumeEnvelope.enabled = true; });
                Add(curve);
            }

            // Children and the group's add buttons.
            var children = new List<ZequenceEntryTK>();
            Button addKlip = null, addShared = null;
            if (entry.editor_foldoutExpanded) {
                bool darker = false;
                for (int i = 0; i < comp.zoundEntries.Count; i++) {
                    float groupPitch = parentPitch * (entry.overridePitch ? entry.pitch : entry.pitch * ZequenceTimeline.Mid(comp));
                    var child = new ZequenceEntryTK(win, comp, comp.zoundEntries[i], i, groupPitch, parentDelay + entry.delay / Mathf.Max(parentPitch, 0.01f), darker, true);
                    child.AddToClassList("zs-zequence-entry__group-child");
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
                Place(grip, new Rect(left.x, left.y, 10f, lh)); left.x += 12f; left.width -= 12f;
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
                float dur = PlacedLength(out float endsAt);
                Place(duration, new Rect(left.x, y, left.width * 0.75f, lh));
                duration.text = dur.ToString("0.00") + " s" + (endsAt > dur + 1e-3f ? " (" + endsAt.ToString("0.00") + " s)" : "");

                float ry = right.y;
                Place(fieldsRow, new Rect(right.x, ry, right.width, lh));
                ry += lh + 2f;
                Place(renameBtn, new Rect(right.x, ry, 60f, 20f));
                float bx = right.x + 62f;
                foreach (var b in new VisualElement[] { dup, rem, mute, solo, conv }) { Place(b, new Rect(bx, ry, 24f, 20f)); bx += 26f; }
                Place(startLabel, new Rect(bx + 6f, ry, 30f, 20f)); Place(start, new Rect(bx + 36f, ry, 56f, 20f));
                ry += 22f;
                if (comp is Zequence) {
                    if (entry.volumeEnvelope.enabled) {
                        float globalMax = win.zeq.editor_maxDuration / win.zeq.minPitch, globalDelay = entry.delay / win.zeq.minPitch;
                        float total = right.width - 50f - 15f;
                        float off = globalDelay / globalMax * total;
                        var bgR = new Rect(right.x + off + 5f, ry, dur / globalMax * total, lh * 4f);
                        Place(envBG, bgR); Place(curve, bgR);
                        ry += lh * 4f;
                    }
                    else ry += lh;
                }
                flashRect = new Rect(content.x, content.y, content.width, ry - content.y);

                float cy = content.y + W.GroupHeaderHeight + (comp is Zequence ? (entry.volumeEnvelope.enabled ? lh * 4f : lh) : 0f);
                foreach (var c in children) { float ch = HeightOf(c.entry); W.Place(c, content.x, cy, content.width, ch); cy += ch + 4f; }
                if (addKlip != null) {
                    float ay = cy + 2f;
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
