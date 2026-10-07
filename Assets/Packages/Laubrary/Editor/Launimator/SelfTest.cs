using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Launimator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Headless verification of Track A's core asset-generation path. Synthesises an
    /// N-frame strip of distinct coloured shapes (self-contained — no external art),
    /// slices it via the regular-grid slicer, then runs the exact "Save → Lauminary"
    /// path and asserts the Lauminary, clip, prefab, and AnimatorController exist and the
    /// clip carries the expected sprite keyframes.
    ///
    /// Run headlessly:
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod Laubrary.Launimator.Editor.SelfTest.RunBatch
    /// </summary>
    public static class SelfTest
    {
        private const int FrameCount = 5;
        private const int Cell = 16;
        private const string TestFolder = "Assets/Launimator/_SelfTest";
        private const string CharName = "SelfTestHero";

        /// <summary>Batch entry point: runs the test, logs PASS/FAIL, sets exit code.</summary>
        public static void RunBatch()
        {
            int exitCode;
            try
            {
                bool ok = BuildSampleLauminary(out string report);
                Debug.Log(report);
                if (ok)
                {
                    Debug.Log("ASSETSCAVENGE SELFTEST: PASS");
                    exitCode = 0;
                }
                else
                {
                    Debug.LogError("ASSETSCAVENGE SELFTEST: FAIL");
                    exitCode = 1;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("ASSETSCAVENGE SELFTEST: FAIL (exception) " + e);
                exitCode = 1;
            }

            // Flush logs then quit with a code the batch caller can read.
            EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// Builds a sample lauminary end-to-end and asserts the results. Returns true on
        /// PASS; fills <paramref name="report"/> with a human-readable breakdown.
        /// </summary>
        public static bool BuildSampleLauminary(out string report)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("── Launimator SelfTest ──");

            // Clean any prior run so the test is deterministic.
            if (AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.DeleteAsset(TestFolder);
            foreach (var prior in LauminaryRepo.EnumerateLauminaries().Where(c => c.lauminaryName == CharName).ToList())
                LauminaryRepo.Delete(prior);
            AssetDatabase.Refresh();

            LauminaryBuilder.EnsureFolder(TestFolder);

            // 1) Synthesise a horizontal strip of FrameCount distinct coloured shapes.
            string sheetPath = $"{TestFolder}/_synthetic_strip.png";
            WriteSyntheticStrip(sheetPath);
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);
            log.AppendLine($"Wrote + imported synthetic {FrameCount}-frame strip at {sheetPath}.");

            // 2) Slice it with the regular-grid slicer.
            var spec = new GridSlicer.GridSpec
            {
                cellWidth = Cell,
                cellHeight = Cell,
                padding = 0,
                pivot = GridSlicer.PivotMode.Center,
                pixelsPerUnit = Cell
            };
            var sprites = GridSlicer.Slice(sheetPath, spec);
            log.AppendLine($"Sliced into {sprites.Count} sprite(s).");
            if (sprites.Count != FrameCount)
                return Fail(log, out report, $"Expected {FrameCount} sliced sprites, got {sprites.Count}.");

            // 3) Create a lauminary and save a 'Walk' animation (recipe of source rects) into its draft.
            var lauminary = LauminaryRepo.CreateLauminary(CharName, Cell);
            string sheetGuid = AssetDatabase.AssetPathToGUID(sheetPath);
            var recipe = sprites.Select(s => new FrameRef
            {
                sourceTextureGuid = sheetGuid,
                cell = s.rect,
                pivot = new UnityEngine.Vector2(0.5f, 0.5f)
            }).ToList();
            var def = new Laumination { name = "Walk", fps = 12f, recipe = recipe, sourceTextureGuid = sheetGuid };
            LauminaryRepo.SaveAnimationToDraft(lauminary, def);
            string safeChar = LauminaryBuilder.Sanitize(CharName);

            // 4) Assert the DRAFT generated assets exist and are wired.
            string draftFolder = LauminaryRepo.DraftFolder(lauminary);
            if (!AssertVersionAssets(draftFolder, safeChar, "Walk", log, out report, out var draftClip)) return false;
            log.AppendLine("Draft built Walk clip + prefab + controller, looping, wired. ✓");

            // 5) Commit a new immutable version; assert ITS assets are independent of the draft.
            int v = LauminaryRepo.CommitNewVersion(lauminary);
            if (v != 1) return Fail(log, out report, $"Expected first commit to be v1, got v{v}.");
            if (lauminary.latestVersion != 1) return Fail(log, out report, "latestVersion not bumped to 1.");
            string vFolder = LauminaryRepo.VersionFolder(lauminary, 1);
            if (!AssertVersionAssets(vFolder, safeChar, "Walk", log, out report, out var vClip)) return false;
            if (vClip == draftClip) return Fail(log, out report, "v1 clip is the SAME asset as the draft clip (snapshot not independent).");
            log.AppendLine("Committed v1 with its own independent clip + prefab + controller. ✓");

            // 6) Reload v1's version asset; assert its animation references the v1 clip + all frames.
            var vVersion = LauminaryRepo.LoadVersion(lauminary, 1);
            if (vVersion == null || vVersion.animations.Count != 1)
                return Fail(log, out report, "v1 version.asset missing or wrong animation count.");
            if (vVersion.animations[0].clip != vClip)
                return Fail(log, out report, "v1 animation clip ref does not point at the v1 clip.");
            if (vVersion.animations[0].frames.Count != FrameCount)
                return Fail(log, out report, "v1 animation frame count mismatch.");
            log.AppendLine("v1 version.asset references its own clip + all frames. ✓");

            report = log.ToString();
            return true;
        }

        private static bool Fail(System.Text.StringBuilder log, out string report, string why)
        {
            log.AppendLine("ASSERT FAILED: " + why);
            report = log.ToString();
            return false;
        }

        /// <summary>Assert a version folder's generated assets exist and are correctly wired. Returns the clip.</summary>
        private static bool AssertVersionAssets(string folder, string safeChar, string clipName,
            System.Text.StringBuilder log, out string report, out AnimationClip clip)
        {
            report = null;
            string clipPath = $"{folder}/Clips/{clipName}.anim";
            string prefabPath = $"{folder}/{safeChar}.prefab";
            string controllerPath = $"{folder}/{safeChar}.controller";

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (clip == null) return Fail(log, out report, $"AnimationClip missing at {clipPath}.");
            if (prefab == null) return Fail(log, out report, $"Prefab missing at {prefabPath}.");
            if (controller == null) return Fail(log, out report, $"AnimatorController missing at {controllerPath}.");

            if (Math.Abs(clip.frameRate - 12f) > 0.01f)
                return Fail(log, out report, $"Clip frame rate expected 12, got {clip.frameRate}.");

            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (bindings.Length != 1)
                return Fail(log, out report, $"Expected 1 object-reference binding, got {bindings.Length}.");
            var binding = bindings[0];
            if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite")
                return Fail(log, out report, $"Binding is {binding.type}.{binding.propertyName}, expected SpriteRenderer.m_Sprite.");
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (keys.Length != FrameCount)
                return Fail(log, out report, $"Expected {FrameCount} keyframes, got {keys.Length}.");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (!settings.loopTime) return Fail(log, out report, "Clip loopTime is false; expected looping.");

            var sm = controller.layers[0].stateMachine;
            if (sm.states.Length != 1) return Fail(log, out report, $"Controller expected 1 state, got {sm.states.Length}.");
            if (sm.states[0].state.motion != clip) return Fail(log, out report, "Controller state motion is not the clip.");

            var sr = prefab.GetComponent<SpriteRenderer>();
            var animator = prefab.GetComponent<Animator>();
            if (sr == null || animator == null) return Fail(log, out report, "Prefab missing SpriteRenderer or Animator.");
            if (animator.runtimeAnimatorController != controller) return Fail(log, out report, "Prefab Animator not wired to controller.");
            if (sr.sprite == null) return Fail(log, out report, "Prefab SpriteRenderer has no resting sprite.");
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Track B — Region Slicer headless test
        // ════════════════════════════════════════════════════════════════════

        private const string RegionTestFolder = "Assets/Launimator/_SelfTestRegion";

        /// <summary>Entry point for the Region Slicer headless test (run via execute_script).</summary>
        public static void RunRegionSliceTest()
        {
            try
            {
                bool ok = RegionSliceCore(out string report);
                Debug.Log(report);
                Debug.Log(ok
                    ? "ASSETSCAVENGE REGIONSLICE SELFTEST: PASS"
                    : "ASSETSCAVENGE REGIONSLICE SELFTEST: FAIL");
            }
            catch (Exception e)
            {
                Debug.LogError("ASSETSCAVENGE REGIONSLICE SELFTEST: FAIL (exception) " + e);
            }
        }

        /// <summary>
        /// Headless test of the UI-FREE Region Slicer core. Synthesises a small texture,
        /// builds TWO RegionSpecs with DIFFERENT cols/rows, Applies their UNION, reloads from
        /// disk, and asserts the importer ends with exactly (region1 + region2) sprites and a
        /// spot-checked rect matches expected px.
        /// </summary>
        public static bool RegionSliceCore(out string report)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("── Launimator Region-Slice SelfTest ──");

            if (AssetDatabase.IsValidFolder(RegionTestFolder))
                AssetDatabase.DeleteAsset(RegionTestFolder);
            AssetDatabase.Refresh();
            LauminaryBuilder.EnsureFolder(RegionTestFolder);

            // Synthesise a 64×64 sheet (content is irrelevant to rect geometry).
            int texW = 64, texH = 64;
            string sheetPath = $"{RegionTestFolder}/_region_sheet.png";
            WriteSolidSheet(sheetPath, texW, texH);
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);
            log.AppendLine($"Wrote + imported {texW}×{texH} synthetic sheet at {sheetPath}.");

            // Region 1: a 30×20 box anchored at (4,40) bottom-left origin, 3 cols × 2 rows.
            var spec1 = new RegionSlicer.RegionSpec
            {
                boxX = 4, boxY = 40, boxW = 30, boxH = 20,
                mode = RegionSlicer.GridMode.FixedColsRows, cols = 3, rows = 2, spacingPx = 0, paddingPx = 0
            };
            // Region 2: a 40×8 box anchored at (0,0), 5 cols × 1 row (DIFFERENT grid).
            var spec2 = new RegionSlicer.RegionSpec
            {
                boxX = 0, boxY = 0, boxW = 40, boxH = 8,
                mode = RegionSlicer.GridMode.FixedColsRows, cols = 5, rows = 1, spacingPx = 0, paddingPx = 0
            };

            var cells1 = RegionSlicer.ExpandRegion(spec1);
            var cells2 = RegionSlicer.ExpandRegion(spec2);
            int expected = cells1.Count + cells2.Count; // 6 + 5 = 11
            log.AppendLine($"Region1 → {cells1.Count} cells (3×2); Region2 → {cells2.Count} cells (5×1); union = {expected}.");
            if (cells1.Count != 6) return Fail(log, out report, $"Region1 expected 6 cells, got {cells1.Count}.");
            if (cells2.Count != 5) return Fail(log, out report, $"Region2 expected 5 cells, got {cells2.Count}.");

            // Spot-check cell geometry. Region1 cellW = 30/3 = 10, cellH = 20/2 = 10.
            // Cells emit top-row first; top row bottom edge = boxY + boxH - cellH = 40+20-10 = 50.
            // First cell (top-left) must be (4, 50, 10, 10).
            Rect c0 = cells1[0];
            if (!RectApprox(c0, new Rect(4, 50, 10, 10)))
                return Fail(log, out report, $"Region1 cell0 expected (4,50,10,10), got {c0}.");
            log.AppendLine($"Region1 cell0 = {c0} matches expected (4,50,10,10). ✓");

            // Region2 cellW = 40/5 = 8, cellH = 8. Third cell (index 2) left = 0 + 2*8 = 16.
            Rect r2c2 = cells2[2];
            if (!RectApprox(r2c2, new Rect(16, 0, 8, 8)))
                return Fail(log, out report, $"Region2 cell2 expected (16,0,8,8), got {r2c2}.");
            log.AppendLine($"Region2 cell2 = {r2c2} matches expected (16,0,8,8). ✓");

            // Build the UNION and Apply in one call (SetSpriteRects REPLACES).
            var union = new System.Collections.Generic.List<Rect>();
            union.AddRange(cells1);
            union.AddRange(cells2);
            string baseName = "_region_sheet";
            var applied = RegionSlicer.Apply(
                sheetPath, union, 16f, GridSlicer.PivotMode.BottomCenter, new Vector2(0.5f, 0f),
                i => i < cells1.Count ? $"{baseName}_r0_{i:000}" : $"{baseName}_r1_{(i - cells1.Count):000}");
            log.AppendLine($"Applied union; importer returned {applied.Count} sprite(s).");

            // Reload from disk and count the persisted sprites.
            int persisted = 0;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
                if (obj is Sprite) persisted++;
            if (persisted != expected)
                return Fail(log, out report, $"Persisted sprite count {persisted}, expected {expected}.");
            log.AppendLine($"Reloaded importer has exactly {persisted} sprites (region1 {cells1.Count} + region2 {cells2.Count}). ✓");

            report = log.ToString();
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Track B — Per-frame pivot (Animate & Align registration) headless test
        // ════════════════════════════════════════════════════════════════════

        private const string PivotTestFolder = "Assets/Launimator/_SelfTestPivot";

        /// <summary>Entry point for the per-frame pivot headless test (run via execute_script).</summary>
        public static void RunPerFramePivotTest()
        {
            try
            {
                bool ok = PerFramePivotCore(out string report);
                Debug.Log(report);
                Debug.Log(ok
                    ? "ASSETSCAVENGE PERFRAMEPIVOT SELFTEST: PASS"
                    : "ASSETSCAVENGE PERFRAMEPIVOT SELFTEST: FAIL");
            }
            catch (Exception e)
            {
                Debug.LogError("ASSETSCAVENGE PERFRAMEPIVOT SELFTEST: FAIL (exception) " + e);
            }
        }

        /// <summary>
        /// Headless test of the per-frame-pivot <see cref="RegionSlicer.Apply"/> overload (the Animate &amp;
        /// Align registration bake). Applies TWO rects with DIFFERENT pivots, reloads the persisted
        /// SpriteRects from the data provider, and asserts each sprite carries its own pivot (Custom
        /// alignment). Also spot-checks <see cref="RegionSlicer.ContentBaselinePivot"/> on a known blob.
        /// </summary>
        public static bool PerFramePivotCore(out string report)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("── Launimator Per-Frame-Pivot SelfTest ──");

            if (AssetDatabase.IsValidFolder(PivotTestFolder))
                AssetDatabase.DeleteAsset(PivotTestFolder);
            AssetDatabase.Refresh();
            LauminaryBuilder.EnsureFolder(PivotTestFolder);

            int texW = 32, texH = 32;
            string sheetPath = $"{PivotTestFolder}/_pivot_sheet.png";
            WriteSolidSheet(sheetPath, texW, texH);
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);

            // Two distinct rects with two DISTINCT pivots.
            var rects = new System.Collections.Generic.List<Rect>
            {
                new Rect(0, 0, 16, 16),
                new Rect(16, 16, 16, 16),
            };
            var pivots = new System.Collections.Generic.List<Vector2>
            {
                new Vector2(0.25f, 0.10f),
                new Vector2(0.80f, 0.65f),
            };

            RegionSlicer.Apply(sheetPath, rects, pivots, 16f,
                i => $"_pivot_{i:000}", new Vector2(0.5f, 0f));

            // Reload the persisted SpriteRects via the data provider (tests what actually round-tripped).
            var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer == null) return Fail(log, out report, "No TextureImporter after Apply.");
            var factory = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var persisted = provider.GetSpriteRects();
            if (persisted.Length != 2)
                return Fail(log, out report, $"Expected 2 persisted sprite rects, got {persisted.Length}.");

            // Match by name (order is not guaranteed) and assert each pivot + Custom alignment round-tripped.
            for (int i = 0; i < rects.Count; i++)
            {
                string want = $"_pivot_{i:000}";
                var sr = System.Array.Find(persisted, s => s.name == want);
                if (sr == null) return Fail(log, out report, $"Persisted rect '{want}' missing.");
                if (sr.alignment != SpriteAlignment.Custom)
                    return Fail(log, out report, $"'{want}' alignment {sr.alignment}, expected Custom.");
                if (Mathf.Abs(sr.pivot.x - pivots[i].x) > 0.001f || Mathf.Abs(sr.pivot.y - pivots[i].y) > 0.001f)
                    return Fail(log, out report, $"'{want}' pivot {sr.pivot}, expected {pivots[i]}.");
                log.AppendLine($"'{want}' round-tripped pivot {sr.pivot} (Custom). ✓");
            }
            // Crucially, the two pivots are DIFFERENT — proving per-cell (not one global) pivots.
            log.AppendLine("Two cells carry DIFFERENT pivots end-to-end (per-frame registration). ✓");

            // ContentBaselinePivot spot-check: blob bottom-center of a known buffer.
            var px = new Color32[texW * texH];
            int bX0 = 8, bX1 = 23, bY0 = 4, bY1 = 19; // inclusive; center x = 15.5, bottom y = 4
            for (int y = bY0; y <= bY1; y++)
                for (int x = bX0; x <= bX1; x++)
                    px[y * texW + x] = new Color32(255, 255, 255, 255);
            Rect cell = new Rect(0, 0, 32, 32);
            if (!RegionSlicer.ContentBaselinePivot(px, texW, texH, cell, 8, out Vector2 bp))
                return Fail(log, out report, "ContentBaselinePivot flagged a cell with a blob as empty.");
            // bbox = (8,4,16,16); content cx = 8 + 8 = 16 → nx = 16/32 = 0.5; bottom = 4 → ny = 4/32 = 0.125.
            if (Mathf.Abs(bp.x - 0.5f) > 0.02f || Mathf.Abs(bp.y - 0.125f) > 0.02f)
                return Fail(log, out report, $"ContentBaselinePivot {bp}, expected ~(0.5,0.125).");
            log.AppendLine($"ContentBaselinePivot → {bp} ≈ (0.5,0.125) content bottom-center. ✓");

            report = log.ToString();
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Track B — Alpha-trim (TrimToContent) headless test
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Entry point for the alpha-trim headless test (run via execute_script).</summary>
        public static void RunAlphaTrimTest()
        {
            try
            {
                bool ok = AlphaTrimCore(out string report);
                Debug.Log(report);
                Debug.Log(ok
                    ? "ASSETSCAVENGE ALPHATRIM SELFTEST: PASS"
                    : "ASSETSCAVENGE ALPHATRIM SELFTEST: FAIL");
            }
            catch (Exception e)
            {
                Debug.LogError("ASSETSCAVENGE ALPHATRIM SELFTEST: FAIL (exception) " + e);
            }
        }

        /// <summary>
        /// Headless test of <see cref="RegionSlicer.TrimToContent"/>. Synthesises a Color32 buffer
        /// (bottom-left origin, row-major) with a KNOWN opaque blob inside a larger cell, asserts the
        /// trimmed rect equals the blob's exact bbox, and asserts an all-transparent cell flags empty.
        /// </summary>
        public static bool AlphaTrimCore(out string report)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("── Launimator Alpha-Trim SelfTest ──");

            // Synthesise a 32×32 transparent buffer with an opaque blob at x:[10..19], y:[6..21] (inclusive).
            int texW = 32, texH = 32;
            int blobX0 = 10, blobX1 = 19, blobY0 = 6, blobY1 = 21; // inclusive
            var px = new Color32[texW * texH];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            for (int y = blobY0; y <= blobY1; y++)
                for (int x = blobX0; x <= blobX1; x++)
                    px[y * texW + x] = new Color32(255, 128, 64, 255);

            // Cell larger than the blob, fully containing it: (4,2,24,28) → x:[4..27], y:[2..29].
            Rect cell = new Rect(4, 2, 24, 28);
            Rect trimmed = RegionSlicer.TrimToContent(px, texW, texH, cell, 8, out bool empty);
            if (empty)
                return Fail(log, out report, "TrimToContent flagged a cell containing an opaque blob as empty.");

            // Expected bbox: x=10, y=6, w=(19-10)+1=10, h=(21-6)+1=16.
            Rect expected = new Rect(blobX0, blobY0, (blobX1 - blobX0) + 1, (blobY1 - blobY0) + 1);
            if (!RectApprox(trimmed, expected))
                return Fail(log, out report, $"Trimmed rect {trimmed}, expected blob bbox {expected}.");
            log.AppendLine($"Trimmed (4,2,24,28) → {trimmed} matches blob bbox {expected}. ✓");

            // Sub-threshold alpha must NOT count as content: a cell over faint (alpha=8) pixels reads empty
            // when threshold == 8 (strictly greater than).
            int faintX = 25, faintY = 25;
            px[faintY * texW + faintX] = new Color32(255, 255, 255, 8);
            Rect faintCell = new Rect(24, 24, 4, 4); // x:[24..27], y:[24..27] — only the faint pixel inside, blob excluded
            RegionSlicer.TrimToContent(px, texW, texH, faintCell, 8, out bool faintEmpty);
            if (!faintEmpty)
                return Fail(log, out report, "A cell whose only pixel has alpha == threshold should flag empty (strictly-greater rule).");
            log.AppendLine("A cell over a single alpha==threshold pixel flags empty (strict > rule). ✓");

            // All-transparent cell elsewhere flags empty and returns the input unchanged.
            Rect emptyCell = new Rect(0, 24, 6, 6); // x:[0..5], y:[24..29] — no blob, no faint pixel
            Rect emptyResult = RegionSlicer.TrimToContent(px, texW, texH, emptyCell, 8, out bool allEmpty);
            if (!allEmpty)
                return Fail(log, out report, "An all-transparent cell should flag empty.");
            if (!RectApprox(emptyResult, emptyCell))
                return Fail(log, out report, $"Empty cell should return input unchanged; got {emptyResult} vs {emptyCell}.");
            log.AppendLine("All-transparent cell flags empty and returns the input rect unchanged. ✓");

            report = log.ToString();
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Track B — Click-to-bbox flood fill (FloodFillBBox) headless test
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Entry point for the flood-fill bbox headless test (run via execute_script).</summary>
        public static void RunFloodFillTest()
        {
            try
            {
                bool ok = FloodFillCore(out string report);
                Debug.Log(report);
                Debug.Log(ok
                    ? "ASSETSCAVENGE FLOODFILL SELFTEST: PASS"
                    : "ASSETSCAVENGE FLOODFILL SELFTEST: FAIL");
            }
            catch (Exception e)
            {
                Debug.LogError("ASSETSCAVENGE FLOODFILL SELFTEST: FAIL (exception) " + e);
            }
        }

        /// <summary>
        /// Headless test of <see cref="RegionSlicer.FloodFillBBox"/>. Synthesises a Color32 buffer
        /// (bottom-left origin, row-major) with TWO separated opaque blobs inside one marquee box,
        /// clicks (seeds) inside the FIRST blob, and asserts the returned bbox equals that blob's exact
        /// bbox — i.e. the connected-component fill did NOT bleed into the second blob. Also asserts a
        /// click on a transparent texel flags empty.
        /// </summary>
        public static bool FloodFillCore(out string report)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("── Launimator Flood-Fill BBox SelfTest ──");

            int texW = 48, texH = 48;
            var px = new Color32[texW * texH];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

            // Blob A: a solid rect x:[6..13], y:[8..19] (inclusive).
            int aX0 = 6, aX1 = 13, aY0 = 8, aY1 = 19;
            // Blob B: a solid rect x:[28..35], y:[10..25] (inclusive) — clearly separated from A by a
            // transparent gutter (x 14..27 is empty), so the two are NOT 4-connected.
            int bX0 = 28, bX1 = 35, bY0 = 10, bY1 = 25;

            void Fill(int x0, int x1, int y0, int y1)
            {
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        px[y * texW + x] = new Color32(255, 200, 80, 255);
            }
            Fill(aX0, aX1, aY0, aY1);
            Fill(bX0, bX1, bY0, bY1);

            // The marquee box encloses BOTH blobs: x:[2..40], y:[4..30].
            Rect box = new Rect(2, 4, 38, 26); // xMax=40, yMax=30

            // Click well inside blob A.
            int seedX = (aX0 + aX1) / 2, seedY = (aY0 + aY1) / 2;
            Rect bbA = RegionSlicer.FloodFillBBox(px, texW, texH, box, seedX, seedY, 8, out bool emptyA);
            if (emptyA)
                return Fail(log, out report, "FloodFillBBox flagged a click inside an opaque blob as empty.");

            Rect expectedA = new Rect(aX0, aY0, (aX1 - aX0) + 1, (aY1 - aY0) + 1);
            if (!RectApprox(bbA, expectedA))
                return Fail(log, out report, $"Blob-A bbox {bbA}, expected {expectedA} (fill must not bleed into blob B).");
            log.AppendLine($"Click in blob A → {bbA} matches blob-A bbox {expectedA}; did NOT merge blob B. ✓");

            // Sanity: clicking blob B yields blob B's bbox, distinct from A's.
            int seedBX = (bX0 + bX1) / 2, seedBY = (bY0 + bY1) / 2;
            Rect bbB = RegionSlicer.FloodFillBBox(px, texW, texH, box, seedBX, seedBY, 8, out bool emptyB);
            Rect expectedB = new Rect(bX0, bY0, (bX1 - bX0) + 1, (bY1 - bY0) + 1);
            if (emptyB || !RectApprox(bbB, expectedB))
                return Fail(log, out report, $"Blob-B bbox {bbB}, expected {expectedB}.");
            log.AppendLine($"Click in blob B → {bbB} matches blob-B bbox {expectedB}. ✓");

            // Click on a transparent texel (in the gutter) flags empty.
            RegionSlicer.FloodFillBBox(px, texW, texH, box, 20, 15, 8, out bool emptyGap);
            if (!emptyGap)
                return Fail(log, out report, "A click on a transparent texel should flag empty.");
            log.AppendLine("Click on a transparent texel flags empty. ✓");

            // The box bounds the fill: a blob touching the box edge is clipped to the box, not the blob.
            // Tighten the box to cut blob A in half horizontally: x:[2..10] (xMax=11) keeps cols 6..10 of A.
            Rect tightBox = new Rect(2, 4, 9, 26); // xMax=11
            Rect bbClip = RegionSlicer.FloodFillBBox(px, texW, texH, tightBox, seedX, seedY, 8, out bool emptyClip);
            if (emptyClip)
                return Fail(log, out report, "Clipped flood fill flagged empty unexpectedly.");
            // Visited x is clamped to [6..10]; y still [8..19].
            Rect expectedClip = new Rect(aX0, aY0, (10 - aX0) + 1, (aY1 - aY0) + 1);
            if (!RectApprox(bbClip, expectedClip))
                return Fail(log, out report, $"Box-clipped bbox {bbClip}, expected {expectedClip} (fill must respect box bounds).");
            log.AppendLine($"A box tighter than the blob clips the fill: {bbClip} matches {expectedClip}. ✓");

            report = log.ToString();
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Track B — Persistence (JSON sidecar) round-trip headless test
        // ════════════════════════════════════════════════════════════════════

        private const string PersistTestFolder = "Assets/Launimator/_SelfTestPersist";

        /// <summary>Entry point for the persistence round-trip headless test (run via execute_script).</summary>
        public static void RunPersistenceTest()
        {
            try
            {
                bool ok = PersistenceCore(out string report);
                Debug.Log(report);
                Debug.Log(ok
                    ? "ASSETSCAVENGE PERSISTENCE SELFTEST: PASS"
                    : "ASSETSCAVENGE PERSISTENCE SELFTEST: FAIL");
            }
            catch (Exception e)
            {
                Debug.LogError("ASSETSCAVENGE PERSISTENCE SELFTEST: FAIL (exception) " + e);
            }
        }

        /// <summary>
        /// Headless test of <see cref="RegionSlicerPersistence"/>. Synthesises a sheet, builds a state
        /// DTO with TWO regions whose cells carry DISTINCT per-cell pivots plus an ordered sequence and
        /// non-default settings, Saves to the sidecar, CLEARS the in-memory DTO, Loads it back, and
        /// asserts regions / cells / per-cell pivots / sequence / settings round-trip EXACTLY. Then it
        /// proves the index-drift guard: a sequence ref pointing past the regions is dropped on Normalize.
        /// </summary>
        public static bool PersistenceCore(out string report)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("── Launimator Persistence SelfTest ──");

            if (AssetDatabase.IsValidFolder(PersistTestFolder))
                AssetDatabase.DeleteAsset(PersistTestFolder);
            AssetDatabase.Refresh();
            LauminaryBuilder.EnsureFolder(PersistTestFolder);

            int texW = 64, texH = 64;
            string sheetPath = $"{PersistTestFolder}/_persist_sheet.png";
            WriteSolidSheet(sheetPath, texW, texH);
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);

            // Build a rich state: region A (2 cells, distinct pivots) + region B "hand-picked" (1 cell),
            // a 3-frame sequence, and deliberately non-default settings.
            var state = new RegionSlicerPersistence.StateDto
            {
                texturePath = sheetPath,
                textureGuid = AssetDatabase.AssetPathToGUID(sheetPath),
                texW = texW, texH = texH,
                alphaTrim = true, alphaThreshold = 42, ppu = 24f,
                pivotMode = (int)GridSlicer.PivotMode.Custom,
                customPivot = new RegionSlicerPersistence.Vec2Dto(new Vector2(0.33f, 0.66f)),
                gridMode = (int)RegionSlicer.GridMode.FixedCellSize,
                cols = 4, rows = 7, cellW = 18, cellH = 19, spacing = 2, padding = 1,
            };

            var regA = new RegionSlicerPersistence.RegionDto { label = "3x2 grid", bounds = new RegionSlicerPersistence.RectDto(new Rect(4, 40, 30, 20)) };
            regA.cells.Add(new RegionSlicerPersistence.RectDto(new Rect(4, 50, 10, 10)));
            regA.cells.Add(new RegionSlicerPersistence.RectDto(new Rect(14, 50, 10, 10)));
            regA.pivots.Add(new RegionSlicerPersistence.Vec2Dto(new Vector2(0.10f, 0.20f)));
            regA.pivots.Add(new RegionSlicerPersistence.Vec2Dto(new Vector2(0.90f, 0.80f)));

            var regB = new RegionSlicerPersistence.RegionDto { label = "hand-picked (click-to-bbox)", bounds = new RegionSlicerPersistence.RectDto(new Rect(0, 0, 16, 16)) };
            regB.cells.Add(new RegionSlicerPersistence.RectDto(new Rect(2, 2, 12, 13)));
            regB.pivots.Add(new RegionSlicerPersistence.Vec2Dto(new Vector2(0.42f, 0.07f)));

            state.regions.Add(regA);
            state.regions.Add(regB);

            regA.sourceTextureGuid = "source-a";
            regB.sourceTextureGuid = "source-b";
            regA.transforms.Add(new RegionSlicerPersistence.TransformDto(new CellTransform { flipX = true, angle = 35f, scaleX = 2f, scaleY = 3f }));
            regA.transforms.Add(new RegionSlicerPersistence.TransformDto(CellTransform.Identity));

            // Save → sidecar.
            string sidecar = RegionSlicerPersistence.Save(sheetPath, state);
            log.AppendLine($"Saved state to sidecar: {sidecar}.");
            if (!RegionSlicerPersistence.Exists(sheetPath))
                return Fail(log, out report, "Sidecar does not exist after Save.");

            // CLEAR in-memory, then Load back from disk (tests what actually persisted).
            state = null;
            var r = RegionSlicerPersistence.Load(sheetPath, out string loadErr);
            if (r == null)
                return Fail(log, out report, $"Load returned null after Save ({loadErr}).");
            log.AppendLine("Re-loaded state from sidecar after clearing in-memory copy.");

            // Settings round-trip.
            if (!r.alphaTrim || r.alphaThreshold != 42 || Mathf.Abs(r.ppu - 24f) > 0.001f)
                return Fail(log, out report, $"alpha/ppu mismatch: {r.alphaTrim}/{r.alphaThreshold}/{r.ppu}.");
            if (r.pivotMode != (int)GridSlicer.PivotMode.Custom
                || Mathf.Abs(r.customPivot.x - 0.33f) > 0.001f || Mathf.Abs(r.customPivot.y - 0.66f) > 0.001f)
                return Fail(log, out report, $"pivotMode/customPivot mismatch: {r.pivotMode}/({r.customPivot.x},{r.customPivot.y}).");
            if (r.gridMode != (int)RegionSlicer.GridMode.FixedCellSize
                || r.cols != 4 || r.rows != 7 || r.cellW != 18 || r.cellH != 19 || r.spacing != 2 || r.padding != 1)
                return Fail(log, out report, "grid settings mismatch.");
            log.AppendLine("Slicing settings (alpha, ppu, pivot, grid mode + cols/rows/cellW/cellH/spacing/padding) round-trip exactly. ✓");

            // Regions / cells / pivots round-trip.
            if (r.regions.Count != 2) return Fail(log, out report, $"Expected 2 regions, got {r.regions.Count}.");
            if (r.regions[0].label != "3x2 grid" || r.regions[1].label != "hand-picked (click-to-bbox)")
                return Fail(log, out report, "Region labels did not round-trip.");
            if (r.regions[0].cells.Count != 2 || r.regions[0].pivots.Count != 2)
                return Fail(log, out report, "Region A cell/pivot counts wrong.");
            if (!RectApprox(r.regions[0].cells[0].ToRect(), new Rect(4, 50, 10, 10))
                || !RectApprox(r.regions[0].cells[1].ToRect(), new Rect(14, 50, 10, 10)))
                return Fail(log, out report, "Region A cell rects did not round-trip.");
            var pa0 = r.regions[0].pivots[0].ToVec2();
            var pa1 = r.regions[0].pivots[1].ToVec2();
            if (Mathf.Abs(pa0.x - 0.10f) > 0.001f || Mathf.Abs(pa0.y - 0.20f) > 0.001f
                || Mathf.Abs(pa1.x - 0.90f) > 0.001f || Mathf.Abs(pa1.y - 0.80f) > 0.001f)
                return Fail(log, out report, $"Region A per-cell pivots did not round-trip: {pa0} / {pa1}.");
            var pb0 = r.regions[1].pivots[0].ToVec2();
            if (Mathf.Abs(pb0.x - 0.42f) > 0.001f || Mathf.Abs(pb0.y - 0.07f) > 0.001f)
                return Fail(log, out report, $"Region B pivot did not round-trip: {pb0}.");
            log.AppendLine("Two regions with index-aligned DISTINCT per-cell pivots round-trip exactly. ✓");

            if (r.regions[0].sourceTextureGuid != "source-a" || r.regions[1].sourceTextureGuid != "source-b")
                return Fail(log, out report, "Per-region source identities did not round-trip.");
            var transform = r.regions[0].transforms[0].ToTransform();
            if (!transform.flipX || transform.angle != 35f || transform.scaleX != 2f || transform.scaleY != 3f)
                return Fail(log, out report, "Sprite transform did not round-trip.");
            r.regions[1].transforms.Clear();
            RegionSlicerPersistence.Normalize(r);
            if (r.regions[1].transforms.Count != 1 || !r.regions[1].transforms[0].ToTransform().IsIdentity)
                return Fail(log, out report, "Missing transforms must normalize to independent identity entries.");
            log.AppendLine("Per-region sources and sprite transforms round-trip; normalization fills missing transforms. ✓");

            // Cleanup the test artifacts.
            AssetDatabase.DeleteAsset(PersistTestFolder);

            report = log.ToString();
            return true;
        }

        private static bool RectApprox(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f
                && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;
        }

        private static void WriteSolidSheet(string assetPath, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)(i % 255), (byte)((i * 7) % 255), 120, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            string sysPath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(sysPath));
            File.WriteAllBytes(sysPath, png);
        }

        /// <summary>
        /// Writes a horizontal strip PNG: FrameCount cells, each a distinct solid colour with
        /// a distinguishing inner shape so frames are visually unique. Transparent background.
        /// </summary>
        private static void WriteSyntheticStrip(string assetPath)
        {
            int w = Cell * FrameCount, h = Cell;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            Color32[] palette =
            {
                new Color32(220, 60, 60, 255),
                new Color32(60, 200, 80, 255),
                new Color32(70, 110, 230, 255),
                new Color32(230, 200, 50, 255),
                new Color32(200, 80, 210, 255),
            };

            for (int f = 0; f < FrameCount; f++)
            {
                int x0 = f * Cell;
                var col = palette[f % palette.Length];
                // Fill a centred shrinking square per frame so each is distinct.
                int inset = f % (Cell / 2);
                for (int y = 0; y < Cell; y++)
                {
                    for (int x = 0; x < Cell; x++)
                    {
                        bool inShape = x >= inset && x < Cell - inset && y >= inset && y < Cell - inset;
                        if (inShape)
                            pixels[y * w + (x0 + x)] = col;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string sysPath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(sysPath));
            File.WriteAllBytes(sysPath, png);
        }
    }
}
