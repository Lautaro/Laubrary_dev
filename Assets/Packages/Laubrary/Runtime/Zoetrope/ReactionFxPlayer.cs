using System;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.SpriteFx;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Bridges Colosseum's damage events to a Zoe's <see cref="ReactionFx"/> — the single place that plays the
    /// Hurt/Death clip AND the FX triggered off that same clip's frame events, replacing the old separate
    /// <c>CombatPresenter</c> (VFX-only) + <c>HitReactionPlayer</c> (clip-only) split. They had to become one
    /// component because Clip is now shared: "start playing the Hurt clip" and "arm this reaction's FX list
    /// against that same clip" happen together, atomically, from here.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class ReactionFxPlayer : MonoBehaviour
    {
        public Zoe def;

        Health _health;
        IAnimatedView _view;

        ReactionFx _armed;
        DamageInfo _armedInfo;
        Action<string, int> _armedHandler;

        /// Fires once when a triggered Hurt clip finishes playing. Never fires for a hit with no Hurt clip
        /// configured (nothing was interrupted, so there's nothing to signal "finished").
        public event Action HurtFinished;
        /// Fires once per death, always — either when the Death clip finishes, or immediately if there was no
        /// clip to play (or no IAnimatedView at all), so a respawn timer downstream never hangs waiting.
        public event Action DeathFinished;

        void Awake()
        {
            _health = GetComponent<Health>();
            _view = GetComponent<IAnimatedView>();
        }

        void OnEnable()
        {
            if (_health == null) _health = GetComponent<Health>();
            if (_health != null) { _health.Damaged += OnHit; _health.Died += OnDeath; }
        }

        void OnDisable()
        {
            if (_health != null) { _health.Damaged -= OnHit; _health.Died -= OnDeath; }
            Disarm();
        }

        void OnHit(DamageInfo info)
        {
            var r = def != null ? def.hit : null;
            if (r == null) return;
            PlayBodyFx(r);
            FireImmediate(r, info);
            TryArmClip(r, info, () => { Disarm(); HurtFinished?.Invoke(); });
        }

        void OnDeath(DamageInfo info)
        {
            var r = def != null ? def.death : null;
            if (r != null) { PlayBodyFx(r); FireImmediate(r, info); }
            if (r == null || !TryArmClip(r, info, () => { Disarm(); DeathFinished?.Invoke(); }))
                DeathFinished?.Invoke();
        }

        // ── clip + frame-event arming ────────────────────────────────────────
        bool TryArmClip(ReactionFx r, DamageInfo info, Action onComplete)
        {
            if (r == null || string.IsNullOrEmpty(r.clip) || _view == null) return false;
            Disarm();
            _armed = r;
            _armedInfo = info;
            _armedHandler = HandleArmedFrameEvent;
            _view.OnFrameEvent += _armedHandler;
            _view.PlayClip(r.clip, loop: false, onComplete: onComplete);
            return true;
        }

        void Disarm()
        {
            if (_armedHandler != null && _view != null) _view.OnFrameEvent -= _armedHandler;
            _armed = null;
            _armedHandler = null;
        }

        // Only fires for the reaction currently armed (the clip actually playing) — a stray frame event from
        // whatever plays AFTER (e.g. Idle resuming) can never spuriously match, since Disarm already ran by then.
        void HandleArmedFrameEvent(string name, int frame)
        {
            if (_armed?.fx == null) return;
            foreach (var entry in _armed.fx)
                if (entry != null && entry.trigger == FxTriggerType.FrameEvent &&
                    string.Equals(entry.eventName, name, StringComparison.OrdinalIgnoreCase))
                    Fire(entry, _armedInfo);
        }

        void FireImmediate(ReactionFx r, DamageInfo info)
        {
            if (r.fx == null) return;
            foreach (var entry in r.fx)
                if (entry != null && entry.trigger == FxTriggerType.Immediate)
                    Fire(entry, info);
        }

        // Play the reaction's body SpriteFx on the character's OWN sprite the instant the reaction fires — a hurt /
        // death flash / tint / dissolve that rides on top of the live animation. Ensures a SpriteFxFilter on the
        // body's SpriteRenderer (the same renderer TargetPosition placement uses) and points it at the reaction's
        // Stack. Layering stays correct: Zoetrope depends DOWN on SpriteFx; SpriteFx never references Zoetrope.
        void PlayBodyFx(ReactionFx r)
        {
            if (r == null || r.bodyFx == null) return;
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr == null) return;
            var filter = sr.GetComponent<SpriteFxFilter>();
            if (filter == null) filter = sr.gameObject.AddComponent<SpriteFxFilter>();
            filter.stack = r.bodyFx;
            filter.Play();
        }

        // ── spawning ──────────────────────────────────────────────────────────
        void Fire(FxEntry entry, DamageInfo info)
        {
            if (entry.fx == null || entry.fx.IsEmpty) return;
            if (!TryResolvePlacement(entry, info, out var pos)) return;
            float dir = DirOf(info);

            bool canFollow = entry.follow && entry.placement != FxPlacementType.HitPosition;
            if (!canFollow) { entry.fx.Play(pos, dir); return; }

            var t = entry.fx.PlayFollowable(pos, dir);
            if (t == null) return;   // this effect has nothing single/ongoing to follow (see PlayFollowable's own doc comment)
            var follower = t.gameObject.AddComponent<FxFollowTarget>();
            follower.Init(() => TryResolvePlacement(entry, info, out var p) ? (Vector3)p : t.position);
        }

        bool TryResolvePlacement(FxEntry entry, DamageInfo info, out Vector2 pos)
        {
            switch (entry.placement)
            {
                case FxPlacementType.HitPosition:
                    pos = PointOf(info);
                    return true;
                case FxPlacementType.TargetOrigin:
                    pos = transform.position;
                    return true;
                case FxPlacementType.TargetPosition:
                    var sr = GetComponentInChildren<SpriteRenderer>();
                    pos = sr != null ? (Vector2)sr.bounds.center : (Vector2)transform.position;
                    return true;
                case FxPlacementType.MetaPoint:
                    if (_view != null && _view.TryGetMetaPoint(entry.metaLayerId, out var w)) { pos = w; return true; }
                    pos = default;
                    return false;
                default:
                    pos = default;
                    return false;
            }
        }

        // Fall back to the character's own position when the hit didn't record a point.
        Vector2 PointOf(in DamageInfo info) => info.point != Vector2.zero ? info.point : (Vector2)transform.position;

        static float DirOf(in DamageInfo info) =>
            info.direction.sqrMagnitude > 1e-6f ? Mathf.Atan2(info.direction.y, info.direction.x) * Mathf.Rad2Deg : float.NaN;
    }
}
