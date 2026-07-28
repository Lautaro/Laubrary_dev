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
        EventContext _armedCtx;
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
            var ctx = BuildContext(info);
            PlayBodyFx(r);
            FireImmediate(r, ctx);
            TryArmClip(r, ctx, () => { Disarm(); HurtFinished?.Invoke(); });
        }

        void OnDeath(DamageInfo info)
        {
            var r = def != null ? def.death : null;
            var ctx = BuildContext(info);
            if (r != null) { PlayBodyFx(r); FireImmediate(r, ctx); }
            if (r == null || !TryArmClip(r, ctx, () => { Disarm(); DeathFinished?.Invoke(); }))
                DeathFinished?.Invoke();
        }

        // Fill the typed EventContext from this damage event: the Zoe's live data (transform, health, the current
        // reel frame's renderer, the animated view for meta-points) + the event's typed in-params (hit
        // position/direction, amount). HitPosition bakes the old PointOf fallback (the Zoe's own position when the
        // hit recorded no point), so placement resolution reproduces the pre-generalization spawn points exactly.
        EventContext BuildContext(in DamageInfo info) => new EventContext
        {
            Transform = transform,
            Health = _health,
            Renderer = GetComponentInChildren<SpriteRenderer>(),
            View = _view,
            HitPosition = info.point != Vector2.zero ? info.point : (Vector2)transform.position,
            HitDirection = info.direction,
            Amount = info.amount,
        };

        // ── clip + frame-event arming ────────────────────────────────────────
        bool TryArmClip(ReactionFx r, EventContext ctx, Action onComplete)
        {
            if (r == null || string.IsNullOrEmpty(r.clip) || _view == null) return false;
            Disarm();
            _armed = r;
            _armedCtx = ctx;
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
            if (_armed?.fx == null || _armedCtx == null) return;
            foreach (var entry in _armed.fx)
                if (entry != null && entry.trigger == FxTriggerType.FrameEvent &&
                    string.Equals(entry.eventName, name, StringComparison.OrdinalIgnoreCase))
                    Fire(entry, _armedCtx);
        }

        void FireImmediate(ReactionFx r, EventContext ctx)
        {
            if (r.fx == null) return;
            foreach (var entry in r.fx)
                if (entry != null && entry.trigger == FxTriggerType.Immediate)
                    Fire(entry, ctx);
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
        void Fire(FxEntry entry, EventContext ctx)
        {
            if (!entry.enabled) return;   // muted (task #8) — kept in the list but never fires
            if (entry.fx == null || entry.fx.IsEmpty) return;
            if (!ctx.TryResolvePosition(entry.placement, entry.metaLayerId, out var pos)) return;

            // Stamp the resolved params the effect reads. For an ICombatFx these are exactly the (pos, dir) the
            // old entry.fx.Play(...) received, so the spawn point is byte-for-byte unchanged.
            ctx.Position = pos;
            ctx.DirectionDeg = ctx.ResolveDirectionDeg(entry.direction);
            ctx.Scalar = ctx.ResolveScalar(entry.scalar);

            bool canFollow = entry.follow && entry.placement != FxPlacementType.HitPosition;
            if (!canFollow) { entry.fx.Apply(ctx); return; }

            // Follow re-homes a single spawned Transform each frame — an ICombatFx-only capability
            // (PlayFollowable). A non-ICombatFx effect has no Transform to hand back, so it just applies once.
            if (!(entry.fx is ICombatFx combat)) { entry.fx.Apply(ctx); return; }

            var t = combat.PlayFollowable(pos, ctx.DirectionDeg);
            if (t == null) return;   // this effect has nothing single/ongoing to follow (see PlayFollowable's own doc comment)
            var follower = t.gameObject.AddComponent<FxFollowTarget>();
            follower.Init(() => ctx.TryResolvePosition(entry.placement, entry.metaLayerId, out var p) ? (Vector3)p : t.position);
        }
    }
}
