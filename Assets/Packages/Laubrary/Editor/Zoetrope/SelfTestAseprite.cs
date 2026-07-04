using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Fully-automated end-to-end test of the self-containment + Aseprite round-trip pipeline — NO human drawing.
    /// It synthesizes a throwaway zoe from a generated sheet, then exercises:
    ///   detach-on-use → promote to .aseprite → HEADLESS Aseprite edit (batch-mode Lua shifts the art down) →
    ///   sync back → re-bake, plus a self-containment check (delete the sheet, rebuild still works).
    /// Run via Tools ▸ Zoetrope ▸ Tests ▸ Run Aseprite Round-Trip Test, or invoke <see cref="Run"/> by
    /// reflection over the Coplay bridge. Results are logged; everything it creates is deleted at the end.
    /// </summary>
    public static class SelfTestAseprite
    {
        private const string TmpFolder = "Assets/Zoetrope/_TestTmp";
        private const int CellW = 16, CellH = 32, Frames = 4, ShiftDown = 6;

        [MenuItem("Laubrary/Zoetrope/Tests/Run Aseprite Round-Trip Test")]
        public static void Run()
        {
            var results = new List<(string name, bool ok, string detail)>();
            void Check(string name, bool ok, string detail = "") => results.Add((name, ok, detail));

            Zoe zoe = null;
            string sheetPath = null;
            try
            {
                // ── 0. Synthesize a source sheet: Frames cells of CellW×CellH, a solid block in the TOP of each ──
                ZoeBuilder.EnsureFolder(TmpFolder);
                sheetPath = $"{TmpFolder}/ase_test_sheet.png";
                int W = CellW * Frames, H = CellH;
                var px = new Color32[W * H]; // transparent
                for (int f = 0; f < Frames; f++)
                    for (int y = 20; y < 30; y++)              // bottom-up: high y = near the TOP
                        for (int x = f * CellW + 4; x < f * CellW + 12; x++)
                            px[y * W + x] = new Color32((byte)(60 + f * 50), 200, 80, 255);
                WritePngReadable(px, W, H, sheetPath);
                string sheetGuid = AssetDatabase.AssetPathToGUID(sheetPath);
                Check("synthetic sheet created", !string.IsNullOrEmpty(sheetGuid), sheetPath);

                // ── 1. New zoe + an animation whose recipe points at the (external) sheet ──
                zoe = ZoeRepo.CreateZoe($"AseTest_{System.DateTime.Now.Ticks}", 16f);
                var def = new AnimationDef { name = "Swing", fps = 10f, framePivot = new Vector2(0.5f, 0f) };
                for (int f = 0; f < Frames; f++)
                    def.recipe.Add(new FrameRef
                    {
                        sourceTextureGuid = sheetGuid,
                        cell = new Rect(f * CellW, 0, CellW, CellH),
                        pivot = new Vector2(0.5f, 0f),
                        transform = CellTransform.Identity
                    });
                ZoeRepo.SaveAnimationToDraft(zoe, def);

                // ── 2. Detach-on-use: recipe must now point inside the zoe's own draft/Source/ folder ──
                string draftFolder = ZoeRepo.DraftFolder(zoe);
                string srcFolder = ZoeSources.SourceFolder(draftFolder);
                var live = ZoeRepo.GetDraftAnimation(zoe, "Swing");
                string f0Path = AssetDatabase.GUIDToAssetPath(live.recipe[0].sourceTextureGuid);
                bool owned = !string.IsNullOrEmpty(f0Path) && f0Path.Replace('\\', '/').StartsWith(srcFolder + "/");
                Check("detach: recipe re-owned into Source/", owned, f0Path);
                Check("detach: owned source PNG exists", File.Exists(ToSystemPath(ZoeSources.SourcePath(draftFolder, zoe.zoeName))));

                // Self-containment: drop the source sheet NOW. Everything below must work without it.
                AssetDatabase.DeleteAsset(sheetPath); sheetPath = null;

                // ── 3. Promote the animation to an editable .aseprite ──
                bool promoted = AnimationAseprite.Promote(live, draftFolder, out string pStatus);
                ZoeRepo.SaveAnimationToDraft(zoe, live);
                live = ZoeRepo.GetDraftAnimation(zoe, "Swing");
                bool aseExists = !string.IsNullOrEmpty(live.asepriteSourcePath) && File.Exists(ToSystemPath(live.asepriteSourcePath));
                Check("promote: .aseprite written", promoted && aseExists, pStatus);
                if (!aseExists) { Report(results); return; }

                // ── 4. HEADLESS Aseprite edit: batch-mode Lua shifts every cel down ShiftDown px ──
                int preTop = ContentTopBottomUp(live.asepriteSourcePath, 0);
                bool ran = RunAsepriteBatch(live.asepriteSourcePath, ShiftDown, out string aseLog);
                int postTop = ContentTopBottomUp(live.asepriteSourcePath, 0);
                Check("headless Aseprite ran", ran, aseLog);
                Check($"headless edit moved art down (top {preTop}→{postTop}, bottom-up)",
                    ran && preTop > 0 && postTop > 0 && (preTop - postTop) >= ShiftDown / 2,
                    $"Δ={preTop - postTop}");

                // ── 5. Sync the edit back → owned source rewritten, recipe repointed, frame box fixed ──
                bool synced = AnimationAseprite.Sync(live, draftFolder, out string sStatus);
                ZoeRepo.SaveAnimationToDraft(zoe, live);
                live = ZoeRepo.GetDraftAnimation(zoe, "Swing");
                string syncedSrc = AssetDatabase.GUIDToAssetPath(live.recipe[0].sourceTextureGuid);
                Check("sync: recipe points at synced owned source", synced && syncedSrc.Contains("/Source/") && syncedSrc.EndsWith("_src.png"), sStatus);
                Check("sync: animation re-baked (clip + frames present)", live.clip != null && live.frames != null && live.frames.Count == Frames);

                // ── 6. Force one more full rebuild (still sheet-less) to confirm stability ──
                ZoeRepo.SetDraftAnimationFps(zoe, "Swing", 12f);
                var after = ZoeRepo.GetDraftAnimation(zoe, "Swing");
                Check("self-contained: rebuilds with the sheet gone", after != null && after.frames != null && after.frames.Count == Frames && after.frames.All(s => s != null));
            }
            catch (System.Exception e)
            {
                Check("UNEXPECTED EXCEPTION", false, e.ToString());
            }
            finally
            {
                if (zoe != null) ZoeRepo.Delete(zoe);
                if (AssetDatabase.IsValidFolder(TmpFolder)) AssetDatabase.DeleteAsset(TmpFolder);
                AssetDatabase.Refresh();
            }

            Report(results);
        }

        private static void Report(List<(string name, bool ok, string detail)> results)
        {
            int pass = results.Count(r => r.ok);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Aseprite Round-Trip Test: {pass}/{results.Count} passed ===");
            foreach (var (name, ok, detail) in results)
                sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : $"  — {detail}")}");
            if (pass == results.Count) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());
        }

        /// <summary>Run Aseprite in batch mode with the shift-down Lua. Returns false if Aseprite can't be found
        /// or the process errors. stdout/stderr are captured into <paramref name="log"/>.</summary>
        private static bool RunAsepriteBatch(string aseAssetPath, int down, out string log)
        {
            string exe = AsepriteLauncher.ResolveExe();
            if (string.IsNullOrEmpty(exe)) { log = "Aseprite executable not found — set it via Tools ▸ Zoetrope ▸ Set Aseprite Path."; return false; }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string lua = Path.GetFullPath(Path.Combine(projectRoot, "AsepriteScripts/test-shift-down.lua"));
            if (!File.Exists(lua)) { log = $"Lua not found: {lua}"; return false; }
            string aseAbs = ToSystemPath(aseAssetPath);

            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                Arguments = $"-b \"{aseAbs}\" --script-param down={down} --script \"{lua}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            try
            {
                using var p = System.Diagnostics.Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                bool exited = p.WaitForExit(30000);
                log = $"exit={(exited ? p.ExitCode.ToString() : "TIMEOUT")} {outp} {err}".Trim();
                return exited && p.ExitCode == 0;
            }
            catch (System.Exception e) { log = $"launch failed: {e.Message}"; return false; }
        }

        /// <summary>Topmost opaque row (bottom-up, i.e. the HIGHEST y with content) of a frame in a .aseprite,
        /// or -1 if none. Used to prove the headless edit moved the art down (this value should decrease).</summary>
        private static int ContentTopBottomUp(string aseAssetPath, int frame)
        {
            try
            {
                var doc = AsepriteIO.Read(File.ReadAllBytes(ToSystemPath(aseAssetPath)));
                if (frame >= doc.frameCount) return -1;
                int W = doc.width, H = doc.height, top = -1;
                var layers = doc.pixels[frame];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        foreach (var L in layers)
                            if (L != null && L[y * W + x].a > 0) { top = Mathf.Max(top, y); break; }
                return top;
            }
            catch { return -1; }
        }

        private static void WritePngReadable(Color32[] px, int w, int h, string assetPath)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try { tex.SetPixels32(px); tex.Apply(); File.WriteAllBytes(ToSystemPath(assetPath), tex.EncodeToPNG()); }
            finally { Object.DestroyImmediate(tex); }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.isReadable = true;
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
        }

        private static string ToSystemPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
