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

        // ------------------------------------------------------------------ the edge fade (graded alpha)

        public struct AlphaResult
        {
            public string name;
            public int spritePx, insidePx, bandPx, beyondPx, mismatch, jumpMismatch, hardMismatch;
            public float insideMinAlpha, beyondMaxAlpha, bandMeanErr, bandMaxErr, tol;
            public bool pass, monotonic, informational;
            public string profile, pngPath;
            public override string ToString() =>
                $"{(informational ? "INFO" : pass ? "PASS" : "FAIL")}  {name}: sprite px {spritePx} = inside {insidePx} (min alpha {insideMinAlpha:F3}) " +
                $"+ fade band {bandPx} (|measured-expected| mean {bandMeanErr:F4}, max {bandMaxErr:F4}) + beyond {beyondPx} (max alpha {beyondMaxAlpha:F3}); " +
                $"off by > {tol:F3}+quantisation: {mismatch} (at a hard jump, within the px tolerance {jumpMismatch}, elsewhere {hardMismatch})" +
                (profile != null ? $"; profile {(monotonic ? "MONOTONIC" : "NOT MONOTONIC")}: {profile}" : "");
        }

        /// Measure one sprite's RENDERED alpha per pixel against a caller-computed EXPECTED visibility (0..1) at
        /// that pixel's world point (from independent geometry, e.g. <see cref="VisionFadeTruth"/> — never from
        /// VisionMask's own maths). Rendered alpha = masked brightness / bypassed brightness (black clear, so
        /// masked = colour × alpha). Every sprite pixel must match within `tol` plus 8-bit quantisation; only where
        /// the expectation itself JUMPS (a wall's hard shadow line, a hard edge) is a pixel allowed to take a
        /// neighbour's value within `jumpPx`. Pixels are also sorted into inside (expected 1), band (between) and
        /// beyond (expected 0), so "fully lit stays 1, fully dark stays 0" is reported on its own.
        /// Optional profile: `profileFrom`→`profileTo` (world), sampled at 24 points, printed measured/expected,
        /// and checked for monotonic non-increase when the expectation is non-increasing along it.
        public static AlphaResult MeasureAlpha(string name, SpriteRenderer sr, Func<Vector3, float> expected, VisionPlane plane,
                                               int resolution = 256, string pngDir = null, int jumpPx = BoundaryPx, float tol = 0.02f,
                                               Vector3? profileFrom = null, Vector3? profileTo = null, float fadeWidth = 0f)
        {
            var res = new AlphaResult { name = name, tol = tol, insideMinAlpha = 1f, monotonic = true };
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
                var exp = new float[n];
                var meas = new float[n];
                var lum = new float[n];
                double bandErr = 0;
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * resolution + x;
                    var f = full[i];
                    float fl = Mathf.Max(f.r, Mathf.Max(f.g, f.b));
                    if (fl < 0.08f) continue;
                    isSprite[i] = true; lum[i] = fl;
                    res.spritePx++;
                    var w = cam.ViewportToWorldPoint(new Vector3((x + 0.5f) / resolution, (y + 0.5f) / resolution, dist));
                    exp[i] = Mathf.Clamp01(expected(w));
                    var m = masked[i];
                    meas[i] = Mathf.Clamp01(Mathf.Max(m.r, Mathf.Max(m.g, m.b)) / fl);
                    if (exp[i] >= 1f) { res.insidePx++; res.insideMinAlpha = Mathf.Min(res.insideMinAlpha, meas[i]); }
                    else if (exp[i] <= 0f) { res.beyondPx++; res.beyondMaxAlpha = Mathf.Max(res.beyondMaxAlpha, meas[i]); }
                    else
                    {
                        res.bandPx++;
                        float e = Mathf.Abs(meas[i] - exp[i]);
                        bandErr += e; res.bandMaxErr = Mathf.Max(res.bandMaxErr, e);
                    }
                }
                res.bandMeanErr = res.bandPx > 0 ? (float)(bandErr / res.bandPx) : 0f;

                // Where is the expectation DISCONTINUOUS (a wall's shadow line, a hard edge)? Between two adjacent
                // pixels the smooth fade can change by at most its steepest slope (1.5 / fade per unit, the
                // smoothstep's peak) — anything well above that is a jump, however faint the band is there.
                float pxPerUnit = resolution / (2f * cam.orthographicSize);
                float jumpDelta = fadeWidth > 0f ? Mathf.Max(0.03f, 2.5f * 1.5f / (fadeWidth * pxPerUnit)) : 0.03f;
                var jumpy = new bool[n];
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * resolution + x;
                    if (!isSprite[i]) continue;
                    if (x + 1 < resolution && isSprite[i + 1] && Mathf.Abs(exp[i + 1] - exp[i]) > jumpDelta) jumpy[i] = jumpy[i + 1] = true;
                    if (y + 1 < resolution && isSprite[i + resolution] && Mathf.Abs(exp[i + resolution] - exp[i]) > jumpDelta) jumpy[i] = jumpy[i + resolution] = true;
                }

                var verdict = pngDir != null ? new Color[n] : null;
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * resolution + x;
                    if (!isSprite[i]) continue;
                    float allow = tol + 2f / (255f * lum[i]);   // 8-bit steps in both renders, relative to this pixel's brightness
                    float err = Mathf.Abs(meas[i] - exp[i]);
                    if (err <= allow)
                    {
                        if (verdict != null) verdict[i] = exp[i] >= 1f ? new Color(0.1f, 0.85f, 0.2f)
                                                        : exp[i] <= 0f ? new Color(0.45f, 0.05f, 0.05f)
                                                        : Color.Lerp(new Color(0.1f, 0.2f, 0.6f), new Color(0.3f, 0.7f, 1f), exp[i]);
                        continue;
                    }
                    res.mismatch++;
                    // A jump in the expectation within jumpPx (a wall's shadow line) whose other side explains this value?
                    // Only next to a real discontinuity — never inside the smooth band, where every pixel must match.
                    bool atJump = false;
                    for (int dy = -jumpPx; dy <= jumpPx && !atJump; dy++)
                    for (int dx = -jumpPx; dx <= jumpPx && !atJump; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= resolution || yy >= resolution) continue;
                        int j = yy * resolution + xx;
                        if (!isSprite[j]) continue;
                        if (jumpy[j] && Mathf.Abs(meas[i] - exp[j]) <= allow) atJump = true;
                    }
                    if (atJump) res.jumpMismatch++; else res.hardMismatch++;
                    if (verdict != null) verdict[i] = atJump ? Color.yellow : Color.magenta;
                }

                // Profile along a world line: measured vs expected; monotonic where the expectation is.
                if (profileFrom.HasValue && profileTo.HasValue)
                {
                    // Every render pixel the line crosses (a sprite with holes — a character silhouette — would starve
                    // a fixed handful of samples), only sprite pixels count; at least 12 are required.
                    // NINE parallel lines across the sprite (a silhouette can leave one line almost empty), each checked on
                    // its own; together they must cross at least 12 sprite pixels. The fullest line is printed.
                    var vA = cam.WorldToViewportPoint(profileFrom.Value); var vB = cam.WorldToViewportPoint(profileTo.Value);
                    Vector2 along = (Vector2)(vB - vA), perp = new Vector2(-along.y, along.x);
                    perp = perp.sqrMagnitude > 1e-12f ? perp.normalized : Vector2.zero;
                    int steps = Mathf.Max(24, Mathf.CeilToInt(along.magnitude * resolution * 2f));
                    List<(float m, float e)> best = null;
                    int total = 0;
                    for (int line = -4; line <= 4; line++)
                    {
                        var shift = (Vector3)(perp * (line * 0.08f));   // ±0.32 of the frame across the edge direction
                        var pts = new List<(float m, float e)>();
                        float prevM = float.MaxValue, prevE = float.MaxValue;
                        int lastI = -1;
                        for (int k = 0; k <= steps; k++)
                        {
                            var vp = Vector3.Lerp(vA, vB, (float)k / steps) + shift;
                            int x = Mathf.FloorToInt(vp.x * resolution), y = Mathf.FloorToInt(vp.y * resolution);
                            if (x < 0 || y < 0 || x >= resolution || y >= resolution) continue;
                            int i = y * resolution + x;
                            if (!isSprite[i] || i == lastI) continue;
                            lastI = i;
                            float allow = tol + 2f / (255f * lum[i]);
                            if (exp[i] <= prevE + 1e-4f && meas[i] > prevM + allow) res.monotonic = false;
                            prevM = meas[i]; prevE = exp[i];
                            pts.Add((meas[i], exp[i]));
                        }
                        total += pts.Count;
                        if (best == null || pts.Count > best.Count) best = pts;
                    }
                    if (total < 12) res.monotonic = false;
                    var sbp = new StringBuilder();
                    int shown = Mathf.Min(24, best.Count);
                    for (int k = 0; k < shown; k++)
                    {
                        var q = best[shown > 1 ? Mathf.RoundToInt(k * (best.Count - 1f) / (shown - 1f)) : 0];
                        sbp.Append($"{q.m:F2}/{q.e:F2} ");
                    }
                    res.profile = $"(measured/expected; 9 lines, {total} sprite px; fullest line {best.Count} px, {shown} shown) " + sbp.ToString().TrimEnd();
                }

                res.pass = res.spritePx > 0 && res.hardMismatch == 0 && res.monotonic;

                if (pngDir != null)
                {
                    Directory.CreateDirectory(pngDir);
                    // Left: the masked render. Middle: EXPECTED alpha as grey. Right: verdict — green inside, dark red
                    // beyond, blue→cyan fade band (correct), MAGENTA wrong, yellow wrong only at a hard jump.
                    var img = new Texture2D(resolution * 3, resolution, TextureFormat.RGBA32, false);
                    for (int y = 0; y < resolution; y++)
                    for (int x = 0; x < resolution; x++)
                    {
                        int i = y * resolution + x;
                        var m = masked[i]; m.a = 1f;
                        img.SetPixel(x, y, m);
                        img.SetPixel(resolution + x, y, isSprite[i] ? new Color(exp[i], exp[i], exp[i]) : new Color(0.1f, 0f, 0.1f));
                        img.SetPixel(2 * resolution + x, y, isSprite[i] ? verdict[i] : Color.black);
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

                RunFadeCases(root, cone, sprite, O, occLayer, dir, results, sb);

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
            int fadePass = 0;
            sb.AppendLine("EDGE FADE (expected alpha per pixel from VisionFadeTruth — raw geometry, never the shader):");
            foreach (var r in fadeResults) { sb.AppendLine("  " + r); if (r.pass) fadePass++; }
            sb.AppendLine($"FADE VERDICT: {fadePass}/{fadeResults.Count} fade cases pass");
            pass += fadePass; counted += fadeResults.Count;
            sb.AppendLine($"VERDICT: {pass}/{counted} cases pass (INFO rows are not counted: they show that the occlusion " +
                          "error shrinks with the shadow resolution, i.e. it is quantisation along the edge).");
            sb.AppendLine("  Note: A whole-object rule could only ever show 0 % or 100 % " +
                          "of a sprite (or all pixels at one faded level); the partial fractions above match the geometric " +
                          "inside set pixel by pixel.");
            sb.AppendLine("PNGs: " + dir);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
            return sb.ToString();
        }

        static readonly List<AlphaResult> fadeResults = new();

        static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

        /// H. The EDGE FADE, every boundary kind, at off-axis angles, judged per pixel against VisionFadeTruth.
        /// Sprite is 1x1 world unit; fade 0.4 u, so each placement holds inside, band and beyond pixels at once.
        static void RunFadeCases(GameObject root, VisionCone cone, SpriteRenderer sprite, Vector3 O, int occLayer, string dir,
                                 List<Result> results, StringBuilder sb)
        {
            fadeResults.Clear();
            const float F = 0.4f;
            var O2 = (Vector2)O;
            var live = new List<VisionCone> { cone };
            Func<Vector3, float> Exp()
            {
                var spec = VisionFadeTruth.FromLive(live, VisionPlane.XY);   // snapshot of the cones' own fields
                return w => VisionFadeTruth.Value(spec, new Vector2(w.x, w.y));
            }
            void Place(Vector2 c) { sprite.transform.position = new Vector3(c.x, c.y, 0f); Physics2D.SyncTransforms(); }
            Vector3 P(Vector2 v) => new Vector3(v.x, v.y, 0f);
            float pxPerUnit = 256f / (2f * 0.5f * 1.15f);

            // The oracle checks itself first: analytic distance vs brute-force dense boundary sampling.
            foreach (var (label, half, range) in new[] { ("60° r20", 30f, 20f), ("360° r2.25", 180f, 2.25f), ("250° r6", 125f, 6f) })
            {
                var c = new VisionFadeTruth.Cone { eye = Vector2.zero, facing = Dir(37f), halfDeg = half, range = range, fade = F };
                float worst = VisionFadeTruth.CrossCheck(c, 600, 1.5f, 100000, 7, out float spacing);
                sb.AppendLine($"  oracle cross-check {label}: analytic vs brute-force distance, worst {worst:0.00000} u (sampling spacing {spacing:0.00000} u) {(worst <= spacing ? "OK" : "DISAGREES")}");
            }

            cone.edgeFade = F; cone.angle = 60f; cone.range = 100f; cone.omniRadius = 0f; cone.occluders = 0;
            try
            {
                // H1 angular edge, TILTED (lower edge at 20°), inside / straddling / outside the band.
                SetFacing(cone, 50f);
                Vector2 e1 = Dir(20f), nOut = new Vector2(e1.y, -e1.x);
                foreach (float off in new[] { -0.2f, 0.25f, 0.5f })
                {
                    var c = O2 + e1 * 20f + nOut * off;
                    Place(c);
                    fadeResults.Add(MeasureAlpha($"H1 tilted cone edge, centre {off:+0.00;-0.00} u past it", sprite, Exp(), VisionPlane.XY, 256, dir, fadeWidth: F,
                        profileFrom: P(c - nOut * 0.45f), profileTo: P(c + nOut * 0.45f)));
                }

                // H2 range arc on a diagonal (facing 135°, pixel direction 150°).
                SetFacing(cone, 135f); cone.range = 20f;
                foreach (float off in new[] { 0f, 0.3f })
                {
                    var c = O2 + Dir(150f) * (20f + off);
                    Place(c);
                    fadeResults.Add(MeasureAlpha($"H2 range arc at 150°, centre {off:+0.00} u past it", sprite, Exp(), VisionPlane.XY, 256, dir, fadeWidth: F,
                        profileFrom: P(c - Dir(150f) * 0.45f), profileTo: P(c + Dir(150f) * 0.45f)));
                }

                // H3 omni disc rim, far from the (narrow, turned away) cone.
                SetFacing(cone, 0f); cone.angle = 10f; cone.range = 100f; cone.omniRadius = 20f;
                {
                    var c = O2 + Dir(225f) * 20.25f;
                    Place(c);
                    fadeResults.Add(MeasureAlpha("H3 omni rim at 225°", sprite, Exp(), VisionPlane.XY, 256, dir, fadeWidth: F,
                        profileFrom: P(c - Dir(225f) * 0.45f), profileTo: P(c + Dir(225f) * 0.45f)));
                }
                cone.omniRadius = 0f;

                // H4 a 360° cone's radial edge (the body-glow shape), incl. exactly BEHIND its facing.
                cone.angle = 360f; cone.range = 20f;
                foreach (float deg in new[] { 180f, 245f, 315f })
                {
                    var c = O2 + Dir(deg) * 20.2f;
                    Place(c);
                    fadeResults.Add(MeasureAlpha($"H4 360° cone rim at {deg:0}°", sprite, Exp(), VisionPlane.XY, 256, dir, fadeWidth: F,
                        profileFrom: P(c - Dir(deg) * 0.45f), profileTo: P(c + Dir(deg) * 0.45f)));
                }

                // Occlusion cases: the shadow line is exact to one shadow texel, so that is the jump tolerance.
                int occMask = 1 << occLayer;
                cone.occluders = occMask; cone.angle = 60f; cone.rays = 1024;
                int TexelTolPx(float atDist)
                {
                    float half = 30f, cover = Mathf.Min(180f, half + VisionCone.FadeShadowMarginDeg);
                    int texels = Mathf.Clamp(Mathf.CeilToInt(1024 * cover / half), 1024, VisionMask.MaxRays);
                    return Mathf.Max(BoundaryPx, Mathf.CeilToInt(2f * cover * Mathf.Deg2Rad / texels * atDist * pxPerUnit) + 1);
                }
                GameObject Wall(Vector2 centre, Vector2 size)
                {
                    var w = new GameObject("Wall") { hideFlags = HideFlags.DontSave, layer = occLayer };
                    w.transform.SetParent(root.transform, false);
                    w.transform.position = new Vector3(centre.x, centre.y, 0f);
                    w.AddComponent<BoxCollider2D>().size = size;
                    Physics2D.SyncTransforms();
                    return w;
                }

                // H5 a wall's shadow line crossing the RADIAL fade band: band pixels behind the wall must be 0.
                SetFacing(cone, 0f); cone.range = 19.8f;
                var wall = Wall(O2 + new Vector2(10f, 2.7f), new Vector2(0.2f, 5f));   // y 0.2..5.2 at x 10
                Place(O2 + new Vector2(20f, 0f));
                fadeResults.Add(MeasureAlpha("H5 wall shadow across the radial band (hard shadow)", sprite, Exp(), VisionPlane.XY, 256, dir, TexelTolPx(20f), fadeWidth: F));
                UnityEngine.Object.DestroyImmediate(wall);

                // H6 a wall just PAST the range, inside the band: what is behind it must stay hidden (the shadow is
                // cast to range + fade).
                cone.range = 19.6f;
                wall = Wall(O2 + new Vector2(19.85f, -2.5f), new Vector2(0.1f, 5f));   // x 19.8..19.9, y -5..0
                Place(O2 + new Vector2(20f, 0f));
                fadeResults.Add(MeasureAlpha("H6 wall inside the band just past the range", sprite, Exp(), VisionPlane.XY, 256, dir, TexelTolPx(20f), fadeWidth: F,
                    profileFrom: P(O2 + new Vector2(19.55f, 0.3f)), profileTo: P(O2 + new Vector2(20.45f, 0.3f))));
                UnityEngine.Object.DestroyImmediate(wall);

                // H7 a wall OUTSIDE the cone's angle, shadowing part of the ANGULAR band (the shadow row's margin).
                SetFacing(cone, 30f); cone.range = 100f;
                wall = Wall(O2 + new Vector2(12f, -0.3f), new Vector2(0.2f, 0.4f));    // y -0.5..-0.1 at x 12
                Place(O2 + new Vector2(20f, -0.25f));
                fadeResults.Add(MeasureAlpha("H7 wall shadowing the angular band outside the cone", sprite, Exp(), VisionPlane.XY, 256, dir, TexelTolPx(20f), fadeWidth: F));
                UnityEngine.Object.DestroyImmediate(wall);
                cone.occluders = 0;

                // H8 two fading cones: the union is the brighter of the two, pixel by pixel.
                SetFacing(cone, 30f);
                var eye2 = O + new Vector3(20f, 30f, 0f);
                var cone2 = NewCone(root, eye2, 1f, 100f, 0f);
                cone2.edgeFade = F;
                SetFacing(cone2, -90f);
                live.Add(cone2);
                Place(O2 + new Vector2(20f, -0.2f));
                fadeResults.Add(MeasureAlpha("H8 two fading cones (union = max)", sprite, Exp(), VisionPlane.XY, 256, dir, fadeWidth: F));
                live.Remove(cone2);
                UnityEngine.Object.DestroyImmediate(cone2.gameObject);
            }
            finally
            {
                cone.edgeFade = 0f; cone.angle = 60f; cone.range = 100f; cone.omniRadius = 0f; cone.occluders = 0; cone.rays = 1024;
            }
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
