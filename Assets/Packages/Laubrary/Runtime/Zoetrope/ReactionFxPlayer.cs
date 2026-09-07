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
        AnimationArbiter _arbiter;

        /// Resolved lazily, never cached at Awake: ZoeSpawner adds the arbiter while building the view, and a
        /// Mirage-added ReactionFxPlayer can land on a GameObject at any point after that. Same spawn-ordering
        /// trap LocomotionAnimator.State exists to dodge.
        AnimationArbiter Arbiter => _arbiter != null ? _arbiter : (_arbiter = GetComponent<AnimationArbiter>());

        /// The view is resolved the SAME lazy way, and for the same reason — a cached-at-Awake reference was
        /// simply wrong for anything added out of order. It is also re-asked every time rather than trusted
        /// once because a null answer here is silent and total: with no view, every clip is "NoClip", the
        /// frame handler is never subscribed, and every OnFrame effect on every reaction quietly does nothing.
        IAnimatedView AnimView => _view != null ? _view : (_view = GetComponent<IAnimatedView>());

        ZoeState _state;

        /// Resolved lazily for the SAME spawn-ordering reason as Arbiter and AnimView above, and here it is
        /// not a theoretical risk: ZoeSpawner adds this component BEFORE ZoeState (so it can wait on our
        /// DeathFinished), so an Awake-time GetComponent would cache null forever and every stun a reaction
        /// asked for would silently do nothing.
        ZoeState State => _state != null ? _state : (_state = GetComponent<ZoeState>());

        IReactionLookAnswerer _lookAnswerer;
        bool _lookAnswererSearched;

        /// The optional game-supplied answerer to "which hurt/death look?" — discovered via
        /// GetComponentInParent, same convention <see cref="IFireStateSource"/> already uses (and, like it,
        /// most characters legitimately have none, so the search runs once and a miss is remembered rather
        /// than repeated on every hit).
        IReactionLookAnswerer LookAnswerer
        {
            get
            {
                if (!_lookAnswererSearched)
                {
                    _lookAnswerer = GetComponentInParent<IReactionLookAnswerer>();
                    _lookAnswererSearched = true;
                }
                return _lookAnswerer;
            }
        }

        /// State names already complained about, so a full-auto weapon asking for a name nobody declared logs
        /// ONCE and not sixty lines a second. Never cleared: the point is to say it, not to keep saying it,
        /// and a name that was missing a second ago is still missing now. Case-insensitive to match
        /// <see cref="Zoe.EventNamed"/> — "Fire" and "fire" are one name, so they are one complaint.
        readonly System.Collections.Generic.HashSet<string> _warnedMissing =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ReactionFx _armed;
        EventContext _armedCtx;
        // The FxOverride slot name this armed reaction was raised with, if any — read by every Fire() call
        // for its whole lifetime (Immediate entries at arm time, OnFrame entries as they fire, and the flush
        // when it ends), so a reaction picks one override for its ENTIRE play, not per-effect.
        string _armedOverrideName;
        Action<int> _armedFrameHandler;
        // When the armed clip will finish (Time.time), so an OnFrame-fired effect knows how much event is left
        // (a Body-SpriteFx card's Loop / RunAtEnd / PingPong bindings time themselves off that remainder).
        float _armedEndTime;
        bool _armedHasDuration;
        // Which OnFrame entries have already fired for the armed clip, so one cannot fire twice on a loop
        // and an unfired one can be flushed when the clip ends.
        readonly System.Collections.Generic.HashSet<FxEntry> _firedThisClip = new();
        // The token currently holding the AnimationArbiter on this character's behalf, or null. Held past
        // Disarm() on purpose: a death's claim outlives its clip (a corpse keeps the body) and only a revival
        // gives it back.
        object _claimOwner;
        // How to retire the currently-armed reaction as interrupted. Kept as a field so the NEXT reaction can
        // run it at the right moment — before it takes the armed state over — rather than after.
        Action _armedInterrupted;

        /// Did a DEATH clip genuinely start playing? False for every "there was nothing to play" case — no
        /// death reaction, no clip name, no animated view, and (the one that used to lie) a clip name the view
        /// does not know. <see cref="ZoeState"/> reads this to decide whether to WAIT for a finish signal or
        /// fall back to its linger timer, so a wrong answer here is a body that either vanishes instantly or
        /// never vanishes at all.
        public bool DeathClipArmed => _deathClipArmed;
        bool _deathClipArmed;

        // ── THE FINISHED-SIGNAL CONTRACT (one place, obeyed identically by all three reaction kinds) ──
        //
        // HurtFinished, DeathFinished and EventFinished each fire EXACTLY ONCE for every reaction that is
        // raised, no exceptions, and their bool always means the same thing: "this did NOT reach its own end".
        //
        //   • the clip played out          → fires when it ends,   false
        //   • the clip was cut short       → fires at the cut,     true
        //   • REFUSED (outranked, or the view does not know the name)
        //                                  → fires immediately,    true
        //   • no clip at all to play       → fires immediately,    false   (nothing started, nothing was lost)
        //
        // Firing unconditionally is the load-bearing half. Anything waiting on one of these — a respawn timer,
        // ZoeState's death linger, a game's "the taunt is over, resume control" — hangs forever on a signal
        // that never comes, and the two ways to never get one (outranked, or a clip name nobody knows) are
        // exactly the two nobody thinks to handle. The three kinds used to disagree here: hurt announced a
        // refusal, death announced one with the WRONG bool, and a named event announced nothing at all unless
        // the consumer also happened to subscribe to EventRefused.
        //
        // EventRefused survives alongside EventFinished because it carries a fact the bool cannot: WHY nothing
        // played (something outranked it) as opposed to "there was nothing to play".

        /// Fires once when a triggered Hurt reaction is over — see the finished-signal contract above.
        public event Action<bool> HurtFinished;
        /// Fires once per death, always — see the finished-signal contract above. A respawn timer downstream,
        /// and ZoeState's linger, both depend on this never being skipped.
        public event Action<bool> DeathFinished;

        void Awake()
        {
            _health = GetComponent<Health>();
            _view = GetComponent<IAnimatedView>();
        }

        void OnEnable()
        {
            if (_health == null) _health = GetComponent<Health>();
            if (_health != null) { _health.Damaged += OnHit; _health.Died += OnDeath; _health.Revived += OnRevived; }
            _deathClipArmed = false;   // a pooled/revived body must not start life believing it is mid-death
            // …and it must not start life still HOLDING the body either. A death's claim is deliberately kept
            // past the clip (see the end-of-reaction path in TryArmClip) and is normally handed back by
            // OnRevived — but a pooled corpse can be switched off and back on without ever being revived, and
            // that path would otherwise re-enter the world with the arbiter locked at priority 1000 by a token
            // from its previous life, which nothing can outrank and nobody is left holding a reference to.
            ReleaseClaim();
        }

        /// Hand the arbiter back if we are holding it, and forget the token. Safe to call when we hold
        /// nothing: <see cref="AnimationArbiter.Release"/> ignores an owner that isn't the current one, so
        /// this can never steal the body from whoever legitimately has it now.
        void ReleaseClaim()
        {
            if (_claimOwner == null) return;
            var c = _claimOwner;
            _claimOwner = null;
            Arbiter?.Release(c);
        }

        void OnDisable()
        {
            if (_health != null) { _health.Damaged -= OnHit; _health.Died -= OnDeath; _health.Revived -= OnRevived; }
            Disarm();
        }

        /// A death claims the arbiter FOREVER (see OnDeath) so nothing animates over a corpse. Coming back to
        /// life is the one thing that must undo that, or a respawned character is frozen on its last death
        /// frame and locomotion can never speak again.
        void OnRevived()
        {
            // Dropped, not fired: coming back to life is not the death reaction being "interrupted", and
            // announcing it as one would restart a respawn cycle that is already underway.
            _armedInterrupted = null;
            Disarm();
            _deathClipArmed = false;
            ReleaseClaim();
        }

        void OnHit(DamageInfo info)
        {
            // Ask, don't default (ZOE_PALETTE_TAKE.md/BUILD_PLAN.md item 2): with no answerer, or an answerer
            // that answers nothing, this plays the built-in Hit reaction exactly as it always has. WHETHER a
            // hurt happens is untouched — this only ever picks its appearance.
            var builtin = def != null ? def.hit : null;
            var r = ResolveLook(builtin, ReactionRole.Hurt, LookAnswerer?.HurtLookFor(info), "hurt");
            if (r == null) return;
            // Read from the reaction that ACTUALLY played, every time, not frozen at spawn. ZoeSpawner used to
            // copy def.hit.stunSeconds onto ZoeState once and never look again, so editing the value on a live
            // character (Mirage, a runtime Instantiate copy, a hot-reloaded asset) changed nothing — and the
            // stun on a death or on a named state was never read by anything at all.
            State?.ApplyStun(r.stunSeconds);
            var req = ReactionRequest.From(info);
            var ctx = BuildContext(req);
            float secs = EventSecondsOf(r);
            ctx.EventSecondsRemaining = secs;   // Immediate entries fire at the event's start — full length
            // Held for the whole armed lifetime (Immediate now, OnFrame/flush later) — see the field's own doc.
            _armedOverrideName = req.OverrideName;
            PlayBodyFx(r, secs);
            FireImmediate(r, ctx);
            var res = TryArmClip(r, ctx, AnimationArbiter.PriorityHurt,
                                 interrupted => HurtFinished?.Invoke(interrupted));
            // Nothing armed means nothing will ever announce ITSELF finished, so announce it here and now, on
            // exactly the terms the contract above states — Refused (a death claim holds, or the view doesn't
            // know the clip) is `true`; nothing-to-play is `false`. Whoever is waiting is never left hanging.
            if (res != ArmResult.Armed) HurtFinished?.Invoke(res == ArmResult.Refused);
        }

        // ── ASKING A CHARACTER TO SHOW A STATE (the two intents, and why they are two methods) ──
        //
        // Raise    — GAME CODE DELIBERATELY CHOSE this name. A miss is a bug, so it WARNS, once, naming what
        //            the character does declare instead.
        // TryRaise — nobody asked for this name in particular; something is offering it speculatively (the
        //            weapon's built-in "Fire", say, which most characters legitimately never declare). A miss
        //            is ordinary, so it is silent.
        //
        // Both return the same bool and run the same code. The split exists because "always warn" and "never
        // warn" are each wrong for one of the two callers, and the previous answer — never warn — meant a
        // deliberately chosen name that missed produced NOTHING: no log, no refusal signal, no return value
        // anyone checked. Measured on ProtoGuy before this change: raising an undeclared name logged zero
        // lines and reported zero refusals. That silence is the whole reason this task exists.
        //
        // Written as two methods rather than a `bool warnIfMissing = true` argument on one, so the call sites
        // read as the two different INTENTS they are instead of as configuration.

        /// Play a custom named reaction — the teleport/spawn/taunt surface. Returns whether this character
        /// DECLARES a state by that name (not whether a clip started — <see cref="EventRefused"/> carries
        /// that), so a caller can tell "no such state" from "played nothing visible" instead of guessing.
        ///
        /// Warns, once per name, when the character declares no such state, listing what it does declare. Use
        /// <see cref="TryRaise(string)"/> instead for a name offered speculatively rather than chosen.
        ///
        /// Runs the SAME sequence OnHit and OnDeath run (context, body FX, immediate entries, then arm the
        /// clip), because a custom event that took a different path would drift from the fixed ones the first
        /// time either was touched — and the whole point of naming them is that they are the same kind of
        /// thing.
        ///
        /// ⚠️ This overload carries NO position, facing or magnitude: the effects land on the character's own
        /// anchor, omni-directional, scaled by nothing. That is the honest answer rather than an invented one
        /// — pass a <see cref="ReactionRequest"/> when the moment really does have a place and a direction
        /// (a shot happens at the muzzle, aimed along the shot).
        public bool Raise(string id) => RaiseInternal(id, default, warn: true);

        /// As <see cref="Raise(string)"/>, with the position/facing/magnitude the moment actually had.
        public bool Raise(string id, in ReactionRequest req) => RaiseInternal(id, req, warn: true);

        /// As <see cref="Raise(string)"/>, from a damage event — the position, direction and amount of the
        /// blow become the state's own, exactly as Hit and Death build theirs.
        public bool Raise(string id, DamageInfo info) => RaiseInternal(id, ReactionRequest.From(info), warn: true);

        /// Ask for a state QUIETLY: identical to <see cref="Raise(string)"/> except that a character which
        /// declares no such state is not a complaint. For a name offered on spec — the reserved "Fire" every
        /// weapon tries — where most characters never declaring it is the normal, correct outcome.
        public bool TryRaise(string id) => RaiseInternal(id, default, warn: false);

        /// As <see cref="TryRaise(string)"/>, with the position/facing/magnitude the moment actually had.
        public bool TryRaise(string id, in ReactionRequest req) => RaiseInternal(id, req, warn: false);

        bool RaiseInternal(string id, in ReactionRequest req, bool warn)
        {
            var r = def != null ? def.EventNamed(id) : null;
            // Honesty surface (T-0096): every attempt to raise a name by id, matched or not — see
            // ZoeReactionTelemetry's own doc for why a miss reports too.
            ZoeReactionTelemetry.Report(def, id, r != null);
            if (r == null)
            {
                if (warn) WarnNoSuchState(id);
                return false;
            }

            // Live, from whichever reaction actually resolved — same rule as OnHit's. A named state's stun has
            // never been read by anything until now.
            State?.ApplyStun(r.stunSeconds);

            var ctx = BuildContext(req);
            float secs = EventSecondsOf(r);
            ctx.EventSecondsRemaining = secs;
            _armedOverrideName = req.OverrideName;
            PlayBodyFx(r, secs);
            FireImmediate(r, ctx);
            // Not Disarm()-ing on failure: a reaction with no clip still fired its Immediate entries above,
            // which is a legitimate custom event (a pure body flash with no animation of its own).
            var res = TryArmClip(r, ctx, AnimationArbiter.PriorityNamedState,
                                 interrupted => EventFinished?.Invoke(id, interrupted));
            // Identical to hurt and death (the contract above): EventFinished fires for EVERY raise, so a
            // caller awaiting it for a named event can never hang. It used to fire only when a clip actually
            // armed, which meant a consumer had to know to also subscribe to EventRefused — and one that
            // didn't waited forever for a "your taunt is over" that was never coming.
            if (res != ArmResult.Armed) EventFinished?.Invoke(id, res == ArmResult.Refused);
            // The return value keeps its ORIGINAL meaning — "this character declares an event by that name" —
            // because CueRelay branches on it and WeaponMuzzleCue relies on the no-such-event no-op. "Declared
            // but the clip was refused" is a different fact and keeps its own signal, which says WHY nothing
            // played where the bool above can only say THAT nothing played.
            if (res == ArmResult.Refused) EventRefused?.Invoke(id);
            return true;
        }

        /// Which reaction actually plays for a hurt/death: the built-in <paramref name="builtin"/> when
        /// nobody answered (or def is unavailable to check against), or the role-chipped custom row
        /// <paramref name="answered"/> names. A row that doesn't exist, or exists but isn't chipped
        /// <paramref name="role"/>, is treated as no legal answer — warned once, falling back to
        /// <paramref name="builtin"/> — never played anyway. This is the ONLY place a hurt/death answer is
        /// resolved, so OnHit and OnDeath can never disagree about what counts as legal.
        ReactionFx ResolveLook(ReactionFx builtin, ReactionRole role, string answered, string questionLabel)
        {
            if (string.IsNullOrEmpty(answered) || def == null) return builtin;
            var entry = def.FindEvent(answered);
            bool legal = entry != null && entry.role == role;
            // Honesty surface (T-0096): an IReactionLookAnswerer naming a row IS a request by name, same as
            // Raise/TryRaise — report it so the row's usage chip and the project-wide miss check both see it.
            ZoeReactionTelemetry.Report(def, answered, legal);
            if (legal) return entry.reaction;
            WarnBadLookAnswer(answered, role, questionLabel);
            return builtin;
        }

        /// Say — once per (role, name) — that an <see cref="IReactionLookAnswerer"/> named a row that isn't a
        /// legal answer to the question it was asked, and say which rows ARE.
        void WarnBadLookAnswer(string answered, ReactionRole role, string questionLabel)
        {
            if (!_warnedMissing.Add($"__look:{role}:{answered}")) return;
            var declared = new System.Collections.Generic.List<string>();
            foreach (var e in def.EventIdsWithRole(role)) declared.Add($"\"{e}\"");
            string list = declared.Count > 0 ? string.Join(", ", declared) : "(none)";
            Debug.LogWarning($"[Zoe] '{def.name}' was told to show \"{answered}\" for its {questionLabel} look, " +
                             $"but that row either doesn't exist or isn't chipped {role} — using the built-in " +
                             $"{questionLabel} reaction instead. Rows chipped {role}: {list}.", this);
        }

        /// Say — once — that someone asked this character for a state it does not have, and say what it DOES
        /// have. Listing the alternatives is the load-bearing half: "no such state" alone leaves the reader
        /// guessing between a typo, a rename, the wrong character and a state nobody ever authored, and the
        /// list settles all four instantly. Logged against `this`, so clicking the line selects the character.
        void WarnNoSuchState(string id)
        {
            if (!_warnedMissing.Add(id ?? "")) return;

            string asked = string.IsNullOrEmpty(id) ? "a state with no name" : $"state \"{id}\"";

            if (def == null)
            {
                Debug.LogWarning($"[Zoe] '{name}' was asked to show {asked}, but it has no Zoe asset, so it " +
                                 "can show nothing by name at all.", this);
                return;
            }

            var declared = new System.Collections.Generic.List<string>();
            foreach (var declaredId in def.EventIds) declared.Add($"\"{declaredId}\"");
            string list = declared.Count > 0 ? string.Join(", ", declared) : "(none)";

            Debug.LogWarning($"[Zoe] '{def.name}' was asked to show {asked} but declares no such state. " +
                             $"It declares: {list}. (Add a row under Reactions ▸ Custom events on that asset, " +
                             "or ask for one of those. Names are matched ignoring case.)", this);
        }

        /// Raised when a custom event finishes its clip: the id, and whether it was INTERRUPTED rather than
        /// reaching its own end.
        public event Action<string, bool> EventFinished;

        /// Raised when a named event was declared and its Immediate effects fired, but its CLIP was refused —
        /// something higher up the priority ladder (a death, a hurt) is holding the body. Distinct from
        /// <see cref="Raise(string)"/> returning false, which means "no such event at all".
        public event Action<string> EventRefused;

        void OnDeath(DamageInfo info)
        {
            // Same ask-don't-default rule as OnHit. Health hitting zero is what killed the character — that
            // already happened, unconditionally, before this method runs — so an unanswered or bad answer
            // here changes nothing about WHETHER it died, only what its death looks like.
            var builtin = def != null ? def.death : null;
            var r = ResolveLook(builtin, ReactionRole.Death, LookAnswerer?.DeathLookFor(info), "death");
            var req = ReactionRequest.From(info);
            var ctx = BuildContext(req);
            // Reset even when nothing plays below (r == null): a stale value from an earlier Raise() with an
            // override name must never leak onto a death's own effects. DamageInfo carries no override name
            // of its own, so this is always null/empty here — see the field's own doc for why it's held.
            _armedOverrideName = req.OverrideName;
            if (r != null)
            {
                // A death's stun was authorable and read by absolutely nothing before this. It matters even on
                // a corpse: CanAct gates the animators, so a death stun is what stops a walk cycle re-asserting
                // itself over the death animation on a character whose death clip is refused.
                State?.ApplyStun(r.stunSeconds);
                float secs = EventSecondsOf(r);
                ctx.EventSecondsRemaining = secs;   // Immediate entries fire at the event's start — full length
                PlayBodyFx(r, secs);
                FireImmediate(r, ctx);
            }
            // Death claims the arbiter at the top of the ladder and NEVER releases it, so locomotion can
            // never resume over a corpse. (ZoeState.CanAct stays the separate GAMEPLAY gate; both hold.)
            var res = TryArmClip(r, ctx, AnimationArbiter.PriorityDeath,
                                 interrupted => DeathFinished?.Invoke(interrupted));

            // A death with nothing of its own to play still ENDS whatever the character was doing: a corpse
            // must not keep flinching through the rest of its hurt animation, and whoever asked for that
            // reaction is owed its "cut short" signal and its unfired effects. (No-op when the death DID arm —
            // arming already retired the previous reaction, in the right order.)
            if (res != ArmResult.Armed) InterruptArmed();

            _deathClipArmed = res == ArmResult.Armed;
            // Nothing playing means nothing to wait for — announce it now so a respawn timer downstream, and
            // ZoeState's linger, are never left waiting on a clip that was never going to start. The bool now
            // distinguishes the two ways that happens, per the contract above (it used to say `false` for
            // both, which told a listener a refused death had "played out normally").
            if (!_deathClipArmed) DeathFinished?.Invoke(res == ArmResult.Refused);
        }

        /// The reaction clip's length in seconds, or 0 when unknown — no clip, no animated view, or a clip the
        /// view cannot measure (a zoned strip with no fixed end).
        float ClipSecondsOf(ReactionFx r)
        {
            var view = AnimView;
            if (r == null || string.IsNullOrEmpty(r.clip) || view == null) return 0f;
            float s = view.GetClipSeconds(r.clip);
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

        // Fill the typed EventContext from the request: the Zoe's live data (transform, health, the current
        // lauminary frame's renderer, the animated view for meta-points) + the request's typed in-params
        // (position/direction/amount). HitPosition bakes the old PointOf fallback (the Zoe's own position when
        // the request carries no point), so placement resolution reproduces the pre-generalization spawn
        // points exactly.
        //
        // Takes a ReactionRequest rather than a DamageInfo because a hit is now just ONE kind of request. Hit
        // and death convert theirs on the way in (ReactionRequest.From reproduces the old point!=zero fallback
        // byte for byte), so their effects land exactly where they always did — while a deliberately raised
        // state can finally say where it happened and which way it faced.
        EventContext BuildContext(in ReactionRequest req)
        {
            var parts = GetComponent<IPartLookup>();
            var renderers = ResolveBodyRenderers(transform, parts);
            return new EventContext
            {
                Transform = transform,
                Health = _health,
                Renderer = renderers.Count > 0 ? renderers[0] : null,
                BodyRenderers = renderers,
                PartLookup = parts,
                View = AnimView,
                HitPosition = req.Position ?? (Vector2)transform.position,
                Direction = req.Direction,
                Amount = req.Amount,
            };
        }

        /// <summary>Resolves the visual body boundary. Composite hosts supply only their declared part transforms;
        /// a simple Zoe retains the historical first-child-renderer fallback.</summary>
        public static System.Collections.Generic.List<SpriteRenderer> ResolveBodyRenderers(
            Transform root, IPartLookup parts)
        {
            var result = new System.Collections.Generic.List<SpriteRenderer>();
            if (parts != null)
            {
                foreach (var part in parts.PartTransforms)
                {
                    var renderer = part != null ? part.GetComponent<SpriteRenderer>() : null;
                    if (renderer != null) result.Add(renderer);
                }
            }

            if (result.Count == 0 && root != null)
            {
                var renderer = root.GetComponentInChildren<SpriteRenderer>();
                if (renderer != null) result.Add(renderer);
            }
            return result;
        }

        // ── clip + frame-event arming ────────────────────────────────────────

        /// Why a reaction's clip did or didn't start. Three outcomes, not two, because "this reaction has no
        /// clip" and "it has one and the body refused it" call for different answers downstream — the first is
        /// a legitimate FX-only reaction, the second leaves a requester waiting on a signal.
        enum ArmResult
        {
            /// Nothing to play: no reaction, no clip name, or no animated view on this character.
            NoClip,
            /// There WAS a clip and it did not start — outranked by an active claim, or a name the view
            /// doesn't know.
            Refused,
            /// The clip is genuinely playing.
            Armed,
        }

        /// Arm <paramref name="r"/>'s clip + frame effects at <paramref name="priority"/>.
        /// <paramref name="onFinished"/> is called exactly once when the reaction is over, with whether it was
        /// INTERRUPTED (cut off by something higher up the ladder) rather than reaching its own end — and it
        /// is called after this reaction's unfired frame effects have been flushed, so being cut short costs a
        /// reaction its remaining timing, never its effects.
        ArmResult TryArmClip(ReactionFx r, EventContext ctx, float priority, Action<bool> onFinished)
        {
            var view = AnimView;
            if (r == null || string.IsNullOrEmpty(r.clip) || view == null) return ArmResult.NoClip;

            // A FRESH token per claim, never `this`: two reactions raised by this same component have to
            // arbitrate against EACH OTHER (a named event must not stomp a hurt clip still playing, and dying
            // mid-flinch must interrupt it properly). Sharing one owner identity makes every such pair look
            // like "the same owner reasserting", which skips both the priority test and the interrupt notice.
            var claim = new object();

            // BOTH refusal tests run BEFORE anything is torn down, so a refused reaction leaves the running one
            // completely intact — its frame handler, its already-fired bookkeeping, its pending completion.
            //
            // The priority test alone was not enough, and the gap was a real bug: a reaction that OUTRANKS the
            // incumbent but names a clip the view has never heard of (a typo, a renamed or deleted animation)
            // passed this gate, retired the reaction in flight — flushing its remaining effects and telling its
            // requester it was cut short — and then played nothing whatsoever. Asking the view for the NAME
            // first (HasClip mirrors the player's own Play() success condition exactly, so a yes here means a
            // yes there) closes it: an unknown clip is now refused on the same terms as being outranked, and
            // costs the running reaction nothing.
            if (!view.HasClip(r.clip)) return ArmResult.Refused;
            if (Arbiter != null && !Arbiter.WouldAccept(claim, priority)) return ArmResult.Refused;

            // Retire the outgoing reaction HERE, while its own state is still the live one. Doing it from the
            // arbiter's interrupt callback instead would run it after the incoming reaction had already taken
            // over `_armed`/`_armedCtx`, so the flush would fire the NEW reaction's whole effect list at once,
            // instantly, under the old one's name — and then unsubscribe the frame handler it just installed.
            var displacedClaim = _claimOwner;
            InterruptArmed();

            Disarm();
            _armed = r;
            _armedCtx = ctx;
            float clipSecs = ClipSecondsOf(r);
            float eventSecs = r.DurationSeconds(clipSecs);
            _armedHasDuration = eventSecs > 0f;
            _armedEndTime = Time.time + eventSecs;
            // Subscribed BEFORE the play, not after: a Zoned player fires OnFrameEntered for frame 0
            // synchronously from inside Play(), so subscribing afterwards silently drops any OnFrame effect
            // authored on the reaction's first frame. Disarm() below undoes it if the clip never started.
            _armedFrameHandler = HandleArmedFrame;
            view.OnFrameEntered += _armedFrameHandler;

            // A death keeps the body forever, so nothing animates over a corpse; everything else hands it back
            // the moment it is done. (ZoeState.CanAct remains the separate gameplay gate.)
            bool handsBack = priority < AnimationArbiter.PriorityDeath;
            bool ended = false;
            Action interruptedHook = null;

            // ONE end-of-reaction path for both outcomes, guarded so it can only run once however it is
            // reached (its own completion, this component retiring it, or the arbiter reporting a takeover).
            Action<bool> end = interrupted =>
            {
                if (ended) return;
                ended = true;
                if (ReferenceEquals(_armedInterrupted, interruptedHook)) _armedInterrupted = null;
                if (ReferenceEquals(_claimOwner, claim))
                {
                    if (handsBack)
                    {
                        _claimOwner = null;
                        // Hand the body back only if we STILL actually hold it. When the interruption was a
                        // takeover the new owner already has it and releasing would yank it out from under
                        // them; when it was this component retiring the reaction with nobody taking over,
                        // releasing is exactly right or the body stays locked to a reaction that is over.
                        if (Arbiter != null && Arbiter.HasControl(claim)) Arbiter.Release(claim);
                    }
                    // A DEATH keeps the body — and therefore `_claimOwner` MUST keep pointing at this claim,
                    // which is exactly what the field's own doc comment promises ("a death's claim outlives
                    // its clip and only a revival gives it back"). Clearing it here instead, as this used to,
                    // threw away the only reference to the token holding the arbiter at priority 1000 with no
                    // expiry: OnRevived's `if (_claimOwner != null)` then found nothing to release, so a
                    // revived character was animation-locked FOREVER — locomotion refused, a new hurt
                    // refused, and even a second death refused (1000 is not > 1000), so it could never
                    // visibly die again either. Dormant only while no Zoe has a death clip authored.
                }
                FlushUnfiredFrameEntries();
                Disarm();
                onFinished?.Invoke(interrupted);
            };
            interruptedHook = () => end(true);
            _armedInterrupted = interruptedHook;

            _claimOwner = Arbiter != null ? claim : null;
            if (!PlayPass(r, clipSecs, claim, priority, () => end(false), interruptedHook, first: true))
            {
                ended = true;
                _armedInterrupted = null;
                _claimOwner = null;
                Disarm();
                // The reaction we displaced has already been told it was cut short, so nobody is left to hand
                // its claim back. The arbiter rolled its own state back to it, so release it here rather than
                // leaving the body locked to a reaction that is over.
                if (displacedClaim != null) Arbiter?.Release(displacedClaim);
                return ArmResult.Refused;
            }
            return ArmResult.Armed;
        }

        /// Retire whatever is armed right now as INTERRUPTED — flush its pending effects, drop it, tell its
        /// requester. No-op when nothing is armed.
        void InterruptArmed()
        {
            var cb = _armedInterrupted;
            _armedInterrupted = null;
            cb?.Invoke();
        }

        /// Play the clip once, and again for as long as the EVENT still has room for a whole pass — how a
        /// multi-loop event, or a fixed-seconds event longer than its clip, actually gets its length. Returns
        /// whether the clip actually started, which is what tells a caller "armed" from "refused".
        ///
        /// Re-play is gated on at least HALF a clip remaining, so an event whose length is not a whole number
        /// of clips ends on a clean pass instead of a visible stutter of a few frames. A clip the view cannot
        /// measure has no remainder to reason about and simply plays once, as it always did.
        bool PlayPass(ReactionFx r, float clipSecs, object claim, float priority, Action onComplete, Action onInterrupted, bool first)
        {
            Action complete = () =>
            {
                bool roomForAnother = ReferenceEquals(_armed, r) && _armedHasDuration && clipSecs > 0f &&
                                      _armedEndTime - Time.time > clipSecs * 0.5f &&
                                      (Arbiter == null || Arbiter.HasControl(claim));
                if (roomForAnother) PlayPass(r, clipSecs, claim, priority, onComplete, onInterrupted, first: false);
                else onComplete?.Invoke();
            };

            // Only the FIRST pass submits a claim; later passes RE-play the claim we already hold. Neither
            // goes to the view directly. Re-submitting via Play would hit the arbiter's own "same owner, same
            // clip — don't restart" rule and silently play nothing, which is why this used to bypass the
            // arbiter entirely — but bypassing it is wrong for a composite body, whose parts each arbitrate
            // on their own and would simply never hear about pass 2. Replay carries the held claim's priority
            // back down the same dispatch pass 1 used.
            if (Arbiter != null)
                return first
                    ? Arbiter.Play(claim, priority, r.clip, loop: false, durationSeconds: 0f,
                                   onComplete: complete, onInterrupted: onInterrupted, targetPart: r.targetPart)
                    : Arbiter.Replay(claim, onComplete: complete);

            return AnimView != null && AnimView.PlayClip(r.clip, loop: false, onComplete: complete);
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
            var renderers = ResolveBodyRenderers(transform, GetComponent<IPartLookup>());
            foreach (var sr in renderers)
            {
                var filter = sr.GetComponent<SpriteFxFilter>();
                if (filter == null) filter = sr.gameObject.AddComponent<SpriteFxFilter>();
                filter.stack = r.bodyFx;
                filter.externalPositionResolver = new ZoeMetaPositionResolver(AnimView, sr);
                if (eventSeconds > 0f) filter.Play(eventSeconds);
                else filter.Play();   // unknown event length — the stack's own duration is the honest last resort
            }
        }

        // ── spawning ──────────────────────────────────────────────────────────
        void Fire(FxEntry entry, EventContext ctx)
        {
            if (!entry.enabled) return;   // muted (task #8) — kept in the list but never fires
            // Resolve ONCE, against whichever override name the currently-armed reaction was raised with (see
            // _armedOverrideName's own doc) — an unmatched or absent name is a no-op inside Resolve() itself,
            // yielding entry.fx, so this is exactly the pre-override behaviour when nothing asked for a swap.
            var effect = entry.Resolve(_armedOverrideName);
            if (effect == null || effect.IsEmpty) return;
            if (!ctx.TryResolvePosition(entry, out var pos)) return;

            // Stamp the resolved params the effect reads. For an ICombatFx these are exactly the (pos, dir) the
            // old entry.fx.Play(...) received, so the spawn point is byte-for-byte unchanged.
            ctx.Position = pos;
            // Rotation + mirror resolved TOGETHER (a Random direction is rolled once and both agree).
            ctx.ResolveOrientation(entry, out float rotationDeg, out bool flipX);
            ctx.DirectionDeg = rotationDeg;
            ctx.FlipX = flipX;
            ctx.Scalar = ctx.ResolveScalar(entry.scalar);

            if (!entry.follow) { effect.Apply(ctx); return; }

            // Follow re-homes a single spawned Transform each frame — an ICombatFx-only capability
            // (PlayFollowable). A non-ICombatFx effect has no Transform to hand back, so it just applies once.
            if (!(effect is ICombatFx combat)) { effect.Apply(ctx); return; }

            var t = combat.PlayFollowable(pos, ctx.DirectionDeg, ctx.FlipX);
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
                follower.Init(() => ctx.TryResolvePosition(entry, out var p) ? (Vector3)p : t.position);
            }
        }
    }
}
