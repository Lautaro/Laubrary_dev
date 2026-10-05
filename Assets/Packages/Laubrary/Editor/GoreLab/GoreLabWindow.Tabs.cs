// The left pane: undo / redo and the tab row on one line, the member chooser, then ONLY the active tab's controls.
using System.Collections.Generic;
using Laubrary.Launimator;
using Laubrary.Zoetrope;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    public partial class GoreLabWindow
    {
        internal delegate void TagChange(ref MemberTag t);

        const float SliderW = 150f;

        void BuildLeft(VisualElement host)
        {
            var rig = Rig;
            if (rig == null) return;

            var tabs = Z.Segmented((int)tab, TabNames, "The tab is the mode: it decides what a drag on the frame does.", i => SetTab((Tab)i));
            for (int i = 0; i < TabTips.Length; i++) tabs.SegmentAt(i).tooltip = TabTips[i];
            var first = Z.Row(
                Z.IconButton("arrow-counter-clockwise", "Undo the last edit to the rig (Ctrl+Z).", Undo.PerformUndo),
                Z.IconButton("arrow-clockwise", "Redo (Ctrl+Y).", Undo.PerformRedo),
                tabs);
            host.Add(first);
            host.Add(Z.Row(
                Z.MicroSlider("3D shape", shapeAlpha, 0.05f, 1f,
                    "How visible the 3D shape on the sprite is: the sphere or box wireframe, its dots and letters. Lower it to see the sprite through them.",
                    v => { shapeAlpha = v; stage?.Refresh(); }, SliderW),
                Z.MicroSlider("Outline", outlineAlpha, 0.05f, 1f,
                    "How visible the dashed outlines are: the marked shape of the active member and, fainter, the other members'.",
                    v => { outlineAlpha = v; stage?.Refresh(); }, SliderW)));

            if (tab != Tab.Test) host.Add(BuildMemberChooser());
            if (!HasFrames && tab != Tab.Frame) host.Add(BuildTargetRow());

            switch (tab)
            {
                case Tab.Shape: BuildShapeTab(host); break;
                case Tab.Paint: BuildPaintTab(host); break;
                case Tab.Frame: BuildFrameTab(host); break;
                case Tab.Test: BuildTestTab(host); break;
            }
        }

        void SetTab(Tab t)
        {
            if (tab == t) return;
            tab = t;
            hideFar = false;
            pendingWound.Clear();
            if (tab == Tab.Test) RecutShown();
            AfterEdit();
        }

        VisualElement BuildMemberChooser()
        {
            if (MemberCount == 0)
                return Z.Row(
                    Z.Text("No members declared", ZuiText.Subtle, "A rig needs body members (a head ball, a torso box...) before anything can be tagged."),
                    Z.Button("Add head and torso", "Give this rig the two default members: a Head (a ball) and a Torso (a box).",
                        () => Edit("Add members", () => AddDefaultMembers(Rig))));

            var names = new string[MemberCount];
            for (int i = 0; i < names.Length; i++) names[i] = MemberName(i);
            var seg = Z.Segmented(memberIndex, names, "The member every tab edits.", i => { memberIndex = i; AfterEdit(); });
            for (int i = 0; i < names.Length; i++)
                seg.SegmentAt(i).tooltip = $"Edit the {names[i]} ({(Rig.members[i] != null && Rig.members[i].kind == MemberKind.Box ? "a box" : "a ball")}) on every tab.";
            return Z.Row(seg);
        }

        /// Change the active member's tag on the shown frame. Records first; the stage redraws, the panes do not
        /// (a slider drag must not rebuild the slider under the pointer).
        void ChangeTag(string what, TagChange change)
        {
            var mf = ActiveMemberFrame(false);
            if (mf == null || !mf.present) return;
            Record(what);
            var t = mf.tag;
            change(ref t);
            mf.tag = t;
            stage?.Refresh();
        }

        // ── Shape ─────────────────────────────────────────────────────────────────────────────────────

        void BuildShapeTab(VisualElement host)
        {
            bool has = TryActiveTag(out var tag);
            bool editable = has && CanEditShown;
            bool box = ActiveMember != null && ActiveMember.kind == MemberKind.Box;

            var sq = Z.MicroSlider("Squareness", has ? (float)tag.n : box ? 4f : 2f, 1.5f, 8f,
                "Shape of the outline: 2 is an oval, higher is boxier. Only pixels inside the outline are ever cut; for a box it also rounds the 3D solid.",
                v => ChangeTag("Squareness", (ref MemberTag t) => t.n = v), SliderW);
            sq.SetEnabled(editable);

            var row = Z.Row(sq);
            if (box)
            {
                var depth = Z.MicroSlider("Depth", has ? (float)GoreTagEdit.Depth(tag) : 4f, 1f, 24f,
                    "How thick the box is front to back, in pixels (its half depth).",
                    v => ChangeTag("Depth", (ref MemberTag t) => t.rz = v), SliderW);
                depth.SetEnabled(editable);
                row.Add(depth);
            }
            var clear = Z.IconButton("trash", $"Delete the {MemberName(memberIndex)}'s shape on this frame, so a new box can be drawn (undoable).",
                () => Edit("Clear shape", () => { var mf = ActiveMemberFrame(false); if (mf != null) mf.present = false; }));
            clear.SetEnabled(editable);
            var copy = Z.IconButton("copy", $"Copy the {MemberName(memberIndex)}'s marker on this frame: shape, orientation and paint (Ctrl+C with the frame focused).", CopyShape);
            copy.SetEnabled(editable);
            var paste = Z.IconButton("clipboard", HasShapeClip
                    ? "Paste the copied marker onto this frame, replacing the shape here (undoable, Ctrl+V). Placed by feet and middle, so frames of another size line up."
                    : "Paste the copied marker onto this frame. Nothing is copied yet.", PasteShape);
            paste.SetEnabled(CanEditShown && HasShapeClip && ActiveMember != null);
            host.Add(row);
            host.Add(Z.Row(copy, paste, clear));
            BuildTurnRows(host, editable);
        }

        // ── Turning (part of the Shape tab) ───────────────────────────────────────────────────────────

        // The turning controls sit under the shape's own sliders: a fixed-angle turn, the direction default, hold-to-hide-far-side and the outline opacity.
        void BuildTurnRows(VisualElement host, bool editable)
        {
            string me = MemberName(memberIndex);

            VisualElement Turn(string label, float deg, string tip)
            {
                var b = Z.Button(label, tip, () => Edit("Turn " + me, () => TurnActive(deg)));
                b.SetEnabled(editable);
                return b;
            }
            host.Add(Z.Row(
                Turn("-45°", -45f, $"Turn the {me} 45° about its up axis, front toward its left (west)."),
                Turn("+45°", 45f, $"Turn the {me} 45° about its up axis, front toward its right (east)."),
                Turn("180°", 180f, $"Turn the {me} round to face the other way.")));

            string dirTip = shown != null && !float.IsNaN(shown.group.angle)
                ? $"the way the {shown.group.label} direction looks"
                : "the viewer (this frame is not part of a direction set)";
            var def = Z.Button("Default", $"Point the {me}'s forward at {dirTip}, on this frame (undoable).",
                () => Edit("Default orientation", () => FaceDefault(false)));
            def.SetEnabled(editable);
            var defAll = Z.Button("Default all", $"The same for every frame of this direction that has a {me} tag (undoable).",
                () => Edit("Default orientation", () => FaceDefault(true)));
            defAll.SetEnabled(CanEditShown);
            host.Add(Z.Row(def, defAll, HoldHideFar()));
        }

        void TurnActive(float deg)
        {
            var mf = ActiveMemberFrame(false);
            if (mf == null || !mf.present) return;
            var t = mf.tag;
            GoreTagEdit.Yaw(ref t, deg);
            mf.tag = t;
        }

        void FaceDefault(bool wholeDirection)
        {
            if (!CanEditShown) return;
            var fwd = DefaultForward();
            var sprites = wholeDirection ? shown.group.sprites : new List<Sprite> { shown.sprite };
            foreach (var s in sprites)
            {
                var mf = MemberAt(FindFrame(s), memberIndex, false);
                if (mf == null || !mf.present) continue;
                var t = mf.tag;
                GoreTagEdit.SetForward(ref t, fwd);
                mf.tag = t;
            }
        }

        /// A button that hides every far-side line and letter while it is HELD (not a latch).
        VisualElement HoldHideFar()
        {
            var b = Z.Button("Hide far side", "Hold this: every line and letter on the far side of the member disappears, so only what faces you remains.", () => { });
            b.AddToClassList("zui-togglebutton");
            void Set(bool on)
            {
                hideFar = on;
                b.EnableInClassList("zui-togglebutton--on", on);
                stage?.Refresh();
            }
            b.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) Set(true); }, TrickleDown.TrickleDown);
            b.RegisterCallback<PointerUpEvent>(_ => Set(false), TrickleDown.TrickleDown);
            b.RegisterCallback<PointerCaptureOutEvent>(_ => Set(false));
            b.RegisterCallback<PointerLeaveEvent>(_ => { if (hideFar) Set(false); });
            return b;
        }

        // ── Paint ─────────────────────────────────────────────────────────────────────────────────────

        void BuildPaintTab(VisualElement host)
        {
            bool editable = TryActiveTag(out _) && CanEditShown;
            string me = MemberName(memberIndex);

            var layer = Z.Segmented(paintLayer, new[] { "Behind", "In front" }, "Which mask the brush paints.", i => { paintLayer = i; stage?.Refresh(); });
            layer.SegmentAt(0).tooltip = $"Behind: body behind the {me}, for example the neck. When a cut removes it, it stays as dark gore instead of turning see-through.";
            layer.SegmentAt(1).tooltip = $"In front: something drawn in front of the {me}, for example an arm over the torso. It is never cut.";
            var mode = Z.Segmented(paintErase ? 1 : 0, new[] { "Paint", "Erase" }, "Left-drag paints or erases; a right-drag always erases.", i => paintErase = i == 1);
            host.Add(Z.Row(layer, mode));

            var brush = BuildBrushRadio();
            var fill = Z.Button("Fill shape", $"Paint every solid pixel inside the {me}'s outline into the chosen mask (undoable).",
                () => Edit("Fill mask", FillMask));
            fill.SetEnabled(editable);
            var clear = Z.IconButton("trash", $"Clear the chosen mask of the {me} on this frame (undoable).",
                () => Edit("Clear mask", ClearMask));
            clear.SetEnabled(editable);
            host.Add(Z.Row(brush, fill, clear));

            host.Add(PaintLayerGroup("Behind", "the Behind paint", BehindColour, c => behindColor = c, behindAlpha, v => behindAlpha = v));
            host.Add(PaintLayerGroup("In front", "the In front paint", FrontColour, c => frontColor = c, frontAlpha, v => frontAlpha = v));
        }

        /// One paint layer's look: its colour picker and its opacity slider together in a framed group, so they read as one control.
        VisualElement PaintLayerGroup(string label, string what, Color colour, System.Action<Color> setColour, float alpha, System.Action<float> setAlpha)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            box.style.paddingLeft = box.style.paddingRight = 4f;
            box.style.paddingTop = box.style.paddingBottom = 3f;
            box.style.marginTop = 2f;
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 1f;
            box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = new Color(1f, 1f, 1f, 0.18f);
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 3f;
            var pick = Z.Color(colour, $"The colour of {what}. Only changes how it looks in this window; the paint itself is the same.",
                c => { c.a = 1f; setColour(c); stage?.Refresh(); }, 52f);
            var slider = Z.MicroSlider(label, alpha, 0.05f, 1f, $"How visible {what} is. Lower it to see the pixels under it.",
                v => { setAlpha(v); stage?.Refresh(); }, 150f);
            box.Add(pick);
            box.Add(slider);
            return box;
        }

        /// Six brushes, each drawn at its own size so the choice is visible: a round dot as wide as the brush reaches.
        VisualElement BuildBrushRadio()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var buttons = new Button[7];
            for (int size = 1; size <= 6; size++)
            {
                int sz = size;
                var b = Z.Button("", $"Brush {sz}: radius {sz} pixel{(sz > 1 ? "s" : "")}. Only solid sprite pixels inside the outline are painted.", () =>
                {
                    brushSize = sz;
                    for (int k = 1; k <= 6; k++) MarkBrushButton(buttons[k], k == brushSize);
                    stage?.Refresh();
                });
                b.style.width = 30f;
                b.style.height = 30f;
                b.style.marginRight = 2f;
                b.style.alignItems = Align.Center;
                b.style.justifyContent = Justify.Center;
                b.style.paddingLeft = b.style.paddingRight = b.style.paddingTop = b.style.paddingBottom = 0f;
                float d = 4f + 3f * sz;
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.style.width = d; dot.style.height = d;
                dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = d * 0.5f;
                dot.style.backgroundColor = new Color(0.92f, 0.94f, 1f);
                b.Add(dot);
                MarkBrushButton(b, sz == brushSize);
                buttons[sz] = b;
                row.Add(b);
            }
            return row;
        }

        // The chosen brush is the one with an orange frame and a warm fill (the radio class alone does not show on a plain button).
        static void MarkBrushButton(Button b, bool on)
        {
            b.EnableInClassList("zui-radio__on", on);
            var orange = new Color(1f, 0.65f, 0.2f);
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = on ? orange : new Color(0f, 0f, 0f, 0f);
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = on ? 2f : 0f;
            b.style.backgroundColor = on ? new Color(0.55f, 0.33f, 0.1f, 0.85f) : new StyleColor(StyleKeyword.Null);
        }

        void FillMask()
        {
            var mf = ActiveMemberFrame(false);
            if (mf == null || !mf.present) return;
            var mine = new HashSet<int>(paintLayer == 1 ? mf.exempt ?? new int[0] : mf.behind ?? new int[0]);
            var other = new HashSet<int>(paintLayer == 1 ? mf.behind ?? new int[0] : mf.exempt ?? new int[0]);
            var g = shown.pixels.grid;
            for (int y = 0; y < g.h; y++)
                for (int x = 0; x < g.w; x++)
                {
                    if (!g.Solid(x, y) || !GoreTagEdit.Inside(mf.tag, x + 0.5, y + 0.5)) continue;
                    int k = y * g.w + x;
                    mine.Add(k);
                    other.Remove(k);
                }
            WriteMasks(mf, mine, other);
        }

        void ClearMask()
        {
            var mf = ActiveMemberFrame(false);
            if (mf == null) return;
            if (paintLayer == 1) mf.exempt = new int[0]; else mf.behind = new int[0];
        }

        /// `mine` is the chosen layer, `other` the opposite one (a pixel is in at most one).
        internal void WriteMasks(GoreMemberFrame mf, HashSet<int> mine, HashSet<int> other)
        {
            if (paintLayer == 1) { mf.exempt = GoreTagEdit.ToArray(mine); mf.behind = GoreTagEdit.ToArray(other); }
            else { mf.behind = GoreTagEdit.ToArray(mine); mf.exempt = GoreTagEdit.ToArray(other); }
        }

        // ── Frame ─────────────────────────────────────────────────────────────────────────────────────

        void BuildFrameTab(VisualElement host)
        {
            string where = shown == null ? "No frame" : $"{shown.Label} of {shown.group.sprites.Count}" + (shown.mirrored ? " (mirror)" : "");
            host.Add(Z.Row(
                Z.IconButton("caret-left", "Previous frame of this direction.", () => StepFrame(-1)),
                Z.IconButton("caret-right", "Next frame of this direction.", () => StepFrame(1)),
                Z.IconButton("caret-double-left", "Previous direction (the same frame number).", () => StepDirection(-1)),
                Z.IconButton("caret-double-right", "Next direction (the same frame number).", () => StepDirection(1)),
                Z.Text(where, ZuiText.Body, "The frame shown on the stage. Mirrored directions are derived from their drawn partner and read only.")));

            string me = MemberName(memberIndex);
            var mf = ActiveMemberFrame(false);
            bool skip = mf != null && mf.skip;
            var hidden = Z.ToggleButton($"No {me.ToLowerInvariant()} here",
                skip ? $"On: this frame is used as drawn for the {me} (nothing of it is cut here). Click to make it cuttable again."
                     : $"Off: the {me} is cut on this frame. Click when the {me} is not visible here (for example a lying pose), so the frame plays as drawn.",
                skip, on => Edit("Member visibility", () =>
                {
                    var m = ActiveMemberFrame(true);
                    if (m != null) m.skip = on;
                }));
            hidden.SetEnabled(CanEditShown && ActiveMember != null);
            host.Add(Z.Row(hidden));
            string meL = me.ToLowerInvariant();
            var copyNext = Z.Button("Copy to next", $"Copy the {meL}'s shape, orientation and paint from this frame to the next frame of this direction (undoable). Then nudge it into place.", () => CopyMemberForward(false));
            var copyRest = Z.Button("Copy to rest", $"Copy the {meL} from this frame to every later frame of this direction (undoable).", () => CopyMemberForward(true));
            var auto = Z.Button("Auto-tag", $"Put a rough {meL} on every frame of this direction that has none, as a starting point to correct (undoable). The torso guess needs the head tagged first.", AutoTagDirection);
            copyNext.SetEnabled(CanEditShown && ActiveMember != null);
            copyRest.SetEnabled(CanEditShown && ActiveMember != null);
            auto.SetEnabled(CanEditShown && ActiveMember != null);
            host.Add(Z.Row(copyNext, copyRest, auto));
            host.Add(BuildTargetRow());
            var attach = Z.Button("Attach to selected",
                "Puts the wound component on the character(s) selected in the scene or project and points it at this rig, so they can be wounded in play (undoable). The rig's target is only for tagging; this is what makes a game character show wounds.",
                AttachToSelection);
            host.Add(Z.Row(attach));
        }

        void AttachToSelection()
        {
            var rig = Rig;
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                GoreBody body = go.GetComponent<GoreBody>();
                if (EditorUtility.IsPersistent(go))
                {
                    if (body == null) body = go.AddComponent<GoreBody>();
                    body.rig = rig;
                    EditorUtility.SetDirty(go);
                }
                else
                {
                    if (body == null) body = Undo.AddComponent<GoreBody>(go);
                    Undo.RecordObject(body, "Attach GoreLab rig");
                    body.rig = rig;
                    EditorUtility.SetDirty(body);
                }
                n++;
            }
            ShowHint(n == 0 ? "Select the character in the scene (or a prefab) first." : $"Attached to {n} object{(n > 1 ? "s" : "")}.");
        }

        /// The rig's target: the character (or bare animation) whose frames are tagged. The character never learns
        /// about the rig; the rig points at it.
        VisualElement BuildTargetRow()
        {
            var rig = Rig;
            var zoe = Z.Object<Zoe>(rig.zoe, "The character whose frames this rig tags. The character does not know this rig exists.",
                v => { Undo.RecordObject(rig, "Set rig target"); rig.zoe = v; EditorUtility.SetDirty(rig); Rebuild(); }, 170f);
            var reel = Z.Object<LauminaryVersion>(rig.reel, "Used instead of a character when the rig targets a bare animation version.",
                v => { Undo.RecordObject(rig, "Set rig target"); rig.reel = v; EditorUtility.SetDirty(rig); Rebuild(); }, 170f);
            return Z.Column(
                Z.Field("Character", "The character whose frames this rig tags. The character does not know this rig exists.", zoe),
                Z.Field("Animation", "Used instead of a character when the rig targets a bare animation version.", reel));
        }
    }
}
