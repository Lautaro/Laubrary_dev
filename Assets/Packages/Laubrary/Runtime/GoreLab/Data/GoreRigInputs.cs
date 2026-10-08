namespace Laubrary.GoreLab
{
    /// <summary>Turns a frame's authored data into the engine's per-member input.</summary>
    public static class GoreRigInputs
    {
        /// <summary>
        /// Builds the engine input for one frame of a w x h sprite. With 'mirrored' the tags go through the engine's tag mirroring and the masks are
        /// flipped left to right, matching a horizontally flipped copy of the sprite. The result has 'memberCount' entries (default: as many as the
        /// frame has); members the frame does not carry come back as not present.
        /// </summary>
        public static GoreMemberInput[] Build(GoreFrameTags f, int w, int h, bool mirrored, int memberCount = -1)
        {
            int authored = f != null && f.members != null ? f.members.Count : 0;
            if (memberCount < 0) memberCount = authored;
            var result = new GoreMemberInput[memberCount];
            for (int i = 0; i < memberCount && i < authored; i++)
            {
                var m = f.members[i];
                if (m == null) continue;
                var input = new GoreMemberInput { present = m.present, skip = m.skip, tag = m.tag };
                if (mirrored) input.tag = GoreTagMath.Mirror(m.tag, w);
                input.behind = BuildMask(m.behind, w, h, mirrored);
                input.exempt = BuildMask(m.exempt, w, h, mirrored);
                result[i] = input;
            }
            return result;
        }

        // Null when nothing is painted, which the engine reads as "no mask" without scanning an empty array per pixel.
        static byte[] BuildMask(int[] indices, int w, int h, bool mirrored)
        {
            if (indices == null || indices.Length == 0 || w <= 0 || h <= 0) return null;
            int total = w * h;
            var mask = new byte[total];
            for (int k = 0; k < indices.Length; k++)
            {
                int idx = indices[k];
                if (idx < 0 || idx >= total) continue;
                if (mirrored)
                {
                    int x = idx % w, y = idx / w;
                    idx = y * w + (w - 1 - x);
                }
                mask[idx] = 1;
            }
            return mask;
        }
    }
}
