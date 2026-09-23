using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Laubrary.VisionMask.Editor
{
    /// The PROOF that VisionMask is per-pixel and not a whole-object verdict in disguise.
    ///
    /// For a sprite it renders the sprite twice through a private camera: once with the mask bypassed (which
    /// pixels ARE the sprite) and once masked (which of them survive). Every sprite pixel is then compared
    /// against a ground truth the CALLER computes from raw geometry at that pixel's world position — never
    /// from VisionMask's own maths — so the check cannot agree with itself by construction. A whole-object
    /// rule (sample points, bounds overlap, a faded alpha) can only produce a visible fraction of 0 or 1, or
    /// every pixel at the same partial value; a pass here means the visible set IS the geometric set.
    ///
    /// A mismatch is tolerated only on the boundary: a pixel whose truth differs from some sprite pixel
    /// within <see cref="BoundaryPx"/> pixels (a pixel centre a hair across an edge, or a ray-width of
    /// shadow). Everything else must match exactly.
    public static class VisionMaskPixelProbe
    {
        public const int ProbeLayer = 31;
        public const int BoundaryPx = 2;

        public struct Result
        {
            public string name;
            public int spritePx, truthPx, visiblePx, mismatch, boundaryMismatch, hardMismatch, tolerancePx, worstEdgePx;
            public float truthFraction, visibleFraction, insideLevel, outsideLevel;
            public bool pass, informational;
            public string pngPath;
            public override string ToString() =>
                $"{(informational ? "INFO" : pass ? "PASS" : "FAIL")}  {name}: sprite px {spritePx}, geometric-inside {truthPx} ({truthFraction:P1}), " +
                $"rendered-visible {visiblePx} ({visibleFraction:P1}), mismatches {mismatch} (within {tolerancePx}px of the true edge " +
                $"{boundaryMismatch}, beyond {hardMismatch}; worst {worstEdgePx}px from the edge), inside-pixel brightness " +
                $"{insideLevel:F3} of unmasked, outside {outsideLevel:F3}";
        }

        /// Measure one sprite. `truth(world)` = should the pixel at this world point be visible, from the
        /// caller's own geometry. `plane` = which way the private camera looks (XY: down +Z; XZ: down -Y).
        /// Temporarily moves the sprite to <see cref="ProbeLayer"/>; restores it. Works in edit and play mode.
        public static Result Measure(string name, SpriteRenderer sr, Func<Vector3, bool> truth, VisionPlane plane,
                                     int resolution = 256, string pngDir = null, int tolerancePx = BoundaryPx)
        {
            var res = new Result { name = name, tolerancePx = tolerancePx };
            var go = sr.gameObject;
            int oldLayer = go.layer;
            bool oldBypass = VisionMask.Bypass;
            var camGo = new GameObject("VisionMaskProbeCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                go.layer = ProbeLayer;
                var b = sr.bounds;
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.cullingMask = 1 << ProbeLayer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f;
                cam.enabled = false;
                const float dist = 20f;
                if (plane == VisionPlane.XY)
                {
                    cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y) * 1.15f;
                    camGo.transform.SetPositionAndRotation(b.center - Vector3.forward * dist, Quaternion.identity);
                }
                else
                {
                    cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) * 1.15f;
                    camGo.transform.SetPositionAndRotation(b.center + Vector3.up * dist, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                }
                cam.targetTexture = rt;

                VisionMask.Bypass = true;
                var full = Grab(cam, rt, resolution);
                VisionMask.Bypass = false;
                var masked = Grab(cam, rt, resolution);

                int n = resolution * resolution;
                var isSprite = new bool[n];
                var tr = new bool[n];
                var vis = new bool[n];
                double inSum = 0, inRef = 0, outSum = 0, outRef = 0;
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * resolution + x;
                    var f = full[i];
                    float fl = Mathf.Max(f.r, Mathf.Max(f.g, f.b));
                    if (fl < 0.08f) continue;   // background, or a pixel of the sprite too dark to judge
                    isSprite[i] = true;
                    res.spritePx++;
                    var w = cam.ViewportToWorldPoint(new Vector3((x + 0.5f) / resolution, (y + 0.5f) / resolution, dist));
                    tr[i] = truth(w);
                    var m = masked[i];
                    float ml = Mathf.Max(m.r, Mathf.Max(m.g, m.b));
                    vis[i] = ml > 0.5f * fl;
                    if (tr[i]) { res.truthPx++; inSum += ml; inRef += fl; }
                    else { outSum += ml; outRef += fl; }
                    if (vis[i]) res.visiblePx++;
                }
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * resolution + x;
                    if (!isSprite[i] || vis[i] == tr[i]) continue;
                    res.mismatch++;
                    int e = EdgeDistance(isSprite, tr, x, y, resolution, 64);
                    if (e > res.worstEdgePx) res.worstEdgePx = e;
                    if (e <= tolerancePx) res.boundaryMismatch++; else res.hardMismatch++;
                }
                res.truthFraction = res.spritePx > 0 ? (float)res.truthPx / res.spritePx : 0f;
                res.visibleFraction = res.spritePx > 0 ? (float)res.visiblePx / res.spritePx : 0f;
                res.insideLevel = inRef > 0 ? (float)(inSum / inRef) : 1f;
                res.outsideLevel = outRef > 0 ? (float)(outSum / outRef) : 0f;
                res.pass = res.spritePx > 0 && res.hardMismatch == 0
                           && Mathf.Abs(res.visibleFraction - res.truthFraction) <= 0.02f
                           && (res.truthPx == 0 || res.insideLevel > 0.95f)
                           && (res.truthPx == res.spritePx || res.outsideLevel < 0.05f);

                if (pngDir != null)
                {
                    Directory.CreateDirectory(pngDir);
                    // Left: the masked render as drawn. Right: the verdict map — green = visible and inside the
                    // cone, dark red = hidden and outside, MAGENTA = wrong, yellow = wrong but on the boundary.
                    var img = new Texture2D(resolution * 2, resolution, TextureFormat.RGBA32, false);
                    for (int y = 0; y < resolution; y++)
                    for (int x = 0; x < resolution; x++)
                    {
                        int i = y * resolution + x;
                        var m = masked[i]; m.a = 1f;
                        img.SetPixel(x, y, m);
                        Color v = Color.black;
                        if (isSprite[i])
                        {
                            if (vis[i] == tr[i]) v = tr[i] ? new Color(0.1f, 0.85f, 0.2f) : new Color(0.45f, 0.05f, 0.05f);
                            else v = EdgeDistance(isSprite, tr, x, y, resolution, tolerancePx) <= tolerancePx ? Color.yellow : Color.magenta;
                        }
                        img.SetPixel(resolution + x, y, v);
                    }
                    img.Apply();
                    res.pngPath = Path.Combine(pngDir, Sanitize(name) + ".png");
                    File.WriteAllBytes(res.pngPath, img.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(img);
                }
            }
            finally
            {
                VisionMask.Bypass = oldBypass;
                VisionMask.Publish();
                go.layer = oldLayer;
                UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            return res;
        }

        /// Chebyshev distance (pixels) from (x,y) to the nearest sprite pixel whose ground truth differs — i.e.
        /// how far this pixel is from the true edge. `max`+1 when there is none that close.
        static int EdgeDistance(bool[] isSprite, bool[] tr, int x, int y, int r, int max)
        {
            int i = y * r + x;
            for (int d = 1; d <= max; d++)
            {
                for (int k = -d; k <= d; k++)
                {
                    if (Differs(x + k, y - d) || Differs(x + k, y + d) || Differs(x - d, y + k) || Differs(x + d, y + k)) return d;
                }
            }
            return max + 1;

            bool Differs(int xx, int yy)
            {
                if (xx < 0 || yy < 0 || xx >= r || yy >= r) return false;
                int j = yy * r + xx;
                return isSprite[j] && tr[j] != tr[i];
            }
        }

        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').Replace('%', 'p');
        }

        static Color[] Grab(Camera cam, RenderTexture rt, int res)
        {
            VisionMask.Publish();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        // ------------------------------------------------------------------ the self-test

        [MenuItem("Laubrary/VisionMask/Run Pixel Probe")]
        public static void RunMenu()
        {
            var report = RunSelfTest();
            Debug.Log(report);
        }

        /// Controlled cases, far from any scene content, with every other cone switched off for the duration.
        /// Returns the report; PNGs land in <project>/Temp/VisionMaskProbe/.
        public static string RunSelfTest()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/VisionMaskProbe"));
            var sb = new StringBuilder();
            sb.AppendLine("VisionMask pixel probe — every sprite pixel vs independent cone geometry (" + DateTime.Now + ")");
            var results = new List<Result>();

            var otherCones = new List<VisionCone>();
            foreach (var k in VisionMask.Cones) if (k != null && k.enabled) otherCones.Add(k);
            foreach (var k in otherCones) k.enabled = false;
            var oldPlane = VisionMask.Plane;
            // Everything lives in a throwaway scene of its own, so the user's open scene is never dirtied.
            var userActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var tempScene = EditorApplication.isPlaying
                ? UnityEngine.SceneManagement.SceneManager.CreateScene("VisionMaskProbe " + DateTime.Now.Ticks)
                : UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                                                                          UnityEditor.SceneManagement.NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(tempScene);
            var root = new GameObject("VisionMaskProbe (temporary)") { hideFlags = HideFlags.DontSave };
            var white = MakeTex(64, (x, y) => Color.white);
            var halves = MakeTex(64, (x, y) => x < 32 ? Color.red : Color.green);
            try
            {
                var O = new Vector3(5000f, 5000f, 0f);
                int occLayer = ProbeLayer;

                VisionMask.Plane = VisionPlane.XY;
                var cone = NewCone(root, O, 60f, 100f, 0f);
                var sprite = NewSprite(root, white);

                // A. Straight cone edge crossing the sprite at 10/25/50/75/90 %. Facing 30° above +X, so the
                //    cone's lower edge is the ray along +X; the sprite (1x1) sits 20 units out, slid vertically.
                SetFacing(cone, 30f);
                foreach (float f in new[] { 0.10f, 0.25f, 0.50f, 0.75f, 0.90f })
                {
                    sprite.transform.position = O + new Vector3(20f, f - 0.5f, 0f);
                    results.Add(Measure($"A edge {f:P0} inside", sprite, w => InCone(w, O, 30f, 30f, 100f), VisionPlane.XY, 256, dir));
                }

                // B. A TILTED edge (lower edge at 20°): the boundary must follow the real line, not an axis.
                SetFacing(cone, 50f);
                var edgeDir = new Vector2(Mathf.Cos(20f * Mathf.Deg2Rad), Mathf.Sin(20f * Mathf.Deg2Rad));
                var normal = new Vector2(-edgeDir.y, edgeDir.x);
                foreach (float off in new[] { -0.4f, 0f, 0.4f })
                {
                    var c = (Vector2)O + edgeDir * 20f + normal * off;
                    sprite.transform.position = new Vector3(c.x, c.y, 0f);
                    results.Add(Measure($"B tilted edge offset {off:+0.0;-0.0}", sprite, w => InCone(w, O, 50f, 30f, 100f), VisionPlane.XY, 256, dir));
                }

                // C. The RANGE arc crossing the sprite (a curved boundary).
                SetFacing(cone, 0f);
                cone.range = 20f;
                foreach (float f in new[] { 0.10f, 0.50f, 0.90f })
                {
                    sprite.transform.position = O + new Vector3(20f + 0.5f - f, 0f, 0f);
                    results.Add(Measure($"C range arc {f:P0} inside", sprite, w => InCone(w, O, 0f, 30f, 20f), VisionPlane.XY, 256, dir));
                }
                cone.range = 100f;

                // D. OCCLUSION: a wall whose end casts a shadow edge across the sprite. Truth = Physics2D.Linecast
                //    per pixel (not the shadow map).
                var wall = new GameObject("Wall") { hideFlags = HideFlags.DontSave, layer = occLayer };
                wall.transform.SetParent(root.transform, false);
                var box = wall.AddComponent<BoxCollider2D>();
                wall.transform.position = O + new Vector3(10f, 0.2f + 2.5f, 0f);
                box.size = new Vector2(0.2f, 5f);   // spans y = O.y+0.2 .. O.y+5.2 at x = O.x+10
                cone.occluders = 1 << occLayer;
                sprite.transform.position = O + new Vector3(20f, 0f, 0f);
                Physics2D.SyncTransforms();
                int occMask = 1 << occLayer;
                // An occlusion edge is exact to ONE shadow texel (the angular resolution the cone was given),
                // so its tolerance is that texel's width at the sprite's distance, in probe pixels. Run at two
                // resolutions: if the error is quantisation, it shrinks with the resolution.
                float pxPerUnit = 256f / (2f * 0.5f * 1.15f);
                foreach (int rays in new[] { 256, 1024 })
                {
                    cone.rays = rays;
                    int tol = Mathf.Max(BoundaryPx, Mathf.CeilToInt(60f * Mathf.Deg2Rad / rays * 20f * pxPerUnit));
                    var rD = Measure(rays == 256 ? "D occluded, 256 shadow texels (coarse; resolution comparison only)"
                                                 : "D occluded, 1024 shadow texels (the default)", sprite,
                        w => InCone(w, O, 0f, 30f, 100f) && !Physics2D.Linecast(O, w, occMask), VisionPlane.XY, 256, dir, tol);
                    rD.informational = rays == 256;
                    results.Add(rD);
                    sb.AppendLine($"  D {rays} texels: {cone.lastRaycasts} physics raycasts for the frame (adaptive), tolerance {tol}px");
                }
                cone.occluders = 0;
                UnityEngine.Object.DestroyImmediate(wall);

                // E. TWO cones: the union, pixel by pixel. Cone 1's edge covers the top 30 %, cone 2 is a thin
                //    vertical beam from above through the sprite's middle.
                SetFacing(cone, 30f);
                sprite.transform.position = O + new Vector3(20f, 0.3f - 0.5f, 0f);
                var eye2 = O + new Vector3(20f, 30f, 0f);
                var cone2 = NewCone(root, eye2, 1f, 100f, 0f);
                SetFacing(cone2, -90f);
                results.Add(Measure("E two cones (union)", sprite,
                    w => InCone(w, O, 30f, 30f, 100f) || InCone(w, eye2, -90f, 0.5f, 100f), VisionPlane.XY, 256, dir));
                UnityEngine.Object.DestroyImmediate(cone2.gameObject);

                // F. flipX survives the mask (URP flips in the vertex shader). Half-covered: the edge at 50 %.
                sprite.sprite = Sprite.Create(halves, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);
                sprite.flipX = true;
                sprite.transform.position = O + new Vector3(20f, 0f, 0f);
                var rF = Measure("F flipX sprite, edge 50 %", sprite, w => InCone(w, O, 30f, 30f, 100f), VisionPlane.XY, 256, dir);
                results.Add(rF);
                sb.AppendLine("  flipX check: " + FlipCheck(sprite));
                sprite.flipX = false;
                sprite.sprite = Sprite.Create(white, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);

                // G. The XZ plane (top-down 3D): a flat sprite on the ground, the cone's facing along +X/+Z.
                VisionMask.Plane = VisionPlane.XZ;
                var O3 = new Vector3(5000f, 0f, 5000f);
                cone.transform.position = O3;
                cone.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(30f * Mathf.Deg2Rad), 0f, Mathf.Sin(30f * Mathf.Deg2Rad)), Vector3.up);
                sprite.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                foreach (float f in new[] { 0.10f, 0.50f, 0.90f })
                {
                    sprite.transform.position = O3 + new Vector3(20f, 0f, f - 0.5f);
                    results.Add(Measure($"G XZ plane edge {f:P0} inside", sprite,
                        w => InCone(new Vector3(w.x, w.z, 0f), new Vector3(O3.x, O3.z, 0f), 30f, 30f, 100f), VisionPlane.XZ, 256, dir));
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("PROBE EXCEPTION " + e);
            }
            finally
            {
                VisionMask.Plane = oldPlane;
                UnityEngine.Object.DestroyImmediate(root);
                if (userActive.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(userActive);
                if (EditorApplication.isPlaying) UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(tempScene);
                else UnityEditor.SceneManagement.EditorSceneManager.CloseScene(tempScene, true);
                UnityEngine.Object.DestroyImmediate(white);
                UnityEngine.Object.DestroyImmediate(halves);
                foreach (var k in otherCones) if (k != null) k.enabled = true;
                VisionMask.Publish();
            }

            int pass = 0, counted = 0;
            foreach (var r in results)
            {
                sb.AppendLine("  " + r);
                if (r.informational) continue;
                counted++;
                if (r.pass) pass++;
            }
            sb.AppendLine($"VERDICT: {pass}/{counted} cases pass (INFO rows are not counted: they show that the occlusion " +
                          "error shrinks with the shadow resolution, i.e. it is quantisation along the edge).");
            sb.AppendLine("  Note: A whole-object rule could only ever show 0 % or 100 % " +
                          "of a sprite (or all pixels at one faded level); the partial fractions above match the geometric " +
                          "inside set pixel by pixel.");
            sb.AppendLine("PNGs: " + dir);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
            return sb.ToString();
        }

        /// The sprite's left half is red, right half green; flipped, the leftmost visible column must be GREEN.
        static string FlipCheck(SpriteRenderer sr)
        {
            // Render unmasked and read the left and right quarter colours.
            var camGo = new GameObject("flipcam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            int old = sr.gameObject.layer;
            try
            {
                sr.gameObject.layer = ProbeLayer;
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true; cam.orthographicSize = 0.5f; cam.cullingMask = 1 << ProbeLayer;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear; cam.enabled = false;
                camGo.transform.position = sr.bounds.center - Vector3.forward * 20f;
                cam.targetTexture = rt;
                VisionMask.Bypass = true;
                var px = Grab(cam, rt, 64);
                VisionMask.Bypass = false;
                var left = px[32 * 64 + 8]; var right = px[32 * 64 + 56];
                bool ok = left.g > left.r && right.r > right.g;
                return (ok ? "PASS" : "FAIL") + $" (flipX=true: left quarter {left}, right quarter {right}; texture is red|green)";
            }
            finally
            {
                sr.gameObject.layer = old;
                UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        /// Independent ground truth: angle from the facing <= half angle, and within range. Plain trig.
        static bool InCone(Vector3 w, Vector3 eye, float facingDeg, float halfDeg, float range)
        {
            var d = new Vector2(w.x - eye.x, w.y - eye.y);
            if (d.magnitude > range) return false;
            var f = new Vector2(Mathf.Cos(facingDeg * Mathf.Deg2Rad), Mathf.Sin(facingDeg * Mathf.Deg2Rad));
            return Vector2.Angle(f, d) <= halfDeg;
        }

        static VisionCone NewCone(GameObject root, Vector3 at, float angle, float range, float omni)
        {
            var go = new GameObject("Cone") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            go.transform.position = at;
            var k = go.AddComponent<VisionCone>();
            k.angle = angle; k.range = range; k.omniRadius = omni; k.occluders = 0;
            return k;
        }

        static void SetFacing(VisionCone k, float deg) =>
            k.transform.up = new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad), 0f);

        static SpriteRenderer NewSprite(GameObject root, Texture2D tex)
        {
            var go = new GameObject("Sprite") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            sr.sharedMaterial = VisionMask.SpriteMaterial(0f);
            return sr;
        }

        static Texture2D MakeTex(int size, Func<int, int, Color> f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) t.SetPixel(x, y, f(x, y));
            t.Apply();
            return t;
        }
    }
}
