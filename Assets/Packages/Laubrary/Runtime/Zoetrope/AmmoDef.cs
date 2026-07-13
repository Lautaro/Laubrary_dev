using UnityEngine;
using Laubrary.Chunks;

namespace Laubrary.Zoetrope
{
    /// A composition recipe for what a projectile IS: its visual, flight, and impact — the ammo half of a shot.
    /// Combat2D's Projectile is the spine (movement / faction / damage / collision); a WeaponDef supplies the
    /// fire stats and picks which AmmoDef(s) it fires. The visual is an IChunkAnimation reference — the same
    /// unifier Chunks already uses for "any visual, thrown as a physics object" (a Pyre blast or a Reel
    /// animation both implement it), so ammo doesn't invent a third visual-asset kind. A spawner bridges this
    /// onto a runtime Combat2D Projectile.
    [CreateAssetMenu(menuName = "Laubrary/Zoetrope/Ammo", fileName = "Ammo")]
    public class AmmoDef : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Ammo";

        [Header("Visual")]
        [Tooltip("A Pyre Blast Chunk Animation or a Reel Chunk Animation — anything implementing IChunkAnimation. " +
                 "Only its first frame is shown on the projectile today; per-projectile animation playback is a " +
                 "future add.")]
        public Object visual;
        public IChunkAnimation Visual => visual as IChunkAnimation;
        public float scale = 1f;
        [Tooltip("Spin the sprite while it flies (rotating rocket / grenade).")]
        public bool spin;
        [Tooltip("Spin rate in degrees/second (when Spin is on).")]
        public float spinSpeed = 360f;
        [Tooltip("Face the sprite along its travel direction (bolts/arrows).")]
        public bool faceTravel = false;

        [Header("Flight")]
        [Min(0.05f)] public float lifetime = 3f;
        [Tooltip("Pass through targets instead of dying on first hit.")]
        public bool pierce = false;
        [Tooltip("Not yet wired into flight — Combat2D's Projectile has no gravity/homing concept today.")]
        public float gravity = 0f;
        [Tooltip("Not yet wired into flight — Combat2D's Projectile has no gravity/homing concept today.")]
        public bool homing = false;

        [Header("Impact")]
        [Tooltip("Effect played where the projectile hits (pluggable — e.g. blast + debris via the Zoetrope.Pyre bridge).")]
        [SerializeReference] public ICombatFx impact;

        // TODO(bridge): a pluggable flight trail (was a Chunks trail on the old coupled Codex) belongs in a
        // presentation bridge as an ITrail-style seam, keeping this core Combat2D-only.
        // TODO(zounds): impact Zound ref.
    }
}
