using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Larder.Editor
{
    /// Larder: tune one Ware and watch it repaint live, roll endless random variations, and bake a shelf's worth of
    /// sprites. Left column = every WareSpec dial as ZUI controls; right column = a live pixel preview plus a row of
    /// all damage stages — all drawn by the exact WareGenerator the game uses, so the preview IS the product. Textures
    /// are rebuilt only when the spec changes and disposed on the way out so the editor never leaks them.
    /// Asset browse + CRUD (New/Duplicate/Rename/Delete + empty-state library) come from ZuiAssetWindow.
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration): fully native — unlike Pyre, the preview needs no IMGUI island,
    /// because it is only texture blits, which `Image` (ScaleToFit) does directly. Dials are Z.* controls, and every
    /// one now records Undo (the IMGUI original only did so for "Randomize whole Ware").
    public class LarderWindow : ZuiAssetWindow<WareSpec>
    {
        [MenuItem("Laubrary/Larder")]
        public static void Open() => GetWindow<LarderWindow>("Larder");

        /// Same entry-point shape as PyreWindow.OpenFor/MirageWindow.OpenFor — lets a reference chip's "Open in its editor" card item
        /// button jump straight into this WareSpec's own editor.
        public static void OpenFor(WareSpec ware)
        {
            var w = GetWindow<LarderWindow>("Larder");
            if (ware != null) w.SetAsset(ware);
        }

        WareSpec spec => Current;         // the base owns the current asset; alias for the dial code below

        int gridCount = 6;

        // preview textures, owned by this window
        Texture2D intactTex;
        Texture2D[] stageTex;

        // live elements refreshed when the spec changes
        VisualElement dialHost, stageStripHost;
        Image stageImage;
        Label captionLabel;

        static readonly string[] KindLabels = { "Book", "Can", "Box", "Crate", "Carton" };
        static readonly string[] ShapeLabels = { "Rect", "Rounded", "Round", "Sphere" };
        static readonly string[] FillLabels = { "Solid", "Gradient", "Glow", "Shadow" };
        static readonly string[] LabelLabels = { "None", "Horiz", "Diag", "Patch" };
        static readonly string[] CornerLabels = { "None", "Triangle", "Rounded", "Cut" };
        static readonly string[] BandLabels = { "None", "Horiz", "Vert", "Diag" };
        static readonly string[] SpotLabels = { "None", "Circle", "Spots" };

        protected override string TypeLabel => "Ware";
        protected override string NewAssetName => "Ware";
        protected override string DefaultFolder => "Assets/Larder";

        protected override void OnAssetChanged() => RebuildPreview();
        protected override void OnDisable() { base.OnDisable(); DisposeTextures(); }

        // Browser thumbnails: the intact ware, rendered by the real generator.
        protected override Texture2D RenderThumbnail(WareSpec item) => MakeTex(item, 0);

        // ── mutation helpers (the Undo contract every dial routes through) ───────────────────
        void Dial(string undoLabel, System.Action apply)
        {
            Undo.RecordObject(spec, undoLabel);
            apply();
            EditorUtility.SetDirty(spec);
            RebuildPreview();
        }

        /// Structural change (a mode switch that shows/hides dials) → rebuild the dial column too.
        void DialAndRebuild(string undoLabel, System.Action apply)
        {
            Dial(undoLabel, apply);
            if (dialHost == null) return;
            dialHost.Clear();
            BuildDials(dialHost);
        }

        protected override void OnBeforeRebuild()
        {
            dialHost = null; stageStripHost = null; stageImage = null; captionLabel = null;
        }

        protected override void BuildAsset(VisualElement root, WareSpec asset)
        {
            root.style.flexGrow = 1f;

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;
            root.Add(split);

            var left = new VisualElement();
            left.style.width = 320f;
            left.style.flexShrink = 0f;
            left.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            dialHost = scroll.contentContainer;
            BuildDials(dialHost);
            left.Add(scroll);
            split.Add(left);

            split.Add(BuildPreview());
            RebuildPreview();
        }

        // ── left: dials ──────────────────────────────────────────────────────────────────────
        void BuildDials(VisualElement root)
        {
            // Each block is a collapsible Z.Section — its header is the toggle, so a long dial column
            // can be folded down to whatever is being worked on.
            var identity = Z.Section("Identity", "What this ware is and the seed every random detail derives from.");
            identity.Add(Z.Row(
                Z.Text($"Seed {spec.seed}", ZuiText.Body, "The seed every random detail of this ware derives from."),
                Z.Button("Randomize seed", "Roll a new seed, keeping every other dial as-is.",
                    () => DialAndRebuild("Randomize seed", () => spec.seed = Random.Range(int.MinValue, int.MaxValue)))));
            identity.Add(Z.MiniRadio((int)spec.kind, KindLabels, "The kind of product — decides its proportions and default decoration.",
                v => DialAndRebuild("Ware kind", () => spec.kind = (WareKind)v)));
            identity.Add(Z.MiniRadio((int)spec.shape, ShapeLabels, "The silhouette's basic shape.",
                v => Dial("Ware shape", () => spec.shape = (WareShape)v)));
            root.Add(identity);

            var silhouette = Z.Section("Silhouette", "The ware's proportions within its canvas.");
            silhouette.Add(Z.Field("Width", "Ware width as a fraction of the canvas.",
                Z.Slider(spec.widthRatio, 0.2f, 1f, "Ware width as a fraction of the canvas.",
                    v => Dial("Width", () => spec.widthRatio = v), 150f)));
            silhouette.Add(Z.Field("Height", "Ware height as a fraction of the canvas.",
                Z.Slider(spec.heightRatio, 0.2f, 1f, "Ware height as a fraction of the canvas.",
                    v => Dial("Height", () => spec.heightRatio = v), 150f)));
            root.Add(silhouette);

            var body = Z.Section("Body", "How the ware's body is filled and coloured.");
            body.Add(Z.MiniRadio((int)spec.fill, FillLabels, "How the body is shaded: flat, a gradient, a glow, or a drop shadow.",
                v => Dial("Fill mode", () => spec.fill = (FillMode)v)));
            body.Add(Z.Toggle("Custom colours", "Pick every colour by hand instead of using one of the built-in palettes.",
                spec.useCustomColors, v => DialAndRebuild("Custom colours", () => spec.useCustomColors = v)));
            if (spec.useCustomColors)
            {
                body.Add(ColorRow("Body", "The body's main colour.", () => spec.customBody, c => spec.customBody = c));
                body.Add(ColorRow("Body dark", "Shaded side of the body.", () => spec.customBodyDark, c => spec.customBodyDark = c));
                body.Add(ColorRow("Body light", "Lit side of the body.", () => spec.customBodyLight, c => spec.customBodyLight = c));
                body.Add(ColorRow("Accent", "Trim and edge details.", () => spec.customAccent, c => spec.customAccent = c));
                body.Add(ColorRow("Label", "The label patch's colour.", () => spec.customLabel, c => spec.customLabel = c));
                body.Add(ColorRow("Ink", "Text/marking colour on the label.", () => spec.customInk, c => spec.customInk = c));
            }
            else
            {
                body.Add(Z.Field("Palette", "Which built-in colour palette this ware uses.",
                    Z.SliderInt(spec.paletteIndex, 0, WarePalettes.Count - 1, "Which built-in colour palette this ware uses.",
                        v => Dial("Palette", () => spec.paletteIndex = v), 150f)));
            }
            root.Add(body);

            var deco = Z.Section("Decoration", "Labels, corners, bands and spots painted onto the body.");
            deco.Add(Z.MiniRadio((int)spec.label, LabelLabels, "The label patch's placement.",
                v => Dial("Label style", () => spec.label = (LabelStyle)v)));
            deco.Add(Z.Field("Label width", "How wide the label patch is across the body.",
                Z.Slider(spec.labelWidth, 0.2f, 1f, "How wide the label patch is across the body.",
                    v => Dial("Label width", () => spec.labelWidth = v), 150f)));
            deco.Add(Z.MiniRadio((int)spec.corner, CornerLabels, "How the ware's corners are cut or rounded.",
                v => Dial("Corner style", () => spec.corner = (CornerStyle)v)));
            deco.Add(Z.MiniRadio((int)spec.bands, BandLabels, "Direction of the decorative bands, if any.",
                v => Dial("Band mode", () => spec.bands = (BandMode)v)));
            deco.Add(Z.Field("Band count", "How many decorative bands are drawn.",
                Z.SliderInt(spec.bandCount, 1, 6, "How many decorative bands are drawn.",
                    v => Dial("Band count", () => spec.bandCount = v), 150f)));
            deco.Add(Z.MiniRadio((int)spec.spots, SpotLabels, "Spot decoration: none, one circle, or scattered spots.",
                v => Dial("Spot mode", () => spec.spots = (SpotMode)v)));
            deco.Add(Z.Toggle("Lid strip", "Draw a lid/cap strip across the top.", spec.hasLid,
                v => Dial("Lid strip", () => spec.hasLid = v)));
            root.Add(deco);

            var output = Z.Section("Output", "Canvas size and what gets baked.");
            output.Add(Z.Field("Resolution", "Canvas size in pixels for the baked sprite.",
                Z.SliderInt(spec.resolution, 24, 64, "Canvas size in pixels for the baked sprite.",
                    v => Dial("Resolution", () => spec.resolution = v), 150f)));
            output.Add(Z.Field("Damage stages", "How many progressively-damaged versions get baked.",
                Z.SliderInt(spec.damageStages, 2, 4, "How many progressively-damaged versions get baked.",
                    v => Dial("Damage stages", () => spec.damageStages = v), 150f)));
            output.Add(Z.Field("Pixels/unit", "Pixels-per-unit stamped onto the baked sprite.",
                Z.Slider(spec.pixelsPerUnit, 8f, 128f, "Pixels-per-unit stamped onto the baked sprite.",
                    v => Dial("Pixels per unit", () => spec.pixelsPerUnit = v), 150f)));
            root.Add(output);

            var actions = Z.Section("Actions", "Randomize this ware, or bake it to sprite assets.");
            actions.Add(Z.Button("Randomize whole Ware", "Roll every dial at once (undoable).", () =>
                DialAndRebuild("Randomize Ware",
                    () => spec.Randomize(new System.Random(Random.Range(int.MinValue, int.MaxValue))))));
            actions.Add(Z.Button("Bake this Ware", "Bake this ware's sprite + damage stages into the project.",
                () => WareBaker.Bake(spec)));
            actions.Add(Z.Row(
                Z.Button("Bake variation grid", "Bake a grid of random variations of this ware.",
                    () => WareBaker.BakeVariationGrid(spec, gridCount)),
                Z.Int(gridCount, "How many variations the grid bake produces.",
                    v => gridCount = Mathf.Clamp(v, 1, 64), 44f)));
            root.Add(actions);
        }

        VisualElement ColorRow(string label, string tooltip, System.Func<Color> get, System.Action<Color> set)
            => Z.Field(label, tooltip, Z.Color(get(), tooltip, c => Dial(label, () => set(c)), 110f));

        // ── right: preview (native — Image does the ScaleToFit blit IMGUI was used for) ──────
        VisualElement BuildPreview()
        {
            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 0f;
            right.style.marginLeft = 4f;

            var stage = new VisualElement();
            stage.style.height = 260f;
            stage.style.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
            stage.style.paddingLeft = stage.style.paddingRight = 12f;
            stage.style.paddingTop = stage.style.paddingBottom = 12f;
            stage.tooltip = "The live ware, drawn by the same WareGenerator the game uses.";
            stageImage = new Image { scaleMode = ScaleMode.ScaleToFit };
            stageImage.style.flexGrow = 1f;
            stage.Add(stageImage);
            right.Add(stage);

            right.Add(Z.Text("Damage stages", ZuiText.Subtle,
                "Every progressively-damaged version this ware bakes, left (intact) to right."));
            stageStripHost = new VisualElement();
            stageStripHost.style.flexDirection = FlexDirection.Row;
            stageStripHost.style.height = 96f;
            stageStripHost.style.backgroundColor = new Color(0.08f, 0.08f, 0.10f);
            right.Add(stageStripHost);

            captionLabel = Z.Text("", ZuiText.Small, "This ware's kind, seed and baked pixel size.");
            right.Add(captionLabel);
            return right;
        }

        // ── preview texture lifecycle ────────────────────────────────────────────────────────
        void RebuildPreview()
        {
            DisposeTextures();
            if (spec == null || stageImage == null) return;

            intactTex = MakeTex(spec, 0);
            stageImage.image = intactTex;

            int stages = Mathf.Clamp(spec.damageStages, 1, 4);
            stageTex = new Texture2D[stages];
            stageStripHost.Clear();
            for (int s = 0; s < stages; s++)
            {
                stageTex[s] = MakeTex(spec, s);
                var cell = new VisualElement();
                cell.style.flexGrow = 1f;
                cell.style.flexBasis = 0f;   // equal shares regardless of texture size
                cell.style.paddingTop = cell.style.paddingLeft = cell.style.paddingRight = 6f;
                cell.tooltip = s == 0 ? "The intact ware." : $"Damage stage {s}.";
                var img = new Image { image = stageTex[s], scaleMode = ScaleMode.ScaleToFit };
                img.style.flexGrow = 1f;
                cell.Add(img);
                var cap = Z.Text(s == 0 ? "intact" : "dmg " + s, ZuiText.Small,
                    s == 0 ? "The intact ware." : $"Damage stage {s}.");
                cap.style.unityTextAlign = TextAnchor.MiddleCenter;
                cell.Add(cap);
                stageStripHost.Add(cell);
            }

            captionLabel.text = $"{spec.kind} · seed {spec.seed} · " +
                $"{Mathf.RoundToInt(spec.resolution * spec.widthRatio)}×{Mathf.RoundToInt(spec.resolution * spec.heightRatio)} px";
        }

        static Texture2D MakeTex(WareSpec spec, int stage)
        {
            var px = WareGenerator.Render(spec, stage, out int w, out int h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        void DisposeTextures()
        {
            if (stageImage != null) stageImage.image = null;
            if (intactTex != null) { Object.DestroyImmediate(intactTex); intactTex = null; }
            if (stageTex != null)
            {
                for (int i = 0; i < stageTex.Length; i++)
                    if (stageTex[i] != null) Object.DestroyImmediate(stageTex[i]);
                stageTex = null;
            }
        }
    }
}
