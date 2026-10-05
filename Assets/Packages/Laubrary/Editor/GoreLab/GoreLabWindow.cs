// GoreLabWindow — the authoring window for a GoreRig: tag body members (head ball, torso box...) on every drawn frame of
// the rig's target, paint the per-member "behind" / "in front" masks, and test the rig's damage types on a frame with
// the same engine the game runs.
//
// Shape: the shared tool layout (controls left, workspace right, Z.Split). The left pane is ONE row of tabs (Shape,
// Paint, Frame, Test) where the tab IS the mode: it decides what a drag on the stage does, so there are no
// separate mode toggles. The right pane is the frame strip (grouped by direction) over the stage.
//
// Undo: every data edit records the rig before it mutates it (Undo.RecordObject) and a drag collapses into one step.
// ZuiWindow rebuilds the whole window after an undo/redo, so all view state (tab, frame, zoom, opacities) lives in
// fields of this window and the stage is rebuilt from them.
//
// Partial files: .Frames (the frame list, the strip, the shown frame), .Tabs (the left pane), .Edit (data access and
// undo), .Test (wounding a frame with the engine). The stage is GoreStage (.Draw, .Gestures).
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    public partial class GoreLabWindow : ZuiAssetWindow<GoreRig>
    {
        [MenuItem("Laubrary/GoreLab")]
        static void OpenMenu() => GetWindow<GoreLabWindow>("GoreLab");

        /// Entry point for LauAsset fields' Edit action and for double-clicking a rig.
        public static void OpenFor(GoreRig rig)
        {
            var w = GetWindow<GoreLabWindow>("GoreLab");
            if (rig != null) w.SetAsset(rig);
        }

        /// LauAssetEditors' Create hook: a fresh rig with its default members and damage types.
        public static GoreRig CreateRig(string name, string folder)
        {
            var rig = AssetLibrary<GoreRig>.Create(name, folder);
            if (rig == null) return null;
            SeedDefaults(rig);
            EditorUtility.SetDirty(rig);
            AssetDatabase.SaveAssetIfDirty(rig);
            return rig;
        }

        protected override string TypeLabel => "Gore Rig";
        protected override string DefaultFolder => "Assets/GoreLab";
        protected override string NewAssetName => "GoreRig";
        protected override string PresentationTool => "gorelab";

        protected override void InitializeNewAsset(GoreRig rig) => SeedDefaults(rig);

        // ── view state (not undoable: none of it is part of the asset) ─────────────────────────────────

        internal enum Tab { Shape, Paint, Frame, Test }
        static readonly string[] TabNames = { "Shape", "Paint", "Frame", "Test" };
        static readonly string[] TabTips =
        {
            "Shape: drag a box over the member to create its outline; drag inside to move it, near its edge to resize it. Turn it in 3D with the arrow gizmo beside it or the U and F dots; tap a head or dot to flip that axis.",
            "Paint: brush the Behind and In front masks of the member. Right-drag erases.",
            "Frame: step through frames and directions, choose the rig's target, mark a member as not visible here.",
            "Test: drag on the frame to wound it with one of the rig's damage types, using the game's own cut engine.",
        };

        [SerializeField] internal Tab tab;
        [SerializeField] internal int memberIndex;
        [SerializeField] internal Sprite selSprite;
        [SerializeField] internal bool selMirrored;
        [SerializeField] internal float shapeAlpha = 1f;
        [SerializeField] internal float paintAlpha = 1f;
        [SerializeField] internal int brushSize = 2;
        [SerializeField] internal int paintLayer;          // 0 = behind, 1 = in front
        [SerializeField] internal bool paintErase;
        [SerializeField] internal int recipeIndex;
        [SerializeField] internal bool flipSide;
        [SerializeField] internal float stageZoom = 1f;
        [SerializeField] internal Vector2 stagePan;

        /// Held by the Hide far side button: every far-side line and letter of the member disappears.
        internal bool hideFar;

        internal GoreRig Rig => Current;
        internal GoreStage stage;
        VisualElement leftHost, stripHost;

        // ── lifecycle ─────────────────────────────────────────────────────────────────────────────────

        protected override void OnAssetChanged()
        {
            selSprite = null;
            selMirrored = false;
            ResetWounds();
            ClearPixelCache();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            stage?.Dispose();
            stage = null;
            ClearPixelCache();
            DisposeTestTextures();
        }

        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            stage?.Dispose();
            stage = null;
            leftHost = null;
            stripHost = null;
        }

        protected override void BuildAsset(VisualElement root, GoreRig rig)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            EnsureMemberLists(rig);
            BuildFrameModel();
            ResolveShown();
            if (tab == Tab.Test) RecutShown();

            var left = new VisualElement();
            left.style.minWidth = 240f;
            left.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            left.Add(scroll);
            leftHost = scroll.contentContainer;
            BuildLeft(leftHost);

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 200f;
            right.style.minHeight = 0f;
            stripHost = new VisualElement();
            stripHost.style.flexShrink = 0f;
            right.Add(stripHost);
            BuildStrip(stripHost);
            stage = new GoreStage(this);
            right.Add(stage);

            var split = Z.Split("gorelab", 330f, left, right);
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;
            root.Add(split);
            stage.Refresh();
        }

        /// After a committed edit or a selection change: the left pane, the strip and the stage re-read the data.
        /// Deliberately not a whole-window Rebuild, which would throw the stage (and its view) away under the cursor.
        internal void AfterEdit()
        {
            if (leftHost != null) { leftHost.Clear(); BuildLeft(leftHost); }
            if (stripHost != null) { stripHost.Clear(); BuildStrip(stripHost); }
            stage?.Refresh();
        }

        // ── defaults ─────────────────────────────────────────────────────────────────────────────────

        static void SeedDefaults(GoreRig rig)
        {
            EnsureMemberLists(rig);
            if (rig.members.Count == 0) AddDefaultMembers(rig);
            rig.EnsureRecipes();
        }

        static void AddDefaultMembers(GoreRig rig) => rig.members.AddRange(GoreRig.DefaultMembers());

        /// Old or hand-made rigs may carry null lists; the window never edits through a null.
        static void EnsureMemberLists(GoreRig rig)
        {
            if (rig.members == null) rig.members = new List<GoreMemberDef>();
            if (rig.frames == null) rig.frames = new List<GoreFrameTags>();
            if (rig.recipes == null) rig.recipes = new List<IWoundRecipe>();
        }
    }
}
