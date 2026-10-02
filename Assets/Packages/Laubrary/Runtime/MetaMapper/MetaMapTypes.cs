using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.MetaMapper
{
    /// <summary>The frame of reference every coordinate in one map is expressed in. A map declares exactly ONE —
    /// two frames on one subject would make every query ambiguous — and it is set from the SUBJECT KIND when the
    /// map is created, never hand-edited afterwards. The map itself never knows about world space; converting is
    /// the binding helpers' job (<see cref="MetaMapSprite"/>, <see cref="MetaMapCells"/>).</summary>
    public enum MapSpace
    {
        /// <summary>Unit = one pixel of the subject's sprite rect, origin bottom-left. Launimator MetaFrame's
        /// exact convention, so painted masks re-anchor with no coordinate rewrite. Serves sprites, single
        /// tiles, any raster art.</summary>
        SpritePixels = 0,

        /// <summary>Unit = one grid cell, origin at the subject's own origin (clump anchor, prop origin, level
        /// (0,0)). Fractional values are legal and meaningful: (2.5, 1.33) is "cell (2,1), a third of the way
        /// up" — no second intra-cell offset field is needed. Serves clumps, props, level regions.</summary>
        GridCells = 1,
    }

    /// <summary>What a layer stores. Fixed at layer creation: changing kind with content authored invites
    /// nonsense (delete and recreate instead).</summary>
    public enum LayerKind
    {
        /// <summary>Dots — a muzzle, a centre, a loot anchor. A layer holds MANY of them; the LAYER ID, not the
        /// mark, is the semantic key.</summary>
        Points = 0,

        /// <summary>A position plus a facing. The position answers Point queries too (a Direction IS a point
        /// that also knows where it aims).</summary>
        Directions = 1,

        /// <summary>A painted byte grid (values 0..10) at map resolution — an area, a hitbox, a mouth. One kind
        /// for both "painted region" and "painted meta-mask": they were never two things.</summary>
        Mask = 2,
    }

    /// <summary>How a layer maps onto the map's frames. THIS IS AN AUTHORED CHOICE, NEVER AN INFERENCE
    /// (Amendment 2026-08-03): nothing in this module may auto-collapse a PerFrame track into Uniform because
    /// its entries currently look identical, nor silently auto-promote Uniform into PerFrame. A user who
    /// authors data on frame 3 and intends frames 4+ EMPTY must not have sharing guessed onto them.</summary>
    public enum FrameBinding
    {
        /// <summary>One entry serves every frame — the deliberate "this is true on every frame" opt-in, and the
        /// only sensible choice on a static (frameCount == 1) map. The frame index is ignored ENTIRELY.</summary>
        Uniform = 0,

        /// <summary>A track of entries index-aligned to frames. The default for a layer created on an ANIMATED
        /// map (frame-scoped is the safe default). A track SHORTER than frameCount means "empty beyond the
        /// end", never an error; an explicitly empty entry MID-track is equally legal and equally meaningful
        /// ("nothing happens on frame 4"), and must never be smoothed away.</summary>
        PerFrame = 1,
    }

    /// <summary>One authored marker: a position, optionally a facing, optionally a name. Positions are in MAP
    /// SPACE (pixels or fractional cells — see <see cref="MapSpace"/>), always subject-local.</summary>
    [System.Serializable]
    public class MetaMark
    {
        [Tooltip("Optional. \"\" for the common anonymous case — a shelf's nine loot anchors all mean the same " +
                 "thing and need no names. Name only the individuals a consumer looks up BY NAME (the PropSpot " +
                 "\"landing-pad\" case). Compared case-insensitively, like layer ids.")]
        public string name = "";

        [Tooltip("Position in MAP SPACE: pixels from the sprite rect's bottom-left, or (fractional) cells from " +
                 "the subject's origin. Never world space — the map is placement-blind by design.")]
        public Vector2 pos;

        [Tooltip("Facing, as a VECTOR not an angle (every transform a mark must survive — quarter-turns, mirror " +
                 "— is canonical vector math; angles would need their own mirror rules). Meaningful only on a " +
                 "Directions layer; zero elsewhere. Stored as authored, normalised by the queries on use.")]
        public Vector2 dir;

        public MetaMark Clone() => new MetaMark { name = name, pos = pos, dir = dir };

        /// <summary>Heal rather than throw: a deserialized mark with a null name is usable, not broken.</summary>
        public void Normalize() { if (name == null) name = ""; }
    }

    /// <summary>One frame's worth of ONE layer. Carries both <see cref="marks"/> and the mask fields, with the
    /// layer's <see cref="LayerKind"/> deciding which is live — the same mild slack Launimator's MetaLayer
    /// already tolerates (mode + frames on one type), chosen deliberately over [SerializeReference]
    /// polymorphism, whose serialization fragility this codebase has no precedent for.</summary>
    [System.Serializable]
    public class MetaEntry
    {
        [Tooltip("Free text, game-defined meaning (MetaFrame.param preserved verbatim). NOT counted as " +
                 "'content' by HasContent — a param without geometry annotates nothing spatial.")]
        public string param = "";

        [Tooltip("Points/Directions kinds. AUTHORED ORDER IS STABLE AND LOAD-BEARING: consumers that walk marks " +
                 "with a seeded System.Random (ShelfLoot) get the same result only while this order holds.")]
        public List<MetaMark> marks = new List<MetaMark>();

        [Tooltip("Mask kind: grid width in cells. PER ENTRY, not per map — a mask may be COARSER than the " +
                 "subject rect (refSize / maskW is the cell's size in map units).")]
        public int maskW;

        [Tooltip("Mask kind: grid height in cells. See maskW — per entry, and may be coarser than refSize.")]
        public int maskH;

        [Tooltip("Per-cell value 0..10 (0 = empty). Length maskW*maskH, ROW-MAJOR, BOTTOM-LEFT ORIGIN " +
                 "(MetaFrame's conventions, unchanged, so Launimator masks adopt without a rewrite). May be " +
                 "null = nothing painted yet; queries treat >0 as 'inside' and ignore intensity for now.")]
        public byte[] mask;

        /// <summary>True when the mask array is safe to index. The length check is not paranoia: an unpainted
        /// frame's array is null in memory, but Unity's serializer round-trips null to a ZERO-LENGTH array
        /// (never back to null), and a null-only guard let that through into an indexed loop and threw. Bug
        /// found and fixed once already in ZonedAnimationPlayer — the guard is inherited deliberately.</summary>
        public bool MaskUsable => mask != null && maskW > 0 && maskH > 0 && mask.Length >= maskW * maskH;

        public bool HasMarks
        {
            get
            {
                if (marks == null) return false;
                for (int i = 0; i < marks.Count; i++) if (marks[i] != null) return true;
                return false;
            }
        }

        public bool HasPaint
        {
            get
            {
                if (!MaskUsable) return false;
                for (int i = 0; i < maskW * maskH; i++) if (mask[i] > 0) return true;
                return false;
            }
        }

        /// <summary>"Is anything spatial authored here." An explicitly EMPTY entry mid-track answers false —
        /// and that is a real, authored answer, not a defect (see <see cref="FrameBinding"/>).</summary>
        public bool IsEmpty => !HasMarks && !HasPaint;

        public byte MaskGet(int x, int y)
            => (MaskUsable && x >= 0 && x < maskW && y >= 0 && y < maskH) ? mask[y * maskW + x] : (byte)0;

        public void MaskSet(int x, int y, int value)
        {
            EnsureMaskAlloc();
            if (x >= 0 && x < maskW && y >= 0 && y < maskH) mask[y * maskW + x] = (byte)Mathf.Clamp(value, 0, 10);
        }

        void EnsureMaskAlloc()
        {
            int need = Mathf.Max(0, maskW * maskH);
            if (mask == null || mask.Length != need) mask = new byte[need];
        }

        /// <summary>Resize the mask grid, preserving the drawing by NEAREST-NEIGHBOUR resampling, so a painted
        /// area survives a change in subject size. Lifted verbatim from
        /// <c>Runtime/Launimator/MetaLayer.cs</c> → <c>MetaFrame.EnsureSize</c> (its twin; keep them in step).
        /// Promoted here from "a Launimator behaviour" to a guarantee of the shared model.</summary>
        public void EnsureMaskSize(int nw, int nh)
        {
            nw = Mathf.Max(0, nw); nh = Mathf.Max(0, nh);
            if (nw == maskW && nh == maskH && mask != null && mask.Length == nw * nh) return;

            var old = mask; int ow = maskW, oh = maskH;
            maskW = nw; maskH = nh; mask = new byte[nw * nh];
            if (old == null || ow <= 0 || oh <= 0) return;
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int sx = (ow == nw) ? x : Mathf.Clamp(Mathf.FloorToInt((x + 0.5f) * ow / nw), 0, ow - 1);
                    int sy = (oh == nh) ? y : Mathf.Clamp(Mathf.FloorToInt((y + 0.5f) * oh / nh), 0, oh - 1);
                    if (sy * ow + sx < old.Length) mask[y * nw + x] = old[sy * ow + sx];
                }
        }

        public MetaEntry Clone()
        {
            var c = new MetaEntry
            {
                param = param,
                maskW = maskW,
                maskH = maskH,
                mask = mask != null ? (byte[])mask.Clone() : null,
                marks = new List<MetaMark>(marks != null ? marks.Count : 0),
            };
            if (marks != null)
                for (int i = 0; i < marks.Count; i++) c.marks.Add(marks[i] != null ? marks[i].Clone() : new MetaMark());
            return c;
        }

        /// <summary>Heal a deserialized entry into a usable one. Never throws, never discards authored geometry:
        /// a truncated mask array is grown (keeping what survived), an over-long one is trimmed, values above
        /// the 0..10 contract are clamped.</summary>
        public void Normalize()
        {
            if (param == null) param = "";
            if (marks == null) marks = new List<MetaMark>();
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                if (marks[i] == null) marks.RemoveAt(i);
                else marks[i].Normalize();
            }

            if (maskW < 0) maskW = 0;
            if (maskH < 0) maskH = 0;
            int need = maskW * maskH;
            if (need <= 0) { maskW = 0; maskH = 0; mask = null; return; }
            if (mask == null) return;                       // null = nothing painted yet; don't allocate on load
            if (mask.Length != need)
            {
                var fixedUp = new byte[need];
                System.Array.Copy(mask, fixedUp, Mathf.Min(mask.Length, need));
                mask = fixedUp;
            }
            for (int i = 0; i < mask.Length; i++) if (mask[i] > 10) mask[i] = 10;
        }
    }

    /// <summary>One named layer of spatial metadata. The layer id is the semantic key an API contract is written
    /// against ("muzzle", "loot"); the marks inside it are instances of that one meaning.</summary>
    [System.Serializable]
    public class MetaMapLayer
    {
        [Tooltip("THE SEMANTIC KEY the reading code looks up — compared CASE-INSENSITIVELY (MetaLayer " +
                 "precedent). A layer that merely EXISTS is already an answer: HasLayer(\"LootShelf\") is how a " +
                 "procgen mutator asks 'is this thing a loot shelf?'. Consumers should publish theirs as a " +
                 "const string rather than retyping the literal.")]
        public string id = "layer";

        [Tooltip("What this layer stores. FIXED AT CREATION — changing kind with content already authored " +
                 "invites nonsense; delete the layer and recreate it instead.")]
        public LayerKind kind = LayerKind.Points;

        [Tooltip("ONE display colour for the whole layer; a mask cell's 0..10 value ramps its BRIGHTNESS " +
                 "(see MetaPalette.CellColor), it does not pick a different hue. Defaults come from " +
                 "MetaPalette.Colors so two layers rarely look alike.")]
        public Color color = MetaPalette.Colors[0];

        [Tooltip("Mask layers only: an AUTHORING hint, never a data rule — it makes the paint tool behave " +
                 "single-cell (clear the previous cell before painting the next). Preserved verbatim from " +
                 "MetaLayerMode.Point, which never meant anything more than this either.")]
        public bool pointHint;

        [Tooltip("AUTHORED, never inferred (Amendment 2026-08-03). Uniform = one entry for all frames; " +
                 "PerFrame = one entry per frame. Nothing may collapse or promote this automatically — a " +
                 "PerFrame track whose entries happen to match is still PerFrame.")]
        public FrameBinding binding = FrameBinding.Uniform;

        [Tooltip("Live only when binding == Uniform. Cleared (not kept as a shadow copy) when the layer is " +
                 "expanded to PerFrame, so serialized data never carries two conflicting truths.")]
        public MetaEntry uniform = new MetaEntry();

        [Tooltip("Live only when binding == PerFrame; index-aligned to frames. SHORTER than frameCount is " +
                 "legal and means 'empty beyond the end' (Launimator's exact tolerance). An empty entry in the " +
                 "MIDDLE is equally legal and means 'nothing on this frame'.")]
        public List<MetaEntry> track = new List<MetaEntry>();

        /// <summary>THE ONE PLACE the frame binding is interpreted. Every mark/mask lookup in the module goes
        /// through here (via <see cref="MetaMapData.EntryAt(MetaMapLayer,int)"/>), which is what lets a future
        /// third binding (sparse keyframes-with-hold) land without touching a single consumer.
        /// Returns null for "nothing authored for this frame" — never throws, never substitutes.</summary>
        public MetaEntry EntryAt(int frame)
        {
            switch (binding)
            {
                case FrameBinding.Uniform:
                    return uniform;                                     // frame ignored ENTIRELY, by design
                case FrameBinding.PerFrame:
                    if (track == null || frame < 0 || frame >= track.Count) return null;  // short track = empty past the end
                    return track[frame];                                // may legitimately be an EMPTY entry
                default:
                    return null;
            }
        }

        /// <summary>Pad the track up to <paramref name="frameCount"/> with EMPTY entries. Never trims: dropping
        /// the tail would silently destroy authored frames, and an over-long track is harmless (it simply reads
        /// as unused). PerFrame layers only.</summary>
        public void EnsureTrack(int frameCount)
        {
            if (track == null) track = new List<MetaEntry>();
            for (int i = 0; i < track.Count; i++) if (track[i] == null) track[i] = new MetaEntry();
            while (track.Count < Mathf.Max(0, frameCount)) track.Add(new MetaEntry());
        }

        /// <summary>Uniform → PerFrame. LOSSLESS: the single entry is COPIED to every frame, so the layer says
        /// exactly what it said before. Already-PerFrame layers are just padded. The uniform slot is cleared
        /// afterwards so the asset never carries a stale second copy.</summary>
        public void ExpandToPerFrame(int frameCount)
        {
            if (binding == FrameBinding.PerFrame) { EnsureTrack(frameCount); return; }
            int n = Mathf.Max(1, frameCount);
            var src = uniform ?? new MetaEntry();
            var expanded = new List<MetaEntry>(n);
            for (int i = 0; i < n; i++) expanded.Add(src.Clone());
            track = expanded;
            uniform = new MetaEntry();
            binding = FrameBinding.PerFrame;
        }

        /// <summary>PerFrame → Uniform. DESTRUCTIVE by nature — every frame but <paramref name="sourceFrame"/>
        /// is discarded — which is exactly why the caller must NAME the winning frame instead of the system
        /// guessing (Amendment 2026-08-03 (3): editors warn before calling this). Already-Uniform layers are
        /// untouched.</summary>
        public void CollapseToUniform(int sourceFrame)
        {
            if (binding == FrameBinding.Uniform) return;
            var src = (track != null && sourceFrame >= 0 && sourceFrame < track.Count) ? track[sourceFrame] : null;
            uniform = src != null ? src.Clone() : new MetaEntry();
            track = new List<MetaEntry>();
            binding = FrameBinding.Uniform;
        }

        public MetaMapLayer Clone()
        {
            var c = new MetaMapLayer
            {
                id = id,
                kind = kind,
                color = color,
                pointHint = pointHint,
                binding = binding,
                uniform = uniform != null ? uniform.Clone() : new MetaEntry(),
                track = new List<MetaEntry>(track != null ? track.Count : 0),
            };
            if (track != null)
                for (int i = 0; i < track.Count; i++) c.track.Add(track[i] != null ? track[i].Clone() : new MetaEntry());
            return c;
        }

        /// <summary>Heal a deserialized layer. Never throws.</summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(id)) id = "layer";
            if (color.a <= 0f)
            {
                // A fully transparent layer colour reads as a bug in every view; a default-constructed Color is
                // exactly that. Restore visibility without inventing a hue the author didn't pick.
                color = (color.r + color.g + color.b) <= 0f
                    ? MetaPalette.Colors[0]
                    : new Color(color.r, color.g, color.b, 1f);
            }
            if (uniform == null) uniform = new MetaEntry();
            if (track == null) track = new List<MetaEntry>();
            uniform.Normalize();
            for (int i = 0; i < track.Count; i++)
            {
                if (track[i] == null) track[i] = new MetaEntry();
                track[i].Normalize();
            }
        }
    }

    /// <summary>The shared layer palette and value ramp. Twin of <c>MetaLayer.Palette</c> / <c>CellColor</c> in
    /// <c>Runtime/Launimator/MetaLayer.cs</c> — duplicated (not referenced) because MetaMapper references
    /// NOTHING by design; when Launimator adopts the model it deletes its copy and points here.</summary>
    public static class MetaPalette
    {
        /// <summary>The 5 predetermined layer colours an authoring palette offers.</summary>
        public static readonly Color[] Colors =
        {
            new Color(1f,    0.30f, 0.30f, 1f),  // red
            new Color(0.40f, 0.85f, 0.45f, 1f),  // green
            new Color(0.40f, 0.65f, 1f,    1f),  // blue
            new Color(1f,    0.82f, 0.28f, 1f),  // amber
            new Color(0.85f, 0.45f, 1f,    1f),  // violet
        };

        /// <summary>Display colour for a mask cell value (0 = fully transparent). Value 5 = the layer colour;
        /// values ramp the BRIGHTNESS so they read apart — 1 = almost black, 5 = the colour, 10 = almost white.
        /// Alpha stays the layer colour's alpha.</summary>
        public static Color CellColor(Color baseColor, int value)
        {
            if (value <= 0) return new Color(0f, 0f, 0f, 0f);
            float t = Mathf.Clamp(value, 1, 10);
            Color rgb = t <= 5f
                ? Color.Lerp(new Color(0.06f, 0.06f, 0.06f), baseColor, (t - 1f) / 4f)
                : Color.Lerp(baseColor, new Color(0.96f, 0.96f, 0.96f), (t - 5f) / 5f);
            rgb.a = baseColor.a;
            return rgb;
        }
    }

    /// <summary>One connected component of a mask, in MAP SPACE (except <see cref="cells"/>, which are mask-grid
    /// coordinates — the grid may be coarser than the map). Reused in place by
    /// <see cref="MetaMapData.GetMaskBlobs"/> so a repeat call on the same buffer allocates nothing new.</summary>
    public class MaskBlob
    {
        /// <summary>Every painted cell of this blob, in MASK-GRID coords (bottom-left origin), scan order.
        /// Convert with <see cref="MetaMapData.MaskCellCenterToMap"/>.</summary>
        public readonly List<Vector2Int> cells = new List<Vector2Int>();

        /// <summary>Value-weighted centroid, MAP SPACE — the same math as the whole-layer centroid, applied to
        /// this blob alone.</summary>
        public Vector2 centroid;

        /// <summary>Painted-cell bounding box, MAP SPACE (a cell contributes its full extent, not its centre).</summary>
        public Rect bounds;

        /// <summary>Highest cell value in the blob (1..10).</summary>
        public int peakValue;

        public int CellCount => cells.Count;

        internal void Reset()
        {
            cells.Clear();
            centroid = default;
            bounds = default;
            peakValue = 0;
        }
    }
}
