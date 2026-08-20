using System;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.SpriteFx;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Plays a Zoe's <see cref="ZoeEvent"/>s: the laumination, the body SpriteFx, the effect list triggered off
    /// that same laumination's frames, and the event's CONSEQUENCES (stun, invulnerability, death).
    ///
    /// <para><b>What changed with the uniform model.</b> This used to read <c>def.hit</c> / <c>def.death</c>
    /// directly — two fixed fields — which meant the death BEHAVIOUR (gating action, disposing the body,
    /// signalling a waiting respawn) belonged to the slot rather than to the event, so no second death could
    /// ever actually kill. It now RESOLVES which event answers an occasion (<see cref="Zoe.EventFor"/>) and
    /// applies whatever consequences that event declares. That resolution step is the one thing the uniform
    /// model makes harder, and it is deliberately in exactly one place.</para>
    ///
    /// <para><b>Auto-wiring is a convenience, not the model.</b> It still subscribes to Health so a Zoe with no
    /// bespoke game code works on its own — but only events that DECLARE a trigger answer those occasions.
    /// Whether a particular hit should make a character flinch is game logic (armour, poise, a boss phase), and
    /// game code says so by raising the event it wants.</para>
    /// </summary>
    [RequireComponent(typeof(Health))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, null, "ReactionFxPlayer")]
    public class ZoeEventPlayer : MonoBehaviour
    {
        public Zoe def;

        Health _health;
        IAnimatedView _view;
        ZoeState _state;
        AnimationArbiter _arbiter;

        /// The priority a triggered/raised event claims the <see cref="AnimationArbiter"/> at — high enough to
        /// preempt Locomotion's steady-state claim (<see cref="LocomotionAnimator.Priority"/> = 0) for the
        /// event's own duration.
        public const float ReactionPriority = 100f;

        // Resolved LAZILY, never cached in Awake: ZoeSpawner adds ZoeState AFTER this component (it has to,
        // since ZoeState listens to this one's DeathFinished), so an Awake-time GetComponent finds nothing and
        // every consequence silently no-ops. Exactly the bug LocomotionAnimator's own State property documents.
        ZoeState State => _state != null ? _state : (_state = GetComponent<ZoeState>());
        AnimationArbiter Arbiter => _arbiter != null ? _arbiter : (_arbiter = GetComponent<AnimationArbiter>());

        ZoeEvent _armed;
        EventContext _armedCtx;
        Action<int> _armedFrameHandler;
        // When the armed event will finish (Time.time), so an OnFrame-fired effect knows how much event is
        // left (a Body-SpriteFx card's Loop / RunAtEnd / PingPong bindings time themselves off that remainder).
        float _armedEndTime;
        bool _armedHasDuration;
        // Which OnFrame entries have already fired for the armed laumination, so one cannot fire twice on a
        // loop and an unfired one can be flushed when it ends.
        readonly System.Collections.Generic.HashSet<FxEntry> _firedThisClip = new();

        /// Fires once when a triggered non-killing event finishes playing. Never fires for a hit with no
        /// laumination configured (nothing was interrupted, so there is nothing to signal "finished").
        public event Action HurtFinished;
        /// Fires once per death, always — either when the death event finishes, or immediately if there was
        /// nothing to play, so a respawn timer downstream never hangs waiting.
        public event Action DeathFinished;
        /// Raised when any named event finishes, with the id that finished.
        public event Action<string> EventFinished;

        /// <summary>The event that actually answered the most recent death, or null if nothing did. Exposed
        /// because "which death was this?" is no longer answerable from the Zoe alone — it depends on the
        /// killing blow — and a consumer like Target Practice needs to know whether that particular death had
        /// anything to show before deciding to hide the body.</summary>
        public ZoeEvent LastDeathEvent { get; private set; }

        void Awake()
        {
            _health = GetComponent<Health>();
            _view = GetComponent<IAnimatedView>();
        }

        void OnEnable()
        {
            if (_health == null) _health = GetComponent<Health>();
            if (_health != null) { _health.Damaged += OnHit; _health.Died += OnDeath; }
            if (_view == null) _view = GetComponent<IAnimatedView>();
            if (_view != null)
            {
                _view.OnFrameEvent += OnCueFrameEvent;
                _view.OnMetaLayerReached += OnCueMetaLayer;
            }
        }

        void OnDisable()
        {
            if (_health != null) { _health.Damaged -= OnHit; _health.Died -= OnDeath; }
            if (_view != null)
            {
                _view.OnFrameEvent -= OnCueFrameEvent;
                _view.OnMetaLayerReached -= OnCueMetaLayer;
            }
            Disarm();
        }

        // ── cue triggers: the ANIMATION raising an event ──────────────────────
        // A cue fires off whatever laumination is playing, which is the one occasion an event's own effect
        // list cannot express (those only run while that event plays). Footstep dust, a cast sparkle.
        void OnCueFrameEvent(string name, int frame) => RaiseCued(name, isFrameEvent: true, at: null);

        void OnCueMetaLayer(string layerId, Vector2 worldPos) => RaiseCued(layerId, isFrameEvent: false, at: worldPos);

        /// <summary>Raise every event whose cue trigger matches this signal. The point is passed through as the
        /// event's hit position, so an effect placed At Hit Position lands ON the painted pixel — which is the
        /// whole reason a cue names a point rather than just a moment.</summary>
        void RaiseCued(string signal, bool isFrameEvent, Vector2? at)
        {
            if (def == null) return;
            string playing = _view != null ? _view.CurrentClip : null;
            var list = def.Events;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e?.trigger is not OnCueTrigger cue) continue;
                if (!cue.Matches(signal, isFrameEvent, playing)) continue;
                // An event whose own laumination carries the point it is cued on would re-raise itself
                // forever; the one already playing never re-triggers.
                if (ReferenceEquals(_armed, e)) continue;
                // A cue on a MetaLayer knows where it is; a FrameEvent cue asks the view for its pixel.
                Vector2 point = at ?? (_view != null && _view.TryGetMetaPoint(signal, out var p) ? p : (Vector2)transform.position);
                string id = e.id;
                Play(e, new DamageInfo(0f, point: point), () => { FlushUnfiredFrameEntries(); Disarm(); EventFinished?.Invoke(id); });
            }
        }

        // Start, not Awake: the spawner assigns `def` immediately after AddComponent, so Awake would run
        // against a null Zoe and every spawn event would be silently skipped.
        void Start() => RaiseSpawnEvents();

        void RaiseSpawnEvents()
        {
            if (def == null) return;
            foreach (var e in def.SpawnEvents())
            {
                string id = e.id;
                Play(e, default, () => { FlushUnfiredFrameEntries(); Disarm(); EventFinished?.Invoke(id); });
            }
        }

        void OnHit(DamageInfo info)
        {
            var e = def != null ? def.EventFor(info, killed: false) : null;
            if (e == null) return;   // nothing declares itself the answer to being damaged — a valid character
            Play(e, info, () => { FlushUnfiredFrameEntries(); Disarm(); HurtFinished?.Invoke(); });
        }

        void OnDeath(DamageInfo info)
        {
            var e = def != null ? def.EventFor(info, killed: true) : null;
            LastDeathEvent = e;

            // A Zoe whose events declare no death still has to DIE — the body must stop acting and be disposed
            // of, or a corpse stands there acting alive. So the state change happens whether or not anything
            // was authored to look at; only the presentation is optional.
            if (e == null)
            {
                State?.BeginDeath(DeathDisposal.Immediate, 0f, waitForEvent: false);
                DeathFinished?.Invoke();
                return;
            }

            if (!Play(e, info, () => { FlushUnfiredFrameEntries(); Disarm(); DeathFinished?.Invoke(); }))
                DeathFinished?.Invoke();   // nothing to wait on — signal immediately so a respawn never hangs
        }

        /// <summary>Play a named event — the teleport/spawn/taunt/fire surface, and the PRIMARY way events are
        /// meant to run. Returns whether anything played, so a caller can tell "no such event" from "played
        /// nothing visible" instead of guessing.</summary>
        ///
        /// ⚠️ A raised event has NO ATTACKER unless you pass one. HitDirection and Amount are zero and
        /// HitPosition falls back to the character's own position, so an effect that scales by Amount scales by
        /// nothing. That is the honest answer rather than an invented one; pass a DamageInfo yourself if the
        /// event really does carry a direction or a magnitude.
        public bool Raise(string id) => Raise(id, default);

        public bool Raise(string id, DamageInfo info)
        {
            var e = def != null ? def.EventNamed(id) : null;
            if (e == null) return false;
            Play(e, info, () => { FlushUnfiredFrameEntries(); Disarm(); EventFinished?.Invoke(id); });
            return true;
        }

        /// <summary>THE single path every event takes, whatever raised it — so a raised event can never drift
        /// from a triggered one. Returns whether a laumination was armed (i.e. whether onComplete will fire
        /// later rather than never).</summary>
        bool Play(ZoeEvent e, in DamageInfo info, Action onComplete)
        {
            var ctx = BuildContext(info);
            float secs = EventSecondsOf(e);
            ctx.EventSecondsRemaining = secs;   // Immediate entries fire at the start — full length
            ApplyConsequences(e);
            PlayBodyFx(e, secs);
            FireImmediate(e, ctx);
            // Not disarming on failure: an event with no laumination still fired its Immediate entries above,
            // which is a legitimate event (a pure body flash with no animation of its own).
            return TryArmClip(e, ctx, onComplete);
        }

        /// What playing this event DOES to the character. Every one of these was previously welded to a fixed
        /// slot; reading them off the event is what lets two deaths dispose differently.
        void ApplyConsequences(ZoeEvent e)
        {
            if (e.stunSeconds > 0f) State?.ApplyStun(e.stunSeconds);
            if (e.invulnerableSeconds > 0f && _health != null) _health.GrantInvulnerability(e.invulnerableSeconds);
            // Deliberately does NOT call Health.Kill(): that would re-enter Died and recurse. An event raised
            // by name with Ends character on gates acting and schedules disposal; if the game wants the health
            // value to reflect it too, it kills the character and lets the Died trigger resolve normally.
            if (e.endsCharacter)
                State?.BeginDeath(e.disposal, e.linger, waitForEvent: !string.IsNullOrEmpty(e.clip));
        }

        /// The event laumination's length in seconds, or 0 when unknown — no clip, no animated view, or a clip
        /// the view cannot measure (a zoned strip with no fixed end).
        float ClipSecondsOf(ZoeEvent e)
        {
            if (e == null || string.IsNullOrEmpty(e.clip) || _view == null) return 0f;
            float s = _view.GetClipSeconds(e.clip);
            return s > 0f ? s : 0f;
        }

        /// How long the EVENT lasts — the laumination's length put through the event's own duration model (N
        /// loops, or an explicit number of seconds). 0 when unknown, which makes every timed playback binding
        /// degrade to a single play, per its own tooltip.
        float EventSecondsOf(ZoeEvent e) => e == null ? 0f : e.DurationSeconds(ClipSecondsOf(e));

        // Fill the typed EventContext from this event: the Zoe's live data (transform, health, the current
        // lauminary frame's renderer, the animated view for meta-points) + the event's typed in-params.
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

        // ── laumination + frame-event arming ─────────────────────────────────
        bool TryArmClip(ZoeEvent e, EventContext ctx, Action onComplete)
        {
            if (e == null || string.IsNullOrEmpty(e.clip) || _view == null) return false;
            Disarm();
            _armed = e;
            _armedCtx = ctx;
            float clipSecs = ClipSecondsOf(e);
            float eventSecs = e.DurationSeconds(clipSecs);
            _armedHasDuration = eventSecs > 0f;
            _armedEndTime = Time.time + eventSecs;
            _armedFrameHandler = HandleArmedFrame;
            _view.OnFrameEntered += _armedFrameHandler;
            PlayPass(e, clipSecs, onComplete);
            return true;
        }

        /// Play the laumination once, and again for as long as the EVENT still has room for a whole pass — how
        /// a multi-loop event, or a fixed-seconds event longer than its laumination, actually gets its length.
        ///
        /// Re-play is gated on at least HALF a pass remaining, so an event whose length is not a whole number
        /// of passes ends cleanly instead of on a visible stutter of a few frames.
        void PlayPass(ZoeEvent e, float clipSecs, Action onComplete)
        {
            void OnPassComplete()
            {
                bool roomForAnother = ReferenceEquals(_armed, e) && _armedHasDuration && clipSecs > 0f &&
                                      _armedEndTime - Time.time > clipSecs * 0.5f;
                if (roomForAnother) PlayPass(e, clipSecs, onComplete);
                else onComplete?.Invoke();
            }

            // Claim the arbiter at reaction priority so this preempts Locomotion's steady-state clip for the
            // event's duration, rather than racing it for the view directly. No arbiter on this GameObject
            // (an older or hand-built rig) degrades to the direct call it always was.
            if (Arbiter != null) Arbiter.Play(this, ReactionPriority, e.clip, loop: false, onComplete: OnPassComplete);
            else _view.PlayClip(e.clip, loop: false, onComplete: OnPassComplete);
        }

        void Disarm()
        {
            if (_armedFrameHandler != null && _view != null) _view.OnFrameEntered -= _armedFrameHandler;
            _armed = null;
            _armedFrameHandler = null;
            _armedHasDuration = false;
            _firedThisClip.Clear();
            Arbiter?.Release(this);
        }

        // Only fires for the event currently armed, so a stray frame entry from whatever plays AFTER (e.g.
        // Idle resuming) can never spuriously match, since Disarm already ran.
        void HandleArmedFrame(int frame)
        {
            if (_armed?.fx == null || _armedCtx == null) return;
            // Re-stamp how much event is left at THIS fire moment, so a timed binding fired mid-play times
            // itself off the remainder, not the whole event.
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

        /// Fire any OnFrame entry whose frame never came round — the laumination is shorter than the number
        /// someone typed, or it was cut short. Firing late beats an effect that silently does nothing, which is
        /// indistinguishable from a broken one.
        void FlushUnfiredFrameEntries()
        {
            if (_armed?.fx == null || _armedCtx == null) return;
            _armedCtx.EventSecondsRemaining = _armedHasDuration ? Mathf.Max(0f, _armedEndTime - Time.time) : 0f;
            foreach (var entry in _armed.fx)
            {
                if (entry == null || entry.trigger != FxTriggerType.OnFrame) continue;
                if (_firedThisClip.Contains(entry)) continue;
                _firedThisClip.Add(entry);
                Fire(entry, _armedCtx);
            }
        }

        void FireImmediate(ZoeEvent e, EventContext ctx)
        {
            if (e.fx == null) return;
            foreach (var entry in e.fx)
                if (entry != null && entry.trigger == FxTriggerType.Immediate)
                    Fire(entry, ctx);
        }

        // Play the event's body SpriteFx on the character's OWN sprite the instant it fires — a hurt / death
        // flash / tint / dissolve riding on top of the live animation. Runs over the EVENT's length, so
        // lengthening the animation re-times the flash with it.
        void PlayBodyFx(ZoeEvent e, float eventSeconds)
        {
            if (e == null || e.bodyFx == null) return;
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr == null) return;
            var filter = sr.GetComponent<SpriteFxFilter>();
            if (filter == null) filter = sr.gameObject.AddComponent<SpriteFxFilter>();
            filter.stack = e.bodyFx;
            if (eventSeconds > 0f) filter.Play(eventSeconds);
            else filter.Play();   // unknown event length — the stack's own duration is the honest last resort
        }

        // ── spawning ──────────────────────────────────────────────────────────
        void Fire(FxEntry entry, EventContext ctx)
        {
            if (!entry.enabled) return;   // muted — kept in the list but never fires
            if (entry.fx == null || entry.fx.IsEmpty) return;
            if (!ctx.TryResolvePosition(entry.placement, entry.metaLayerId, out var pos)) return;

            ctx.Position = pos;
            ctx.DirectionDeg = ctx.ResolveDirectionDeg(entry.direction);
            ctx.Scalar = ctx.ResolveScalar(entry.scalar);

            if (!entry.follow) { entry.fx.Apply(ctx); return; }

            // Follow re-homes a single spawned Transform each frame — an ICombatFx-only capability. A
            // non-ICombatFx effect has no Transform to hand back, so it just applies once.
            if (!(entry.fx is ICombatFx combat)) { entry.fx.Apply(ctx); return; }

            var t = combat.PlayFollowable(pos, ctx.DirectionDeg);
            if (t == null) return;   // nothing single/ongoing to follow (see PlayFollowable's own doc comment)
            var follower = t.gameObject.AddComponent<FxFollowTarget>();

            if (entry.placement == FxPlacementType.HitPosition && ctx.Transform != null)
            {
                // HitPosition + Follow STICKS the fixed hit point to the Zoe: capture where the hit landed in
                // the Zoe's OWN space, then re-project it each frame so the effect rides along as the Zoe
                // moves / turns, from the exact point the hit was detected.
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
