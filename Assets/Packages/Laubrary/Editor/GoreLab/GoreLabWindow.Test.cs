// The Test tab: wound the shown frame with one of the rig's damage types, through the SAME engine the game uses
// (GoreRigInputs.Build + the recipe's Generate + GoreCut.CutFrame). The removers live in this window only and are never
// saved; they stay while you switch frames and directions, so one wound can be checked on every view. Reset clears them.
//
// A recipe's tunable fields are drawn by reflection (ZuiReflect, the same path Chunks' capability cards use), so a new
// damage type written by a game needs no edit here.
using System;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    public partial class GoreLabWindow
    {
        internal enum TestState { Idle, Ok, NotSetUp, Hidden }

        readonly List<GoreRemover> removers = new List<GoreRemover>();
        internal readonly List<GoreRemover> pendingWound = new List<GoreRemover>();
        readonly GoreFrameResult cutResult = new GoreFrameResult();
        readonly GoreFrameResult previewResult = new GoreFrameResult();
        int woundCounter;
        [SerializeField] int testTarget = -1;      // -1 = every tagged member the line reaches, else a member index
        bool testPlaying;
        double nextPlayStep;
        internal TestState testState;
        internal Texture2D resultTex;       // the wounded frame, shown in place of the sprite while wounds exist
        internal bool resultValid;
        internal byte[] previewMask;        // what the swipe being dragged would take off (null = nothing)

        internal int RemoverCount => removers.Count;

        internal IWoundRecipe ActiveRecipe
        {
            get
            {
                var list = Rig?.recipes;
                if (list == null || list.Count == 0) return null;
                recipeIndex = Mathf.Clamp(recipeIndex, 0, list.Count - 1);
                return list[recipeIndex];
            }
        }

        void ResetWounds()
        {
            removers.Clear();
            pendingWound.Clear();
            previewMask = null;
            woundCounter = 0;
            resultValid = false;
            testState = TestState.Idle;
        }

        void DisposeTestTextures()
        {
            if (resultTex != null) DestroyImmediate(resultTex);
            resultTex = null;
            resultValid = false;
        }

        // ── the tab ───────────────────────────────────────────────────────────────────────────────────

        void BuildTestTab(VisualElement host)
        {
            var rig = Rig;
            var list = rig.recipes;
            var add = Z.Button("Add…", "Add a damage type to this rig. Every damage type in the project is offered, including ones a game wrote.",
                () => { });
            add.clicked += () => ShowAddRecipeMenu(add);

            if (list == null || list.Count == 0)
            {
                host.Add(Z.Row(
                    Z.Text("No damage types declared", ZuiText.Subtle, "This rig offers no damage types yet, so there is nothing to test with."),
                    Z.Button("Add defaults", "Give this rig the standard damage types: Slice, Cut, Bullet, Shotgun and Remove head (undoable).",
                        () => Edit("Add damage types", () => rig.EnsureRecipes())),
                    add));
                return;
            }

            var names = new string[list.Count];
            for (int i = 0; i < list.Count; i++) names[i] = list[i] != null ? list[i].DisplayName : "(missing type)";
            var pick = Z.MiniRadio(recipeIndex, names, "The damage type a drag on the frame applies.",
                i => { recipeIndex = i; pendingWound.Clear(); AfterEdit(); }, wrap: true);
            host.Add(pick);

            var targetNames = new List<string> { "Auto" };
            for (int i = 0; i < MemberCount; i++) targetNames.Add(MemberName(i));
            host.Add(Z.Field("Target", "Which body member a swipe or shot is aimed at. Auto = every tagged member the line reaches (a slice goes to the one it crosses most).",
                Z.MiniRadio(testTarget + 1, targetNames.ToArray(), "Which body member a swipe or shot is aimed at.",
                    i => { testTarget = i - 1; pendingWound.Clear(); AfterEdit(); }, wrap: true)));

            var recipe = ActiveRecipe;
            var remove = Z.IconButton("trash", $"Remove {(recipe != null ? recipe.DisplayName : "this damage type")} from the rig (undoable).",
                () => Edit("Remove damage type", () => { rig.recipes.RemoveAt(recipeIndex); recipeIndex = Mathf.Max(0, recipeIndex - 1); }));
            var flip = Z.ToggleButton("Flip side", flipSide
                    ? "On: a slice sends the other side flying (the side WITH the neck). Click for the automatic side."
                    : "Off: a slice sends the side without the neck flying. Click to send the other side.",
                flipSide, on => { flipSide = on; AfterEdit(); });
            var reset = Z.Button("Reset", "Remove every test wound; the frames show as drawn again. Test wounds are never saved.",
                () => { ResetWounds(); RecutShown(); AfterEdit(); });
            reset.SetEnabled(removers.Count > 0);
            var play = Z.ToggleButton("Play walk", testPlaying
                    ? "On: the frames of this direction play in turn, so you can watch the wounds follow the walk. Click to stop."
                    : "Off: a still frame. Click to play this direction's frames in a loop and watch the wounds on every one.",
                testPlaying, SetTestPlaying);
            host.Add(Z.Row(flip, play, reset, add, remove));

            if (recipe == null) return;
            // No titled box: the chooser above already names the damage type these settings belong to.
            host.Add(BuildRecipeFields(recipe));
        }

        /// The recipe's public fields as controls, by reflection, so a damage type a game writes needs no edit here.
        /// ZuiReflect draws every field type except double, which the recipes use (the engine works in doubles), so
        /// doubles get the same controls ZuiReflect gives a float: a MicroSlider with [Range], a scrub field without.
        /// A [Header] becomes a labelled divider.
        VisualElement BuildRecipeFields(IWoundRecipe recipe)
        {
            var rig = Rig;
            var opt = new ZuiReflect.Options
            {
                OnBeforeChange = () => Undo.RecordObject(rig, "Edit " + recipe.DisplayName),
                OnChanged = () => EditorUtility.SetDirty(rig),
                OnStructureChanged = AfterEdit,
            };
            var flow = new VisualElement();
            flow.AddToClassList("zui-foundation-flow");
            foreach (var f in ZuiReflect.FieldsOf(recipe.GetType()))
            {
                var header = (HeaderAttribute)Attribute.GetCustomAttribute(f, typeof(HeaderAttribute));
                if (header != null) flow.Add(Z.Divider(header.header));
                var ve = f.FieldType == typeof(double) ? DoubleField(recipe, f, opt) : ZuiReflect.BuildField(recipe, f, opt);
                if (ve != null) flow.Add(ve);
            }
            return flow;
        }

        static VisualElement DoubleField(object owner, FieldInfo f, ZuiReflect.Options opt)
        {
            string label = ObjectNames.NicifyVariableName(f.Name);
            string tip = ZuiReflect.TooltipAttributeOf(f) ?? label;
            float v = (float)(double)f.GetValue(owner);
            void Set(float x)
            {
                opt.OnBeforeChange?.Invoke();
                f.SetValue(owner, (double)x);
                opt.OnChanged?.Invoke();
            }
            var range = (RangeAttribute)Attribute.GetCustomAttribute(f, typeof(RangeAttribute));
            if (range != null) return Z.MicroSlider(label, v, range.min, range.max, tip, Set, SliderW);
            return Z.Field(label, tip, Z.Float(v, tip, Set, 64f));
        }

        void ShowAddRecipeMenu(VisualElement anchor)
        {
            var rig = Rig;
            var menu = Z.Menu(anchor);
            int n = 0;
            foreach (var type in GoreRecipes.Discover())
            {
                if (type == null) continue;
                var probe = SafeCreate(type);
                if (probe == null) continue;
                n++;
                var t = type;
                menu.Item(probe.DisplayName, $"Add a {probe.DisplayName} damage type with its default settings.", () =>
                    Edit("Add damage type", () =>
                    {
                        var r = SafeCreate(t);
                        if (r == null) return;
                        rig.recipes.Add(r);
                        recipeIndex = rig.recipes.Count - 1;
                    }));
            }
            if (n == 0) menu.Item("No damage types found", "No class implementing IWoundRecipe was found in the project.", () => { }, enabled: false);
            menu.Show();
        }

        static IWoundRecipe SafeCreate(Type t)
        {
            try { return GoreRecipes.Create(t); }
            catch { return null; }
        }

        // ── the engine ────────────────────────────────────────────────────────────────────────────────

        bool TryBuildInput(out GoreFrameInput input)
        {
            input = default;
            if (shown == null || shown.tags == null || shown.pixels == null) return false;
            input = new GoreFrameInput
            {
                grid = shown.pixels.grid,
                sx = 0, sy = 0,
                members = GoreRigInputs.Build(shown.tags, shown.W, shown.H, shown.mirrored, MemberCount),
            };
            return input.members != null;
        }

        // The same spot rule the game uses for a bullet: how many frames of this direction would show a hole at this remover.
        double ShownDirectionHoleVisibility(GoreRemover op)
        {
            if (shown == null) return 1;
            var grids = new List<GoreGrid>();
            var mems = new List<GoreMemberInput[]>();
            foreach (var s in shown.group.sprites)
            {
                var px = Pixels(s, shown.mirrored);
                var tags = FindFrame(s);
                if (px == null || tags == null) continue;
                grids.Add(px.grid);
                mems.Add(GoreRigInputs.Build(tags, px.W, px.H, shown.mirrored, MemberCount));
            }
            return GoreHoleVisibility.Score(op, grids, mems);
        }

        void SetTestPlaying(bool on)
        {
            testPlaying = on;
            EditorApplication.update -= TickTestPlay;
            if (on) { nextPlayStep = EditorApplication.timeSinceStartup + 0.16; EditorApplication.update += TickTestPlay; }
            AfterEdit();
        }

        // A light step: the panes are not rebuilt six times a second, only the picture changes.
        void TickTestPlay()
        {
            if (this == null || !testPlaying || tab != Tab.Test || shown == null) { testPlaying = false; EditorApplication.update -= TickTestPlay; return; }
            if (EditorApplication.timeSinceStartup < nextPlayStep) return;
            nextPlayStep += 0.16;
            int n = shown.group.sprites.Count;
            selSprite = shown.group.sprites[(shown.index + 1) % n];
            ResolveShown();
            pendingWound.Clear();
            RecutShown();
            stage?.Refresh();
        }

        int NextGroup()
        {
            int g = -1;
            foreach (var r in removers) if (r.group > g) g = r.group;
            return g + 1;
        }

        /// Turn a swipe (sprite-local pixels of the shown frame) into removers with the active recipe, into `into`.
        bool Generate(Vector2 p0, Vector2 p1, in GoreFrameInput input, List<GoreRemover> into)
        {
            into.Clear();
            var recipe = ActiveRecipe;
            if (recipe == null || (p1 - p0).magnitude < 1f) return false;
            var targets = new bool[input.members.Length];
            for (int i = 0; i < targets.Length; i++) targets[i] = testTarget < 0 || i == testTarget;
            var ctx = new WoundContext
            {
                sliceable = Rig.SliceableFlags(),
                p0x = p0.x, p0y = p0.y, p1x = p1.x, p1y = p1.y,
                group = NextGroup(),
                seed = Rig.cut.seed * 131 + woundCounter,
                shotCounter = woundCounter,
                holeVisibility = ShownDirectionHoleVisibility,
                grid = input.grid,
                members = input.members,
                targets = targets,
                cut = Rig.cut,
                flipSide = flipSide,
            };
            ctx.existing.AddRange(removers);
            try { recipe.Generate(ctx, into); }
            catch (Exception e) { Debug.LogException(e); into.Clear(); }
            return into.Count > 0;
        }

        /// The live preview of a drag: what the swipe would take off, computed exactly as the release will.
        internal void PreviewWound(Vector2 p0, Vector2 p1)
        {
            previewMask = null;
            if (!TryBuildInput(out var input) || !Generate(p0, p1, input, pendingWound)) return;
            var all = new List<GoreRemover>(removers);
            all.AddRange(pendingWound);
            GoreCut.CutFrame(input, all, Rig.cut, Rig.style, previewResult);
            if (!previewResult.missing && previewResult.chunkCount > 0) previewMask = previewResult.chunkMask;
        }

        /// Release: the swipe becomes part of this body's wounds, and the shown frame is re-cut.
        internal void FireWound(Vector2 p0, Vector2 p1)
        {
            previewMask = null;
            if (!TryBuildInput(out var input)) { testState = TestState.NotSetUp; return; }
            if (!Generate(p0, p1, input, pendingWound)) return;
            removers.AddRange(pendingWound);
            pendingWound.Clear();
            woundCounter++;
            RecutShown();
        }

        /// Cut the shown frame with every test wound (the frame as the game would show it).
        internal void RecutShown()
        {
            resultValid = false;
            if (removers.Count == 0) { testState = TestState.Idle; return; }
            if (!TryBuildInput(out var input)) { testState = TestState.NotSetUp; return; }
            GoreCut.CutFrame(input, removers, Rig.cut, Rig.style, cutResult);
            if (cutResult.missing) { testState = TestState.NotSetUp; return; }
            if (resultTex == null) resultTex = GoreSpritePixels.NewTexture(shown.W, shown.H);
            GoreSpritePixels.Write(resultTex, cutResult.body, shown.W, shown.H);
            resultValid = true;
            testState = cutResult.changed == 0 ? TestState.Hidden : TestState.Ok;
        }
    }
}
