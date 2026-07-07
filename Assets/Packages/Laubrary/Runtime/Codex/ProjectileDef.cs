using UnityEngine;
using Laubrary.Chunks;

namespace Laubrary.Codex
{
    /// A composition recipe for a projectile's LOOK + flight + impact — the visual half of a shot. Colosseum's
    /// Projectile is the spine (movement / faction / damage / collision); a Weapon supplies the damage & speed. This
    /// adds the sprite (a spinning rocket, a bolt), an optional Chunks trail (jet stream / smoke out the back), and
    /// the impact VFX played where it lands. A spawner bridges this onto a runtime Colosseum Projectile.
    [CreateAssetMenu(menuName = "Laubrary/Codex/Projectile", fileName = "Projectile")]
    public class ProjectileDef : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Projectile";

        [Header("Look")]
        [Tooltip("Bullet / bolt / rocket sprite. TODO: allow a Zoe for animated projectiles.")]
        public Sprite sprite;
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
        [Tooltip("Optional Chunks trail emitted along the flight path (jet stream / smoke).")]
        public ChunkSpec trail;

        [Header("Impact")]
        [Tooltip("Blast + debris played where the projectile hits.")]
        public CombatVfx impact = new CombatVfx();

        // TODO(zounds): impact Zound ref.
    }
}
