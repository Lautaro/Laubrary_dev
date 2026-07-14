using UnityEngine;

namespace Laubrary.Combat2D
{
    /// <summary>
    /// How a spawned <see cref="Projectile"/> actually moves through space each frame — the pluggable "flight"
    /// half of a launch mode (paired with however the WEAPON resolved its aim/target; <see cref="Projectile"/>
    /// itself and the existing Collider2D/Hitbox/Hurtbox hit-detection stay unaware of which motion is in use).
    /// <see cref="PlanarMotion"/> is the default (today's straight-line 2D travel, unchanged behavior);
    /// <see cref="DepthMotion"/> travels toward a resolved world-space target through depth (rail-shooter
    /// style — AfterBurner/Space Harrier). Assigned via <c>[SerializeReference]</c> on <see cref="Projectile"/>
    /// and (per-ammo) on <c>AmmoDef</c>, matching the same pluggable pattern as <c>ICharacterView</c>/
    /// <c>IChunkAnimation</c>/<c>ICombatFx</c> elsewhere in Laubrary.
    /// </summary>
    public interface IProjectileMotion
    {
        /// Called once at launch. `direction` is always populated (normalized); `target`, if given, is an
        /// explicit world-space endpoint a target-seeking motion (e.g. DepthMotion) travels toward — direction-
        /// only motions (e.g. PlanarMotion) simply ignore it.
        void Init(Vector3 origin, Vector3 direction, float speed, Vector3? target);
        /// Advance by dt from `currentPosition`; returns the new world position.
        Vector3 Tick(Vector3 currentPosition, float dt);
    }

    /// <summary>The default: constant-velocity straight-line travel along a fixed direction — today's
    /// Projectile movement, extracted unchanged. No dependency on `target`.</summary>
    [System.Serializable]
    public class PlanarMotion : IProjectileMotion
    {
        Vector3 _dir;
        float _speed;

        public void Init(Vector3 origin, Vector3 direction, float speed, Vector3? target)
        {
            _dir = direction;
            _speed = speed;
        }

        public Vector3 Tick(Vector3 currentPosition, float dt) => currentPosition + _dir * (_speed * dt);
    }

    /// <summary>
    /// Rail-shooter style: travels from the launch origin toward a resolved world-space target at constant
    /// speed (AfterBurner/Space Harrier-style shots receding/growing into depth). Falls back to a point far
    /// along `direction` if no explicit `target` was given, so it degrades gracefully if paired with a
    /// direction-only firing call. Hit detection is untouched — Unity's Physics2D ignores Z, so a
    /// Collider2D-based Hitbox/Hurtbox still resolves correctly purely from this motion's XY position each
    /// frame, regardless of how far along Z it's travelled.
    /// </summary>
    [System.Serializable]
    public class DepthMotion : IProjectileMotion
    {
        Vector3 _origin, _target;
        float _speed, _traveled, _totalDistance;

        public void Init(Vector3 origin, Vector3 direction, float speed, Vector3? target)
        {
            _origin = origin;
            _target = target ?? origin + direction * 50f;
            _speed = speed;
            _traveled = 0f;
            _totalDistance = Vector3.Distance(_origin, _target);
        }

        public Vector3 Tick(Vector3 currentPosition, float dt)
        {
            _traveled += _speed * dt;
            float t = _totalDistance > 0.001f ? Mathf.Clamp01(_traveled / _totalDistance) : 1f;
            return Vector3.Lerp(_origin, _target, t);
        }
    }
}
