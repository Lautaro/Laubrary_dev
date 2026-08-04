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
        Action<int> _armedFrameHandler;
        // When the armed clip will finish (Time.time), so an OnFrame-fired effect knows how much event is left
        // (a Body-SpriteFx card's Loop / RunAtEnd / PingPong bindings time themselves off that remainder).
        float _armedEndTime;
        bool _armedHasDuration;
        // Which OnFrame entries have already fired for the armed clip, so one cannot fire twice on a loop
        // and an unfired one can be flushed when the clip ends.
        readonly System.Collections.Generic.HashSet<FxEntry> _firedThisClip = new();

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
            float secs = EventSecondsOf(r);
            ctx.EventSecondsRemaining = secs;   // Immediate entries fire at the event's start — full length
            PlayBodyFx(r, secs);
            FireImmediate(r, ctx);
            TryArmClip(r, ctx, () => { FlushUnfiredFrameEntries(); Disarm(); HurtFinished?.Invoke(); });
        }

        /// Play a custom named reaction — the teleport/spawn/taunt surface. Returns whether anything played,
        /// so a caller can tell "no such event" from "played nothing visible" instead of guessing.
        ///
        /// Runs the SAME sequence OnHit and OnDeath run (context, body FX, immediate entries, then arm the
        /// clip), because a custom event that took a different path would drift from the fixed ones the first
        /// time either was touched — and the whole point of naming them is that they are the same kind of
        /// thing.
        ///
        /// ⚠️ A custom event has NO ATTACKER. HitDirection and Amount are zero and HitPosition falls back to
        /// the character's own position, so an effect that scales by Amount will scale by nothing here. That is
        /// the honest answer rather than an invented one; pass a DamageInfo yourself if the event really does
        /// carry a direction or a magnitude.
        public bool Raise(string id) => Raise(id, default);

        public bool Raise(string id, DamageInfo info)
        {
            var r = def != null ? def.EventNamed(id) : null;
            if (r == null) return false;

            var ctx = BuildContext(info);
            float secs = EventSecondsOf(r);
            ctx.EventSecondsRemaining = secs;
            PlayBodyFx(r, secs);
            FireImmediate(r, ctx);
            // Not Disarm()-ing on failure: a reaction with no clip still fired its Immediate entries above,
            // which is a legitimate custom event (a pure body flash with no animation of its own).
            TryArmClip(r, ctx, () => { FlushUnfiredFrameEntries(); Disarm(); EventFinished?.Invoke(id); });
            return true;
        }

        /// Raised when a custom event finishes its clip, with the id that finished.
        public event Action<string> EventFinished;
        void OnDeath(DamageInfo info)
        {
            var r = def != null ? def.death : null;
            var ctx = BuildContext(info);
            if (r != null)
            {
                float secs = EventSecondsOf(r);
                ctx.EventSecondsRemaining = secs;   // Immediate entries fire at the event's start — full length
                PlayBodyFx(r, secs);
                FireImmediate(r, ctx);
            }
            if (r == null || !TryArmClip(r, ctx, () => { FlushUnfiredFrameEntries(); Disarm(); DeathFinished?.Invoke(); }))
                DeathFinished?.Invoke();
        }

        /// The reaction clip's length in seconds, or 0 when unknown — no clip, no animated view, or a clip the
        /// view cannot measure (a zoned strip with no fixed end).
        float ClipSecondsOf(ReactionFx r)
        {
            if (r == null || string.IsNullOrEmpty(r.clip) || _view == null) return 0f;
            float s = _view.GetClipSeconds(r.clip);
            return s > 0f ? s : 0f;
        }

        /// How long the EVENT lasts — the clip's length put through the reaction's own duration model (N loops,
        /// or an explicit number of seconds). 0 when unknown, which makes every timed playback binding degrade
        /// to a single play, per its own tooltip.
        ///
        /// Everything riding the event is measured against this, never against a stack's own duration: a flash
        /// tied to a death animation should last exactly as long as the death animation, and re-timing the
        /// animation should re-time the flash with it.
        float EventSecondsOf(ReactionFx r) => r == null ? 0f : r.DurationSeconds(ClipSecondsOf(r));

        // Fill the typed EventContext from this damage event: the Zoe's live data (transform, health, the current
        // lauminary frame's renderer, the animated view for meta-points) + the event's typed in-params (hit
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
            float clipSecs = ClipSecondsOf(r);
            float eventSecs = r.DurationSeconds(clipSecs);
            _armedHasDuration = eventSecs > 0f;
            _armedEndTime = Time.time + eventSecs;
            _armedFrameHandler = HandleArmedFrame;
            _view.OnFrameEntered += _armedFrameHandler;
            PlayPass(r, clipSecs, onComplete);
            return true;
        }

        /// Play the clip once, and again for as long as the EVENT still has room for a whole pass — how a
        /// multi-loop event, or a fixed-seconds event longer than its clip, actually gets its length.
        ///
        /// Re-play is gated on at least HALF a clip remaining, so an event whose length is not a whole number
        /// of clips ends on a clean pass instead of a visible stutter of a few frames. A clip the view cannot
        /// measure has no remainder to reason about and simply plays once, as it always did.
        void PlayPass(ReactionFx r, float clipSecs, Action onComplete)
        {
            _view.PlayClip(r.clip, loop: false, onComplete: () =>
            {
                bool roomForAnother = ReferenceEquals(_armed, r) && _armedHasDuration && clipSecs > 0f &&
                                      _armedEndTime - Time.time > clipSecs * 0.5f;
                if (roomForAnother) PlayPass(r, clipSecs, onComplete);
                else onComplete?.Invoke();
            });
        }

        void Disarm()
        {
            if (_armedFrameHandler != null && _view != null) _view.OnFrameEntered -= _armedFrameHandler;
            _armed = null;
            _armedFrameHandler = null;
            _armedHasDuration = false;
            _firedThisClip.Clear();
        }

        // Only fires for the reaction currently armed (the clip actually playing) — a stray frame entry from
        // whatever plays AFTER (e.g. Idle resuming) can never spuriously match, since Disarm already ran.
        void HandleArmedFrame(int frame)
        {
            if (_armed?.fx == null || _armedCtx == null) return;
            // Re-stamp how much event is left at THIS fire moment, so a timed binding fired mid-clip times
            // itself off the remainder, not the whole clip.
            _armedCtx.EventSecondsRemaining = _armedHasDuration ? Mathf.Max(0f, _armedEndTime - Time.time) : 0f;
            foreach (var entry in _armed.fx)
            {
                // entry.frame is 1-based because that is how an animator counts frames; the lauminary is 0-based.
                if (entry == null || entry.trigger != FxTriggerType.OnFrame) continue;
                if (entry.frame - 1 != frame || _firedThisClip.Contains(entry)) continue;
                _firedThisClip.Add(entry);
                Fire(entry, _armedCtx);
            }
        }

        /// Fire any OnFrame entry whose frame never came round — the clip is shorter than the number someone
        /// typed, or it was cut short. Firing late beats an effect that silently does nothing, which is
        /// indistinguishable from a broken one.
        void FlushUnfiredFrameEntries()
        {
            if (_armed?.fx == null || _armedCtx == null) return;
            // The clip is over (or was cut short) — whatever fires now has no event time left.
            _armedCtx.EventSecondsRemaining = _armedHasDuration ? Mathf.Max(0f, _armedEndTime - Time.time) : 0f;
            foreach (var entry in _armed.fx)
            {
                if (entry == null || entry.trigger != FxTriggerType.OnFrame) continue;
                if (_firedThisClip.Contains(entry)) continue;
                _firedThisClip.Add(entry);
                Fire(entry, _armedCtx);
            }
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
        //
        // Runs over the EVENT's length, exactly like the Body-SpriteFx effect card does. This slot used to call
        // a bare Play(), which fell through to the stack's own fallback duration — so the same stack lasted one
        // length on an effect card and another here, and lengthening the animation left this one finishing early.
        void PlayBodyFx(ReactionFx r, float eventSeconds)
        {
            if (r == null || r.bodyFx == null) return;
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr == null) return;
            var filter = sr.GetComponent<SpriteFxFilter>();
            if (filter == null) filter = sr.gameObject.AddComponent<SpriteFxFilter>();
            filter.stack = r.bodyFx;
            if (eventSeconds > 0f) filter.Play(eventSeconds);
            else filter.Play();   // unknown event length — the stack's own duration is the honest last resort
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

            if (!entry.follow) { entry.fx.Apply(ctx); return; }

            // Follow re-homes a single spawned Transform each frame — an ICombatFx-only capability
            // (PlayFollowable). A non-ICombatFx effect has no Transform to hand back, so it just applies once.
            if (!(entry.fx is ICombatFx combat)) { entry.fx.Apply(ctx); return; }

            var t = combat.PlayFollowable(pos, ctx.DirectionDeg);
            if (t == null) return;   // this effect has nothing single/ongoing to follow (see PlayFollowable's own doc comment)
            var follower = t.gameObject.AddComponent<FxFollowTarget>();

            if (entry.placement == FxPlacementType.HitPosition && ctx.Transform != null)
            {
                // HitPosition + Follow STICKS the fixed hit point to the Zoe (task #4): capture where the hit landed
                // in the Zoe's OWN space, then re-project it each frame so the effect rides along as the Zoe moves /
                // turns, from the exact point the hit was detected. (TryResolvePosition can't — HitPosition is a fixed
                // world point with nothing to re-sample, which is why Follow used to be disabled for it.)
                var zoeT = ctx.Transform;
                Vector3 localHit = zoeT.InverseTransformPoint(pos);
                follower.Init(() => zoeT != null ? zoeT.TransformPoint(localHit) : t.position);
            }
            else
            {
                follower.Init(() => ctx.TryResolvePosition(entry.placement, entry.metaLayerId, out var p) ? (Vector3)p : t.position);
            }
        }
    }
}
