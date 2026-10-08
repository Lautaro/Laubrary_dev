// GoreLab shared data types. This file is the CONTRACT between the cut engine (Core), the wound recipes, the rig asset, the live component
// and the editor window. Plain C#, no UnityEngine types, so the engine can be compiled and tested outside the editor. Everything here is
// blittable or array based on purpose: the cut engine is written so that one frame's work can later be handed to a Burst job unchanged.
//
// Conventions (same as the web prototype this was ported from):
//   - Pixel space is SPRITE-LOCAL: origin top-left, x right, y DOWN. z points toward the viewer.
//   - A member is described in its own coordinates: the unit ball (or unit box) with axes east, up, forward.
//   - Colours are packed 0xAABBGGRR (the byte order of a Color32 on a little-endian machine). Alpha 0 means empty.
using System;
using System.Collections.Generic;

namespace Laubrary.GoreLab
{
    public enum MemberKind : byte { Ball = 0, Box = 1 }

    /// <summary>One body member (head ball, torso box...) on one drawn frame. Doubles on purpose: the engine reproduces the prototype's numbers.</summary>
    [Serializable]
    public struct MemberTag
    {
        public MemberKind kind;
        public double cx, cy;          // centre
        public double rx, ry, rz;      // half sizes across, along up, and (Box only) deep. A Ball uses rx for its depth too.
        public double n;               // squareness exponent of the 2D outline and, for a Box, of the 3D solid (2 = oval)
        public double angle;           // 2D angle of "up" on screen in radians (used only by the outline test)
        public double ux, uy, uz;      // up, unit
        public double fx, fy, fz;      // forward, unit and perpendicular to up
    }

    /// <summary>Built-in remover kinds. Game-defined kinds use ids from FirstCustom upward and register an IRemoverKind.</summary>
    public static class RemoverKinds
    {
        public const int Plane = 0;      // removes the side where N.p >= d (a slice)
        public const int Capsule = 1;    // removes a tunnel from A to B of radius r (a bullet hole, a pellet, one link of a knife cut)
        public const int FirstCustom = 128;
    }

    /// <summary>
    /// One thing a wound takes out of a member. Stored ONCE in the member's own unit coordinates, so the same remover lands correctly on every
    /// frame and direction. A swipe or a shot produces a list of these sharing a group number; later groups have higher numbers.
    /// Unmanaged on purpose (jobs).
    /// </summary>
    [Serializable]
    public struct GoreRemover
    {
        public int kind;               // RemoverKinds.* or a registered custom id
        public int group;              // one number per swipe / shot, increasing with time
        public int member;             // index into the rig's member list
        public int noiseIndex;         // position of this remover among the removers of its member (decides its ragged-edge noise stream)
        public double ax, ay, az;      // Capsule: end A (member coordinates)
        public double bx, by, bz;      // Capsule: end B
        public double r;               // Capsule: radius (member units)
        public double nx, ny, nz;      // Plane: unit normal
        public double d;               // Plane: offset
        public double backX, backY;    // screen direction (unit, or 0) blood and crumbs leave along; 0,0 = derive from the geometry
        public double p0, p1, p2, p3;  // spare slots for custom kinds
    }

    /// <summary>A sprite as a flat pixel array. Alpha 0 means empty. No Unity types so the engine runs anywhere.</summary>
    public sealed class GoreGrid
    {
        public readonly int w, h;
        public readonly uint[] px;
        public GoreGrid(int w, int h) { this.w = w; this.h = h; px = new uint[w * h]; }
        public GoreGrid(int w, int h, uint[] pixels) { this.w = w; this.h = h; px = pixels; }
        public GoreGrid Clone() { var g = new GoreGrid(w, h); Array.Copy(px, g.px, px.Length); return g; }
        public bool Solid(int x, int y) { return x >= 0 && y >= 0 && x < w && y < h && (px[y * w + x] >> 24) != 0; }
    }

    /// <summary>One member's data for one frame, as the engine receives it.</summary>
    public struct GoreMemberInput
    {
        public bool present;           // the frame has a tag for this member
        public bool skip;              // present but deliberately not cut on this frame (the prototype's "no head here")
        public MemberTag tag;
        public byte[] behind;          // per pixel of the grid (1 = body is behind the member here), or null
        public byte[] exempt;          // per pixel of the grid (1 = in front of the member, never cut), or null
    }

    /// <summary>Everything the engine needs to cut one frame.</summary>
    public struct GoreFrameInput
    {
        public GoreGrid grid;          // the frame placed on its canvas (for Unity: the sprite itself)
        public int sx, sy;             // where the sprite sits on that canvas (0,0 when the grid is the sprite)
        public GoreMemberInput[] members; // indexed like the rig's member list
    }

    /// <summary>The numbers of the cut that the engine reads (the prototype's cfg fields that matter).</summary>
    [Serializable]
    public struct GoreCutConfig
    {
        public int seed;               // cfg.seed
        public double jag;             // ragged edge amplitude (for a 6 px head)
        public double jagFreq;         // ragged edge frequency
        public bool bone;              // pale bone core in a slice
        public static GoreCutConfig Default() { return new GoreCutConfig { seed = 1, jag = 1.2, jagFreq = 0.45, bone = true }; }
    }

