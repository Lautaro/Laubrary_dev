using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Zounds;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Bridges a <see cref="WeaponDef"/>'s muzzle effect to whatever <see cref="ICueSink"/> is available on
    /// its shooter — <c>OnEnable</c>/<c>OnDisable</c> register/unregister, which is what makes weapon-slot
    /// switching correct for free: deactivating a slot automatically drops its muzzle cue, no extra
    /// bookkeeping needed elsewhere. Falls back to the OLD behavior (fire on every successful shot, at a
    /// fixed <see cref="WeaponDef.muzzleOffset"/> position) when the shooter has no ICueSink at all — a
    /// plain SpriteView Zoe still gets a muzzle flash, just not one that tracks a live animated point.
    /// Also plays <see cref="WeaponDef.fireZoundName"/> (if any) on the SAME <see cref="ProjectileWeapon.Fired"/>
    /// event, once per successful shot — unconditionally, regardless of ICueSink presence, since
    /// ZoundEngine.PlayZound is name-only (no position to relay), so there's no positional-cue equivalent
    /// for audio the way there is for the muzzle VFX.
    /// </summary>
    [AddComponentMenu("Laubrary/Zoetrope/Weapon Muzzle Cue")]
    public class WeaponMuzzleCue : MonoBehaviour
    {
        public WeaponDef def;
        public ProjectileWeapon weapon;
        [Tooltip("Fallback muzzle position source when there's no ICueSink. Falls back to this transform if unset.")]
        public Transform muzzle;

        readonly string _key = System.Guid.NewGuid().ToString("N");
        ICueSink _sink;
        System.Action<Projectile> _fallbackHandler;
        System.Action<Projectile> _audioHandler;
        bool _configured;

        /// Call this right after AddComponent — OnEnable already ran before the caller could set fields the
        /// normal way, so this both sets them AND performs the initial registration immediately.
        public void Configure(WeaponDef def, ProjectileWeapon weapon, Transform muzzle)
        {
            this.def = def;
            this.weapon = weapon;
            this.muzzle = muzzle;
            _configured = true;
            if (isActiveAndEnabled) RegisterNow();
        }

        void OnEnable()
        {
            if (_configured) RegisterNow();
        }

        void OnDisable() => UnregisterNow();

        void RegisterNow()
        {
            if (def == null) return;

            if (def.muzzle != null && !def.muzzle.IsEmpty)
            {
                _sink = GetComponentInParent<ICueSink>();
                if (_sink != null)
                {
                    _sink.Register(_key, def.muzzleLayerId, def.muzzle, def.muzzleEventName);
                }
                else if (weapon != null)
                {
                    var m = muzzle != null ? muzzle : transform;
                    _fallbackHandler = _ => def.muzzle.Play(m.position);
                    weapon.Fired += _fallbackHandler;
                }
            }

            if (weapon != null && !string.IsNullOrEmpty(def.fireZoundName))
            {
                _audioHandler = _ => ZoundEngine.PlayZound(def.fireZoundName);
                weapon.Fired += _audioHandler;
            }
        }

        void UnregisterNow()
        {
            _sink?.Unregister(_key);
            _sink = null;
            if (_fallbackHandler != null && weapon != null)
            {
                weapon.Fired -= _fallbackHandler;
                _fallbackHandler = null;
            }
            if (_audioHandler != null && weapon != null)
            {
                weapon.Fired -= _audioHandler;
                _audioHandler = null;
            }
        }
    }
}
