using UnityEditor;
using UnityEngine;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Larder.Editor
{
    /// Larder: tune one Ware and watch it repaint live, roll endless random variations, and bake a shelf's worth of
    /// sprites. Left column = every WareSpec dial as ZUI controls; right column = a live pixel preview plus a row of
    /// all damage stages — all drawn by the exact WareGenerator the game uses, so the preview IS the product. Textures
    /// are rebuilt only when the spec changes and disposed on the way out so the editor never leaks them.
    /// Asset browse + CRUD (New/Duplicate/Rename/Delete + empty-state library) come from LaubraryAssetWindow.
    public class LarderWindow : LaubraryAssetWindow<WareSpec>
    {
        [MenuItem("Laubrary/Larder")]
        public static void Open() => GetWindow<LarderWindow>("Larder");

        WareSpec spec => Current;         // the base owns the current asset; alias for the dial code below

        int gridCount = 6;
        Vector2 leftScroll;

        // preview textures, owned by this window
        Texture2D intactTex;
        Texture2D[] stageTex;
        bool dirty = true;

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

        protected override void OnZUIEnable() => dirty = true;
        protected override void OnAssetChanged() => dirty = true;
        protected override void OnDisable() { base.OnDisable(); DisposeTextures(); }

        // Browser thumbnails: the intact ware, rendered by the real generator.
        protected override Texture2D RenderThumbnail(WareSpec item) => MakeTex(item, 0);

        protected override void DrawAsset(WareSpec asset)
        {
            EditorGUILayout.BeginHorizontal();
            DrawDials(GUILayout.Width(320));
            DrawPreview();
            EditorGUILayout.EndHorizontal();

            if (dirty || intactTex == null) RebuildPreview();
        }

        // A plain horizontal row (ZUI has no object-field wrapper, so mixed slots use EditorGUILayout).
        static System.IDisposable ZUINullSafeRow()
        {
            EditorGUILayout.BeginHorizontal();
            return new EndHorizontal();
        }

        class EndHorizontal : System.IDisposable { public void Dispose() => EditorGUILayout.EndHorizontal(); }

        // ── left: dials ──────────────────────────────────────────────────────────────────────────────
        void DrawDials(params GUILayoutOption[] opt)
        {
            EditorGUILayout.BeginVertical(opt);
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            EditorGUI.BeginChangeCheck();

            Section("Identity");
            using (ZUINullSafeRow())
            {
                EditorGUILayout.LabelField($"Seed {spec.seed}", GUILayout.Width(150));
                if (GUILayout.Button("Randomize seed")) spec.seed = Random.Range(int.MinValue, int.MaxValue);
            }
            spec.kind = (WareKind)MiniRadio((int)spec.kind, KindLabels);
            spec.shape = (WareShape)MiniRadio((int)spec.shape, ShapeLabels);

            Section("Silhouette");
            spec.widthRatio = Slider(spec.widthRatio, 0.2f, 1f, "Width");
            spec.heightRatio = Slider(spec.heightRatio, 0.2f, 1f, "Height");

            Section("Body");
            spec.fill = (FillMode)MiniRadio((int)spec.fill, FillLabels);
            spec.useCustomColors = Toggle(spec.useCustomColors, "Custom colours");
            if (spec.useCustomColors)
            {
                spec.customBody = EditorGUILayout.ColorField("Body", spec.customBody);
                spec.customBodyDark = EditorGUILayout.ColorField("Body dark", spec.customBodyDark);
                spec.customBodyLight = EditorGUILayout.ColorField("Body light", spec.customBodyLight);
                spec.customAccent = EditorGUILayout.ColorField("Accent", spec.customAccent);
                spec.customLabel = EditorGUILayout.ColorField("Label", spec.customLabel);
                spec.customInk = EditorGUILayout.ColorField("Ink", spec.customInk);
            }
            else
            {
                spec.paletteIndex = Mathf.RoundToInt(Slider(spec.paletteIndex, 0, WarePalettes.Count - 1, "Palette"));
            }

            Section("Decoration");
            spec.label = (LabelStyle)MiniRadio((int)spec.label, LabelLabels);
            spec.labelWidth = Slider(spec.labelWidth, 0.2f, 1f, "Label width");
            spec.corner = (CornerStyle)MiniRadio((int)spec.corner, CornerLabels);
            spec.bands = (BandMode)MiniRadio((int)spec.bands, BandLabels);
            spec.bandCount = Mathf.RoundToInt(Slider(spec.bandCount, 1, 6, "Band count"));
            spec.spots = (SpotMode)MiniRadio((int)spec.spots, SpotLabels);
            spec.hasLid = Toggle(spec.hasLid, "Lid strip");

            Section("Output");
            spec.resolution = Mathf.RoundToInt(Slider(spec.resolution, 24, 64, "Resolution"));
            spec.damageStages = Mathf.RoundToInt(Slider(spec.damageStages, 2, 4, "Damage stages"));
            spec.pixelsPerUnit = Slider(spec.pixelsPerUnit, 8f, 128f, "Pixels/unit");

            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(spec); dirty = true; }

            VerticalSpace();
            Section("Actions");
            if (Button("Randomize whole Ware"))
            {
                Undo.RecordObject(spec, "Randomize Ware");
                spec.Randomize(new System.Random(Random.Range(int.MinValue, int.MaxValue)));
                EditorUtility.SetDirty(spec); dirty = true;
            }
            if (Button("Bake this Ware")) WareBaker.Bake(spec);
            using (ZUINullSafeRow())
            {
                if (GUILayout.Button("Bake variation grid")) WareBaker.BakeVariationGrid(spec, gridCount);
                gridCount = Mathf.Clamp(EditorGUILayout.IntField(gridCount, GUILayout.Width(44)), 1, 64);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void Section(string t) { VerticalSpace(); Label(t, ZUI.ZTextStyle.SectionHeader); }

        // ── right: preview ───────────────────────────────────────────────────────────────────────────
        void DrawPreview()
        {
            EditorGUILayout.BeginVertical();

            Rect stage = GUILayoutUtility.GetRect(220, 260, GUILayout.ExpandWidth(true), GUILayout.Height(260));
            EditorGUI.DrawRect(stage, new Color(0.10f, 0.10f, 0.12f));
            if (intactTex != null)
            {
                var pad = new Rect(stage.x + 12, stage.y + 12, stage.width - 24, stage.height - 24);
                GUI.DrawTexture(pad, intactTex, ScaleMode.ScaleToFit, true);
            }

            Label("Damage stages", ZUI.ZTextStyle.Subtle);
            Rect strip = GUILayoutUtility.GetRect(220, 96, GUILayout.ExpandWidth(true), GUILayout.Height(96));
            EditorGUI.DrawRect(strip, new Color(0.08f, 0.08f, 0.10f));
            if (stageTex != null && stageTex.Length > 0)
            {
                float cellW = strip.width / stageTex.Length;
                for (int i = 0; i < stageTex.Length; i++)
                {
                    if (stageTex[i] == null) continue;
                    var cell = new Rect(strip.x + i * cellW + 6, strip.y + 6, cellW - 12, strip.height - 20);
                    GUI.DrawTexture(cell, stageTex[i], ScaleMode.ScaleToFit, true);
                    GUI.Label(new Rect(strip.x + i * cellW, strip.yMax - 16, cellW, 16),
                        i == 0 ? "intact" : "dmg " + i, EditorStyles.centeredGreyMiniLabel);
                }
            }

            if (spec != null)
                Label($"{spec.kind} · seed {spec.seed} · {Mathf.RoundToInt(spec.resolution * spec.widthRatio)}×" +
                      $"{Mathf.RoundToInt(spec.resolution * spec.heightRatio)} px", ZUI.ZTextStyle.Small);

            EditorGUILayout.EndVertical();
        }

        // ── preview texture lifecycle ─────────────────────────────────────────────────────────────────
        void RebuildPreview()
        {
            DisposeTextures();
            if (spec == null) { dirty = false; return; }

            intactTex = MakeTex(spec, 0);
            int stages = Mathf.Clamp(spec.damageStages, 1, 4);
            stageTex = new Texture2D[stages];
            for (int s = 0; s < stages; s++) stageTex[s] = MakeTex(spec, s);
            dirty = false;
            Repaint();
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
