using FOW;
using UnityEngine;

namespace Laubrary.Fov
{
    /// Point-visibility questions against the live fog world, for any system that wants a fog-aware
    /// decision (AI reactions, loot pings, spawn gating, audio ducking). Two DIFFERENT questions with
    /// two different costs — ask the one actually being asked:
    ///
    /// - IsRevealed = LIVE vision: is the point inside some revealer's current line of sight? Cost:
    ///   cheap CPU geometry per active revealer (distance and cone rejects run first), plus at most one
    ///   Physics2D raycast per revealer whose cone actually contains the point. Fine every frame for a
    ///   handful of askers; for hundreds of points per frame prefer the texture path below, or track a
    ///   cell set through FovReveal.
    ///
    /// - IsExplored = MEMORY: has the point been lit (fog-texture visibility at or above a threshold)?
    ///   Cost: an array read against the async CPU readback of the fog texture — the FovRoomWorld
    ///   recipe turns AsyncReadbackFogDataToCpu on for exactly this. Before the first readback lands
    ///   (a frame or two after world init) it falls back to a blocking 1-pixel ReadPixels: harmless as
    ///   a one-off, poison in a loop. Note the mode interaction: in LiveOnly reveal mode the memory
    ///   floor is 0, so explored ground decays back to nothing and the question degenerates to
    ///   "currently (or moments ago) lit".
    ///
    /// Per-CELL first-seen tracking (the CellFirstRevealed event, completion %) lives on FovReveal.
    public static class FovQuery
    {
        /// True when `world` is inside some revealer's LIVE line of sight. False when no fog world is
        /// alive. Cost: see the class comment — geometry per revealer, at most one raycast each.
        public static bool IsRevealed(Vector3 world)
        {
            return FogOfWarWorld.instance != null && FogOfWarWorld.TestPointVisibility(world);
        }

        /// True when the fog MEMORY at `world` has visibility at or above `threshold`. The default
        /// threshold sits below any sane memory floor, so "was ever seen" stays true on fully-decayed
        /// explored ground (PartialMemory/SnapFull); raise it toward 1 to ask "brightly lit right now"
        /// instead. False when no fog world is alive. Cost: see the class comment — an async-readback
        /// array read, with a blocking ReadPixels fallback only before the first readback exists.
        public static bool IsExplored(Vector3 world, float threshold = 0.05f)
        {
            return FogOfWarWorld.instance != null &&
                   FogOfWarWorld.SampleFogTextureColorAtPoint(world) >= threshold;
        }
    }
}
