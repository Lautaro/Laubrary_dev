using System;
using UnityEngine;
using Laubrary.PreviewKit;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// The one place member outlines are drawn onto pixels, shared by the rig's asset preview and the editor stage. Pixel buffers use Unity's layout
    /// (row 0 at the bottom); tags use sprite-local coordinates (origin top-left, y down), and these helpers convert.
    /// </summary>
    public static class GoreRigPreview
    {
        static readonly Color32 UpColour = new Color32(255, 255, 255, 255);
        static readonly Color32 ForwardColour = new Color32(255, 230, 60, 255);

        /// <summary>A fresh texture of the rig's first tagged frame (else its first sprite) with the member outlines drawn over it. Caller owns it; null when the rig has no sprites.</summary>
        public static Texture2D Render(GoreRig rig)
        {
            if (rig == null) return null;
            Sprite chosen = null, first = null;
            GoreFrameTags tags = null;
            foreach (var s in rig.EnumerateTargetSprites())
            {
                if (first == null) first = s;
                if (rig.TryGetFrame(s, out var ft)) { chosen = s; tags = ft; break; }
            }
            if (chosen == null) chosen = first;
            if (chosen == null) return null;

            var tex = PreviewTex.CropSprite(chosen);
            if (tex == null) return null;
            if (tags != null)
            {
                var px = tex.GetPixels32();
                DrawMembers(px, tex.width, tex.height, rig, tags);
                tex.SetPixels32(px);
                tex.Apply();
            }
            return tex;
        }

        /// <summary>Draws every present member of 'frame' (outline, up tick, forward tick) in its editor colour. 'mirrored' draws the tags as they would be on the flipped sprite.</summary>
        public static void DrawMembers(Color32[] px, int w, int h, GoreRig rig, GoreFrameTags frame, bool mirrored = false)
        {
            if (px == null || rig == null || frame == null || frame.members == null) return;
            for (int i = 0; i < frame.members.Count; i++)
            {
                var m = frame.members[i];
                if (m == null || !m.present) continue;
                Color c = i < rig.members.Count && rig.members[i] != null ? rig.members[i].editorColour : Color.white;
                var tag = mirrored ? GoreTagMath.Mirror(m.tag, w) : m.tag;
                DrawMember(px, w, h, tag, c);
            }
        }

        public static void DrawMember(Color32[] px, int w, int h, MemberTag tag, Color colour)
        {
            Color32 c = colour;
            double ux = Math.Cos(tag.angle), uy = Math.Sin(tag.angle);
            double ax = -uy, ay = ux;                                    // the "across" axis of the 2D outline
            double expo = 2.0 / Math.Max(0.5, tag.n);
            const int steps = 72;
            double prevX = 0, prevY = 0;
            for (int i = 0; i <= steps; i++)
            {
                double th = i * (2 * Math.PI / steps);
                double cs = Math.Cos(th), sn = Math.Sin(th);
                double across = tag.rx * Math.Sign(cs) * Math.Pow(Math.Abs(cs), expo);
                double along = tag.ry * Math.Sign(sn) * Math.Pow(Math.Abs(sn), expo);
                double x = tag.cx + ux * along + ax * across, y = tag.cy + uy * along + ay * across;
                if (i > 0) Line(px, w, h, prevX, prevY, x, y, c);
                prevX = x; prevY = y;
            }
            Line(px, w, h, tag.cx, tag.cy, tag.cx + tag.ux * tag.ry, tag.cy + tag.uy * tag.ry, UpColour);
            Line(px, w, h, tag.cx, tag.cy, tag.cx + tag.fx * tag.rx, tag.cy + tag.fy * tag.rx, ForwardColour);
        }

        static void Line(Color32[] px, int w, int h, double x0, double y0, double x1, double y1, Color32 c)
        {
            int ix0 = (int)Math.Floor(x0), iy0 = (int)Math.Floor(y0), ix1 = (int)Math.Floor(x1), iy1 = (int)Math.Floor(y1);
            int dx = Math.Abs(ix1 - ix0), dy = -Math.Abs(iy1 - iy0), sx = ix0 < ix1 ? 1 : -1, sy = iy0 < iy1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 4096; guard++)
            {
                if (ix0 >= 0 && iy0 >= 0 && ix0 < w && iy0 < h) px[(h - 1 - iy0) * w + ix0] = c;
                if (ix0 == ix1 && iy0 == iy1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; ix0 += sx; }
                if (e2 <= dx) { err += dx; iy0 += sy; }
            }
        }
    }
}
