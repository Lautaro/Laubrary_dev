// LatheBaker — renders a LatheSpec's turntable into a real pixel-art sprite STRIP (one row, one tile per
// frame — standard Unity sprite-sheet input, ready for the Sprite Editor's grid slicer). This is the piece
// that turns Lathe's live 3D preview into something usable in a game, closing the loop back to the original
// ask: "expand the possibilities for procgen sprite animations."
using UnityEngine;

namespace Laubrary.Lathe.Editor
{
    public static class LatheBaker
    {
        // Render at 4× canvasSize then box-filter down — crisp flat-shaded edges without a soft-VFX bloom
        // chain (PyrePlayback3DPreview needs that for HDR fire packs; Lathe's solids don't).
        const int Supersample = 4;

        /// `orbitYaw`/`orbitPitch` are the window's CURRENT camera angle — the bake uses a FIXED camera (the
        /// angle you're already looking from) and only spins the subject, the correct sprite-sheet
        /// convention. Caller owns the returned Texture2D.
        public static Texture2D BakeStrip(LatheSpec spec, float orbitYaw, float orbitPitch)
        {
            if (spec == null || spec.solids == null || spec.solids.Count == 0) return null;
            int size = Mathf.Clamp(spec.canvasSize, 16, 256);
            int frames = Mathf.Max(1, spec.turntableFrames);
            float fitRadius = SolveFitRadius(spec);

            var strip = new Texture2D(size * frames, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            using (var pass = new LatheBakePass())
            {
                for (int f = 0; f < frames; f++)
                {
                    float turntableDeg = f / (float)frames * 360f;
                    var frameTex = pass.RenderFrame(spec, size, Supersample, turntableDeg, orbitYaw, orbitPitch, fitRadius);
                    strip.SetPixels32(f * size, 0, size, size, frameTex.GetPixels32());
                    Object.DestroyImmediate(frameTex);
                }
            }
            strip.Apply(false, false);
            return strip;
        }

        // A FIXED camera has to frame every frame of the spin, not just one — samples the world radius (from
        // the Y axis, through the origin) every enabled solid's mesh bounds reach at a few turntable angles.
        // Cheap approximation (4 samples), not an exact solve, matched to how PyrePlayback3DPreview
        // samples a few representative moments rather than solving the true extremum.
        static float SolveFitRadius(LatheSpec spec)
        {
            float maxR = 0.1f;
            foreach (float deg in new[] { 0f, 90f, 180f, 270f })
            {
                var turntable = Quaternion.Euler(0f, deg, 0f);
                foreach (var solid in spec.solids)
                {
                    if (solid == null || !solid.enabled) continue;
                    var mesh = solid.BuildMesh();
                    if (mesh == null) continue;
                    var matrix = solid.LocalToWorld(turntable);
                    foreach (var c in BoundsCorners(mesh.bounds))
                    {
                        Vector3 wp = matrix.MultiplyPoint3x4(c);
                        float r = new Vector2(wp.x, wp.z).magnitude + Mathf.Abs(wp.y) * 0.5f;
                        if (r > maxR) maxR = r;
                    }
                    Object.DestroyImmediate(mesh);
                }
            }
            return maxR * 1.2f;
        }

        static Vector3[] BoundsCorners(Bounds b)
        {
            var c = b.center; var e = b.extents;
            return new[]
            {
                c + new Vector3(e.x, e.y, e.z), c + new Vector3(-e.x, e.y, e.z),
                c + new Vector3(e.x, -e.y, e.z), c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z), c + new Vector3(-e.x, e.y, -e.z),
                c + new Vector3(e.x, -e.y, -e.z), c + new Vector3(-e.x, -e.y, -e.z),
            };
        }
    }
}
