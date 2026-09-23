using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Laubrary.VisionMask.Editor
{
    /// Play-mode check for ANY scene that uses VisionMask: while the game runs, every masked sprite that is
    /// currently partly inside a cone is measured pixel by pixel (VisionMaskPixelProbe.Measure) against
    /// ground truth rebuilt from the live cones' raw geometry — angle, range, omni disc, and a physics
    /// linecast per pixel for occluders — and a capture of the given camera is saved. Writes
    /// <project>/Temp/VisionMaskProbe/live-report.txt and returns the report.
    public static class VisionMaskLiveCheck
    {
        [MenuItem("Laubrary/VisionMask/Measure Live Scene (Play mode)")]
        static void Menu() => Debug.Log(Run(Camera.main, "live"));

        public static string Run(Camera cam, string tag, int maxSprites = 12)
        {
            var sb = new StringBuilder();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/VisionMaskProbe"));
            Directory.CreateDirectory(dir);
            if (!Application.isPlaying) return "not playing";
            sb.AppendLine($"VisionMask live check '{tag}' t={Time.time:F2}s, cones={VisionMask.Cones.Count}, plane={VisionMask.Plane}");
            VisionMask.Publish();

            if (cam != null)
            {
                var shot = Capture(cam, 1280, 720);
                var path = Path.Combine(dir, tag + "-camera.png");
                File.WriteAllBytes(path, shot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(shot);
                sb.AppendLine("capture: " + path);
            }

            int measured = 0, passed = 0, whole = 0, none = 0;
            foreach (var masked in UnityEngine.Object.FindObjectsByType<VisionMasked>(FindObjectsSortMode.None))
            {
                foreach (var sr in masked.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!VisionMask.IsMaskMaterial(sr.sharedMaterial) || sr.sprite == null || !sr.enabled) continue;
                    if (masked.hiddenAlpha > 0f) continue;   // floors/ghosts: not an actor
                    var r = VisionMaskPixelProbe.Measure($"{tag} {sr.name}", sr, Truth, VisionMask.Plane, 128, dir, Tolerance(sr));
                    if (r.truthPx == 0) { none++; continue; }
                    if (r.truthPx == r.spritePx) { whole++; continue; }
                    measured++;
                    if (r.pass) passed++;
                    sb.AppendLine("  " + r);
                    if (measured >= maxSprites) break;
                }
                if (measured >= maxSprites) break;
            }
            sb.AppendLine($"partly-lit sprites measured: {measured} (pass {passed}); fully lit: {whole}; fully dark: {none}");
            File.WriteAllText(Path.Combine(dir, tag + "-report.txt"), sb.ToString());
            return sb.ToString();
        }

        /// Independent truth from the live cones' public fields: never the shader, never VisionMask.IsVisible.
        static bool Truth(Vector3 w)
        {
            bool xy = VisionMask.Plane == VisionPlane.XY;
            var p = xy ? new Vector2(w.x, w.y) : new Vector2(w.x, w.z);
            foreach (var k in VisionMask.Cones)
            {
                if (k == null || !k.isActiveAndEnabled) continue;
                var e3 = k.transform.position;
                var eye = xy ? new Vector2(e3.x, e3.y) : new Vector2(e3.x, e3.z);
                var d = p - eye;
                if (d.magnitude <= k.omniRadius) return true;
                if (d.magnitude > k.range) continue;
                if (Vector2.Angle(k.Facing, d) > k.angle * 0.5f) continue;
                if (k.occluders.value != 0)
                {
                    bool blocked = xy
                        ? Physics2D.Linecast(eye, p, k.occluders.value).collider != null
                        : Physics.Linecast(e3, new Vector3(w.x, e3.y, w.z), k.occluders.value, QueryTriggerInteraction.Ignore);
                    if (blocked) continue;
                }
                return true;
            }
            return false;
        }

        /// One shadow texel at the sprite's distance, in the probe's pixels (at least the 2px boundary).
        static int Tolerance(SpriteRenderer sr)
        {
            float worst = 0f;
            var b = sr.bounds;
            float pxPerUnit = 128f / (2f * Mathf.Max(b.extents.x, VisionMask.Plane == VisionPlane.XY ? b.extents.y : b.extents.z) * 1.15f);
            foreach (var k in VisionMask.Cones)
            {
                if (k == null || !k.isActiveAndEnabled || k.occluders.value == 0) continue;
                float dist = Vector3.Distance(k.transform.position, b.center) + b.extents.magnitude;
                worst = Mathf.Max(worst, k.angle * Mathf.Deg2Rad / Mathf.Max(1, k.rays) * dist * pxPerUnit);
            }
            return Mathf.Max(VisionMaskPixelProbe.BoundaryPx, Mathf.CeilToInt(worst));
        }

        static Texture2D Capture(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }
    }
}
