// LatheSurfaceFill — a procedural 2D gradient (Linear, Radial, or a Linear gradient whose iso-color lines
// are bent by a curve — "Custom Linear") that bakes to a Texture2D at render time, same texProp pipeline as
// an assigned Texture2D. The "2D texture applied on a surface" idea, generated instead of imported.
//
// KNOWN LIMITATION (verified live, not theoretical): the sampling coordinate is LatheMeshData.BoxProject —
// a HARD switch between 3 axis-aligned projections (whichever the vertex normal points closest to), not a
// true triplanar BLEND. Linear and CustomLinear read fine on curved solids since a straight-ish gradient
// mostly agrees across the projection boundary; Radial visibly seams on a sphere — each of the 6 box faces
// gets its OWN local radial centre, so they don't line up where the faces meet. A proper fix blends the 3
// projections by how aligned the normal is to each axis; not attempted here — future work.
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum LatheFillKind { None, Linear, Radial, CustomLinear }

    [System.Serializable]
    public class LatheSurfaceFill
    {
        public LatheFillKind kind = LatheFillKind.None;
        public Gradient gradient = DefaultGradient();
        [Range(0f, 360f)] public float angle = 0f;
        public Vector2 center = new Vector2(0.5f, 0.5f);
        [Range(0.1f, 8f)] public float scale = 1f;
        // CustomLinear only: bends the gradient's iso-color lines. The curve is sampled by position along
        // the axis PERPENDICULAR to `angle` (0..1 across the tile) and its output offsets the gradient's
        // sample position along the axis itself — a straight gradient line becomes a wavy/curved one.
        [Range(0f, 1f)] public float warpAmount = 0.2f;
        public AnimationCurve warp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public LatheSurfaceFill Clone() => new LatheSurfaceFill
        {
            kind = kind,
            gradient = new Gradient { colorKeys = gradient.colorKeys, alphaKeys = gradient.alphaKeys, mode = gradient.mode },
            angle = angle,
            center = center,
            scale = scale,
            warpAmount = warpAmount,
            warp = new AnimationCurve(warp.keys) { preWrapMode = warp.preWrapMode, postWrapMode = warp.postWrapMode },
        };

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }

    public static class LatheFillBaker
    {
        public static Texture2D Bake(LatheSurfaceFill fill, int resolution)
        {
            if (fill == null || fill.kind == LatheFillKind.None || fill.gradient == null) return null;
            resolution = Mathf.Clamp(resolution, 8, 256);

            float rad = fill.angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 perp = new Vector2(-dir.y, dir.x);

            var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / resolution, (y + 0.5f) / resolution);
                    Vector2 offs = uv - fill.center;
                    float t;
                    switch (fill.kind)
                    {
                        case LatheFillKind.Radial:
                            t = offs.magnitude * 2f * fill.scale;
                            break;
                        case LatheFillKind.CustomLinear:
                        {
                            float alongPerp = Vector2.Dot(offs, perp) * fill.scale;
                            float bend = (fill.warp.Evaluate(Mathf.Clamp01(alongPerp + 0.5f)) - 0.5f) * fill.warpAmount;
                            t = Vector2.Dot(offs, dir) * fill.scale + bend + 0.5f;
                            break;
                        }
                        default: // Linear
                            t = Vector2.Dot(offs, dir) * fill.scale + 0.5f;
                            break;
                    }
                    px[y * resolution + x] = fill.gradient.Evaluate(Mathf.Repeat(t, 1f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