    /// <summary>The colours the wounds are made of. Fleshy is the default; a game can build others (gooey, mechanical...).</summary>
    [Serializable]
    public sealed class GoreStyle
    {
        public uint[] flesh = new uint[4];   // wound face colours (the cut picks index 0..3)
        public uint[] bone = new uint[2];    // bone core
        public uint[] blood = new uint[4];   // blood palette
        public uint crater;                  // dark red mixed into pellet craters
        public uint goreDark;                // what a "behind" pixel darkens toward
        public static GoreStyle Fleshy() { return GoreDefaults.FleshyStyle(); }
    }

    /// <summary>A point on a wound that bleeds: where, and the outward direction.</summary>
    public struct GoreBleedPoint { public int x, y; public float nx, ny; }

    /// <summary>A chunk that flew off as a body of its own (3 pixels or more).</summary>
    public sealed class GorePiece
    {
        public GoreGrid grid;          // cropped pixels
        public int x, y;               // top-left on the frame's grid
        public double outX, outY;      // unit screen direction it leaves along
        public List<GoreBleedPoint> edge = new List<GoreBleedPoint>();  // points on its torn edge (relative to the piece), for bleeding
    }

    /// <summary>A loose crumb (a chunk smaller than 3 pixels).</summary>
    public struct GoreGib { public float x, y; public uint colour; }

    /// <summary>The result of cutting one frame. Reuse one instance per worker to avoid allocating per cut.</summary>
    public sealed class GoreFrameResult
    {
        public uint[] body;                              // the frame with ALL removers applied (same size as the input grid)
        public byte[] chunkMask;                         // 1 where the NEWEST group changed the picture
        public int changed;                              // pixels where body differs from the input grid
        public int chunkCount;                           // number of 1s in chunkMask
        public double chunkCx, chunkCy;                  // centroid of the chunk (grid pixels)
        public double outX, outY;                        // direction the newest group's chunk leaves along
        public bool missing;                             // a needed member had no tag: nothing was cut, the frame is "not set up"
        public readonly List<GoreBleedPoint> bleed = new List<GoreBleedPoint>();   // every exposed wound pixel + torn rim (the stump bleeds from these)
        public readonly List<GorePiece> pieces = new List<GorePiece>();            // newest group's chunk, 3+ pixels, largest first
        public readonly List<GoreGib> gibs = new List<GoreGib>();                  // newest group's crumbs
    }

    /// <summary>Extension seam: a game adds its own KIND of remover (for example a burn or an acid pool) without editing GoreLab.</summary>
    public interface IRemoverKind
    {
        int KindId { get; }                                                   // >= RemoverKinds.FirstCustom
        /// <summary>The stretch of the line of sight (point = origin + z * dir, z in pixels) this remover takes out, or false if none.</summary>
        bool Interval(in GoreRemover r, in GoreLineOfSight los, out double lo, out double hi);
        /// <summary>Colour of the wound surface the remover exposes, at the member-space point q.</summary>
        uint WoundColour(in GoreRemover r, GoreStyle style, double qx, double qy, double qz);
    }

    /// <summary>The line of sight through one pixel, in member coordinates.</summary>
    public struct GoreLineOfSight
    {
        public double ox, oy, oz;      // origin at depth 0
        public double dx, dy, dz;      // direction per pixel of depth
    }

    /// <summary>
    /// A damage type: turns a request (a swipe or a shot) into removers. Slice, Cut, Bullet, Shotgun and Remove-head are recipes; a game
    /// adds its own by writing one [Serializable] class. Recipes are stored in the rig with [SerializeReference].
    /// </summary>
    public interface IWoundRecipe
    {
        string DisplayName { get; }
        /// <summary>Append the removers for this request. Must be deterministic for a given context (seed included).</summary>
        void Generate(WoundContext ctx, List<GoreRemover> into);
    }

    /// <summary>What a recipe is told about the hit. Coordinates are sprite-local pixels (origin top-left, y down) of the frame being shown.</summary>
    public sealed class WoundContext
    {
        public double p0x, p0y, p1x, p1y;     // the swipe / aim line, from where the user pressed to where they released
        public int group;                     // group number to stamp on every remover
        public int seed;                      // already mixed with a per-wound counter
        public GoreGrid grid;                 // the frame being shown (to find solid pixels)
        public GoreMemberInput[] members;     // tags of the frame being shown
        public bool[] targets;                // which members may be hit (all true = automatic)
        public bool[] sliceable;              // which members may be severed (null = all). A member that is not sliceable gets holes and dents, never a cut-off part.
        public GoreCutConfig cut;             // engine config
        public bool flipSide;                 // slice: choose the other side to fly
        /// <summary>Optional: how much of the animation shows a hole at this remover (0..1); used by recipes that pick a spot. May be null.</summary>
        public Func<GoreRemover, double> holeVisibility;
        public readonly List<GoreRemover> existing = new List<GoreRemover>();   // removers already on the body (for recipes that care)
        /// <summary>
        /// How many wounds this body has been dealt before this one (0 for the first). The caller sets seed = cut.seed * 131 + shotCounter, as the
        /// prototype did; the bullet also needs the counter on its own to pick which member takes the shot.
        /// </summary>
        public int shotCounter;
    }
}
