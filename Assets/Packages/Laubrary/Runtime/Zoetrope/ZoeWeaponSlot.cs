using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// One of a Zoe's equipped weapon slots: WHICH generic <see cref="WeaponDef"/> (stats/FX/ammo only, no
    /// knowledge of any particular character) plus THIS Zoe's own answer to "where does it attach, and where's
    /// its muzzle" — moved here from WeaponDef so the same weapon asset stays equippable by any character. A
    /// WeaponDef baking in one specific Zoe's part name / MetaLayer id meant it could only ever be correctly
    /// configured for ONE character's rig, defeating the whole point of a shared, reusable weapon asset — the
    /// real bug this class fixes (found 2026-08-17, mid-session, by the user: "the weapon has a hard reference
    /// to a Laumination — the torso of ProtoGuy").
    /// </summary>
    [System.Serializable]
    public class ZoeWeaponSlot
    {
        public WeaponDef weapon;

        [Tooltip("Which named composite body part (on THIS Zoe specifically) this weapon's slot attaches to — " +
                 "so its muzzle can read that PART's own animation. Empty = the character's root transform " +
                 "(the only option for a single-body Zoe, which has no named parts at all).")]
        public string attachToPartName = "";

        [Tooltip("Which MetaLayer (Point or Vector mode, on THIS Zoe's own animation) carries the muzzle's live " +
                 "position — drives BOTH the muzzle-flash VFX (via ICueSink) AND where a real projectile " +
                 "actually spawns from (via MuzzleTracker for Point, MuzzleVectorTracker for Vector — " +
                 "ZoeSpawner picks whichever mode is actually painted, automatically). Ignored when Muzzle " +
                 "Event Name below is set. Falls back to the weapon's own fixed muzzleOffset when nothing " +
                 "painted/reachable.")]
        public string muzzleLayerId = "Muzzle";

        [Tooltip("Alternative to Layer Id — the name of a FrameEvent (an authored pixel position, set via the " +
                 "Laumination Builder's pixel tool) that triggers the muzzle-flash VFX at its point instead. " +
                 "Takes priority over Layer Id for the VFX cue specifically; the real projectile spawn position " +
                 "still comes from Layer Id's tracker.")]
        public string muzzleEventName = "";
    }
}
