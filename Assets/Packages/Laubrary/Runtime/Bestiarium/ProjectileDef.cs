using UnityEngine;

namespace Laubrary.Bestiarium
{
    /// A composition recipe for a projectile's LOOK + flight + impact — the visual half of a shot. Combat2D's
    /// Projectile is the spine (movement / faction / damage / collision); a Weapon supplies the damage & speed. This
    /// adds the sprite (a spinning rocket, a bolt) and the impact effect played where it lands. A spawner bridges
    /// this onto a runtime Combat2D Projectile.
    [CreateAssetMenu(menuName = "Laubrary/Bestiarium/Projectile", fileName = "Projectile")]
    public class ProjectileDef : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Projectile";

        [Header("Look")]
        [Tooltip("Bullet / bolt / rocket sprite.")]
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

        [Header("Impact")]
        [Tooltip("Effect played where the projectile hits (pluggable — e.g. blast + debris via the Bestiarium.Pyre bridge).")]
        [SerializeReference] public ICombatFx impact;

        // TODO(bridge): a pluggable flight trail (was a Chunks trail on the old coupled Codex) belongs in a
        // presentation bridge as an ITrail-style seam, keeping this core Combat2D-only.
        // TODO(zounds): impact Zound ref.
    }
}
