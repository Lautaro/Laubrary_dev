using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Zoetrope;
using Laubrary.Launimator;
using Laubrary.Choreographer;
using Laubrary.Caching;
using Laubrary.ZoetropeLaunimator;

namespace Laubrary.Mirage
{
    /// <summary>
    /// Drop this on an empty GameObject in a Mirage scene, assign a Zoe — it spawns through the exact same
    /// <see cref="ZoeSpawner"/> path gameplay uses, so weapon fire, muzzle FX, hit/death FX and Choreography
    /// motion are never reimplemented for preview purposes: whatever the assigned Zoe/WeaponDef actually
    /// have configured is what shows up, nothing more. A Zoe with no weapon shows no fire options; a clip
    /// with no painted MetaLayer can't drive a <see cref="ClipStep.fireWeapon"/> step. <see cref="ExecuteAlways"/>
    /// so it spawns in Edit mode too (MirageRig places it, this shows it) — the actual spawn trigger is a
    /// GUARDED FIRST Update() tick, not Start(): Unity does NOT call Start() in Edit mode for an ExecuteAlways
    /// script (a real, documented limitation — Update/OnEnable/Awake all run in Edit mode, Start doesn't),
    /// which meant a Zoe previewable was COMPLETELY INVISIBLE outside Play mode (reported: "assets are not
    /// visible until play mode") — not just static, nothing spawned at all. Update() still isn't used for
    /// the SAME reason OnEnable() was originally rejected in favour of Start() (MirageRig.Realize() does
    /// AddComponent then assigns zoe/weapon/etc. synchronously right after — OnEnable would fire mid-
    /// assignment, the exact trap WeaponMuzzleCue's Configure() pattern exists to avoid) — but by the time
    /// the FIRST Update() tick runs, one full frame later, those synchronous field assignments have already
    /// completed, same timing guarantee Start() was reaching for, just via a hook that also fires in Edit mode.
    /// Downstream animation/auto-fire only actually plays once ZonedAnimationPlayer's own Update runs
    /// (Play mode only, deliberately not made ExecuteAlways — it's shared gameplay infrastructure, out of
    /// scope for a Mirage-only change) — in Edit mode a Zoe previewable now shows its spawned idle pose,
    /// static (this fix's actual scope), rather than nothing.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Laubrary/Mirage/Mirage Subject")]
    public class MirageSubject : MonoBehaviour
    {
        [Header("Content")]
        public Zoe zoe;
        [Tooltip("Optional. Which of the Zoe's OWN equipped weapons (zoe.weapons) to make active for this " +
                 "preview — Mirage selects a slot, it never equips a WeaponDef the Zoe doesn't already have " +
                 "configured (Mirage doesn't edit the Zoe). Leave unset to use the Zoe's own defaultActiveWeapon.")]
        public WeaponDef weapon;
        [Tooltip("Optional. Drives this subject's OWN motion (matches how OutBurner's WaveGroupDef pairs a " +
                 "character + weapon + choreography) — not a fired projectile's path.")]
        public Choreography choreography;

        [Header("Animation")]
        [Tooltip("Cycles through these clips in order, forever (e.g. Idle, Shoot, repeat), using " +
                 "ZonedAnimationPlayer.OnComplete to step to the next one -- always, there's no separate " +
                 "single-clip mode; a one-item list just replays that one clip forever. Only meaningful if " +
                 "the Zoe's view is a Zoned Launimator view. Each step owns its OWN fire-weapon trigger " +
                 "(ClipStep.fireWeapon/muzzleLayerId) instead of one entry-wide layer.")]
        public List<ClipStep> clips = new List<ClipStep>();
        [Tooltip("Stands in for whatever would drive Combatant.aimDirection in real gameplay (player input, " +
                 "AI) -- Mirage has neither, so a fixed preview value is needed to fire in a specific direction.")]
        public Vector2 previewAimDirection = Vector2.right;
        [Tooltip("Freeze onto one named, real game-state pose (\"Idle N\", \"Moving E\") from " +
                 "MotionPoseCatalog.Derive(zoe) instead of driving off live input. Empty = normal preview.")]
        public string previewPose = "";

        [Header("Target Practice (a Mirage-only testing convenience, not saved onto the Zoe)")]
        [Tooltip("Opt in to a self-contained Idle -> (Hurt) -> Death -> respawn loop for THIS preview, " +
                 "replacing the Clips list above. Uses the real spawn path, Health, and the Zoe's own " +
                 "hit/death reactions for the hurt/death clips -- the respawning-dummy LOOP itself is " +
                 "Mirage-side. Idle is always the Zoe's own view.idleClip, not a separate field.")]
        public bool targetPractice;
        [Min(0f)] public float respawnDelay = 2f;

        [Header("Manual control (opt-in, requested by the editor that configured this view)")]
        [Tooltip("Offer manual controls for this subject in the Mirage HUD. Opt-in, set by whichever editor " +
                 "configured the view (see PreviewableEntry.manualControls).")]
        public bool manualControls;

        /// The hand-control driver, when <see cref="manualControls"/> asked for one — the HUD's handle on this
        /// subject. Null otherwise, so a view that didn't opt in costs nothing.
        public MirageManualDriver Manual { get; private set; }

        /// What the spawned Zoe can actually be asked to do, derived once per spawn. The HUD builds its
        /// controls from THIS, so a capability the character hasn't earned produces no button.
        public ZoeCapabilities Capabilities { get; private set; }

        // Hand-set pose, kept across a respawn (see Spawn's restore branch). Not serialized on purpose: it is
        // transient test state, not authoring, and must never end up saved onto anything.
        bool _hasKeptManual;
        float _keptAim, _keptMove;
        bool _keptWalking, _keptFiring, _keptLock = true;

        /// <summary>Re-establish the manual-control state if it went missing, and do nothing if it didn't.
        ///
        /// <see cref="Manual"/> and <see cref="Capabilities"/> are plain properties, so a domain reload (any
        /// script recompile) silently wipes them while the spawned GameObject itself survives — the same trap
        /// this class's own Update() comment describes for Spawned/Player/Weapon, and that MotionPoseAnimator
        /// and AnimatedViewRelay both hit before. Without this, the controls disappear after any recompile and
        /// look like a broken feature; re-deriving is cheap and always correct, so it self-heals instead.</summary>
        public void EnsureManualControls()
        {
            if (!manualControls || Spawned == null) return;
            if (Capabilities.Zoe == null) Capabilities = ZoeCapabilities.Derive(zoe);
            // A Zoe with its own playerController configured is driven by REAL input (ZoeSpawner already
            // attached it) — the hand-control rig would fight it every frame (see Spawn()'s own comment), so
            // it's skipped entirely rather than added and left to conflict.
            if (zoe != null && zoe.playerController != null) return;
            if (Manual == null)
            {
                Manual = Spawned.GetComponent<MirageManualDriver>();
                if (Manual == null) Manual = Spawned.AddComponent<MirageManualDriver>();
            }
        }

        [Header("Combat")]
        public LayerMask projectileBlockers;

        public GameObject Spawned { get; private set; }
        public ZonedAnimationPlayer Player { get; private set; }
        public ProjectileWeapon Weapon { get; private set; }
        public Combatant Combatant { get; private set; }
        public Health Health { get; private set; }

        int _sequenceIndex;

        // Mirage's whole reason to exist is that authoring changes show up WITHOUT a restart -- see the
        // standing protocol in Laubrary.Caching.AssetCacheInvalidation's own doc comment. A Zoe's view fields
        // (e.g. ZonedLauminaryView.height) and an equipped WeaponDef's fields are only ever copied onto the spawned
        // GameObject ONCE, at Spawn() time -- editing them afterward left the live preview stale until the
        // whole MirageView was reloaded (reported: changed Floating Disc's height, had to reload to see it).
        // Doing a full destroy+respawn on any relevant edit is deliberately blunt rather than trying to patch
        // individual fields live (chasing "which fields matter" one at a time is exactly the fragility that
        // protocol warns about) -- it guarantees the live preview matches a fresh spawn of whatever's
        // currently authored, at the cost of resetting any in-progress test state (health, current clip) on
        // every edit, which is the right trade for an authoring tool.
        void OnEnable() => AssetCacheInvalidation.Invalidated += HandleAssetInvalidated;
        void OnDisable() => AssetCacheInvalidation.Invalidated -= HandleAssetInvalidated;

        bool _pendingRebuild;

        // Only sets a flag here -- does NOT destroy/respawn synchronously. AssetCacheInvalidation.Invalidated
        // fires from INSIDE Unity's own ObjectChangeEvents dispatch (see AssetCacheInvalidationBridge), and
        // doing heavy scene mutation (DestroyImmediate + several AddComponent calls, i.e. Spawn()) reentrant
        // to that Editor-internal callback is genuinely unsafe -- confirmed live: it triggered Unity's own
        // "Access version should be odd when acquiring lock" assertion, repeatedly, the moment any watched
        // asset changed. The actual rebuild happens in Update() below instead, the normal/safe place for this
        // component to mutate the scene, same reasoning as the class doc's Update()-not-Start()/OnEnable()
        // explanation.
        void HandleAssetInvalidated(Object asset)
        {
            if (Spawned == null || zoe == null) return;
            bool relevant = asset == zoe
                         || (zoe.weapons != null && zoe.weapons.Exists(s => s != null && s.weapon == asset))
                         || ReferencesLauminaryVersion(asset);
            if (relevant) _pendingRebuild = true;
        }

        /// <summary>Does this Zoe's view play out of <paramref name="asset"/>? The animation data — clips,
        /// meta-layers, the painted points a rig anchor reads — lives in a LauminaryVersion the view points at,
        /// NOT on the Zoe, so watching only the Zoe and its weapons meant editing an animation never refreshed
        /// the preview. That is the half of "edit and watch it change" that was missing.</summary>
        bool ReferencesLauminaryVersion(Object asset)
        {
            if (asset == null || zoe == null) return false;
            if (zoe.view is CompositeLauminaryView composite)
            {
                if (composite.parts == null) return false;
                foreach (var part in composite.parts)
                    if (part?.view is ZonedLauminaryView pv && pv.version != null && pv.version == asset) return true;
                return false;
            }
            return zoe.view is ZonedLauminaryView single && single.version != null && single.version == asset;
        }

        // Tears down the live spawn (and its event subscriptions) without touching this component's own
        // fields (zoe/weapon/clips/etc.) -- shared by the invalidation handler above, which always follows it
        // with a fresh Spawn().
        void DestroySpawned()
        {
            if (Player != null)
            {
                Player.OnMetaLayerReached -= HandleMetaLayerReached;
                Player.OnFrameEvent -= HandleFrameEvent;
                Player.OnComplete -= HandleSequenceStepComplete;
            }
            if (Spawned != null)
            {
                if (Application.isPlaying) Destroy(Spawned); else DestroyImmediate(Spawned);
            }
            // Remember the hand-set pose before the driver dies with the body, so an asset edit doesn't reset
            // what you were looking at.
            if (Manual != null)
            {
                _hasKeptManual = true;
                _keptAim = Manual.aimAngleDeg; _keptMove = Manual.moveAngleDeg;
                _keptWalking = Manual.walking; _keptFiring = Manual.firing; _keptLock = Manual.lockMoveToAim;
            }
            // Manual/Capabilities go with the rest: Manual lives ON the object just destroyed, so keeping the
            // reference would leave the HUD driving a corpse.
            Spawned = null; Player = null; Weapon = null; Combatant = null; Health = null;
            Manual = null; Capabilities = ZoeCapabilities.None;
            _sequenceIndex = 0;
        }

        // A domain reload (any script recompile) resets EVERY plain field/property on this component --
        // Spawned/Player/Weapon/Combatant/Health are none of them [SerializeField] -- even though the
        // already-spawned CHILD GameObject itself survives the reload just fine (a real, fully-serialized
        // scene object). Without checking the actual hierarchy, every recompile while this MirageSubject
        // sits in an open Edit-mode scene would spawn ANOTHER duplicate character on top of the orphaned
        // one from last time, forever accumulating (confirmed live: 3 duplicate copies after a handful of
        // edit-mode recompiles). transform.childCount is ground truth that survives the reload even though
        // the C# fields tracking it don't.
        void Update()
        {
            if (_pendingRebuild)
            {
                _pendingRebuild = false;
                if (Spawned != null) DestroySpawned();
            }
            if (Spawned != null) { EnsureManualControls(); return; }
            if (transform.childCount > 0) { RebindToExisting(transform.GetChild(0).gameObject); return; }
            Spawn();
        }

        // Re-attaches to an already-spawned child (found via transform.childCount, see Update() above)
        // instead of calling Spawn() again -- re-resolves the same component references and RE-SUBSCRIBES
        // the MetaLayer/sequence-complete event handlers Spawn() would have wired up, since domain-reload
        // wiped the in-memory delegate lists along with everything else. _sequenceIndex resets to 0 (a real
        // but minor animation-continuity glitch — restarts the Clips list from the top rather than resuming
        // mid-sequence — not worth the extra complexity of trying to infer which step was actually playing).
        void RebindToExisting(GameObject existing)
        {
            Spawned = existing;
            Combatant = Spawned.GetComponent<Combatant>();
            Health = Spawned.GetComponent<Health>();
            Player = Spawned.GetComponent<ZonedAnimationPlayer>();
            if (Combatant != null) Combatant.aimDirection = previewAimDirection;
            ApplyPoseOverride();

            var switcher = Spawned.GetComponent<WeaponSwitcher>();
            Weapon = switcher != null ? switcher.ActiveWeapon : null;

            EnsureTargetPractice();

            // Target Practice owns Idle/Hurt/Death itself (via TargetPracticeController above) — the general
            // Clips-sequence engine below would fight it for the same Player.Play()/OnComplete, so it's
            // skipped entirely while this preview is in that mode, same gate as Spawn() uses.
            if (Player != null && !targetPractice)
            {
                _sequenceIndex = 0;
                Player.OnComplete += HandleSequenceStepComplete;
                bool anyStepFires = clips != null && clips.Exists(s => s != null && s.fireWeapon);
                if (anyStepFires) { Player.OnMetaLayerReached += HandleMetaLayerReached; Player.OnFrameEvent += HandleFrameEvent; }
            }
        }

        // Applies/clears previewPose on every MotionPoseAnimator under Spawned (one per composite part, or
        // one for a single-body Zoe) — looked up by label against MotionPoseCatalog.Derive(zoe), the SAME
        // auto-derived list Mirage's own UI offers, so a stale/renamed label just falls back to normal live
        // preview (ClearPoseOverride) rather than throwing. Called from both Spawn() and RebindToExisting()
        // since a domain reload needs the override re-applied to the FRESH MotionPoseAnimator instances the
        // reload's Awake/OnEnable produced, same reasoning DestroySpawned/RebindToExisting's own doc gives
        // for re-subscribing event handlers there.
        void ApplyPoseOverride()
        {
            if (Spawned == null) return;
            var animators = Spawned.GetComponentsInChildren<MotionPoseAnimator>(true);
            if (animators.Length == 0) return;

            MotionPoseCatalog.Pose? match = null;
            if (!string.IsNullOrEmpty(previewPose) && zoe != null)
            {
                foreach (var p in MotionPoseCatalog.Derive(zoe))
                    if (p.label == previewPose) { match = p; break; }
            }

            foreach (var a in animators)
            {
                if (match.HasValue) a.SetPoseOverride(match.Value.bucket, match.Value.angleDeg);
                else a.ClearPoseOverride();
            }
        }

        // Mirage-only respawning-dummy convenience — NOT saved onto the Zoe (it lived there briefly during
        // development; moved here after review, since "opt in to Target Practice in Mirage" meant a Mirage
        // concept, not a persistent Zoe field — see PreviewableEntry.targetPractice's own doc comment).
        // Ensures ReactionFxPlayer exists (so TargetPracticeController has finished-events to listen to even
        // if the Zoe's own hit/death reactions have no clip set — they then simply have no clip, which
        // ReactionFxPlayer already handles gracefully) + TargetPracticeController, both idempotent so this is
        // safe to call from Spawn() and RebindToExisting() alike.
        void EnsureTargetPractice()
        {
            if (!targetPractice || Spawned == null) return;
            var hrp = Spawned.GetComponent<ReactionFxPlayer>();
            if (hrp == null) hrp = Spawned.AddComponent<ReactionFxPlayer>();
            hrp.def = zoe;
            var tpc = Spawned.GetComponent<TargetPracticeController>();
            if (tpc == null) tpc = Spawned.AddComponent<TargetPracticeController>();
            // Idle is always the Zoe's own view.idleClip -- no separate Mirage-authored field (removed after
            // review: redundant with data the Zoe already has, and Target Practice is meant for the simple
            // "just wire up a hit reaction" case, not another place to author animation choices).
            var zonedView = zoe != null ? zoe.view as ZonedLauminaryView : null;
            tpc.idleClip = zonedView != null ? zonedView.idleClip : "";
            tpc.respawnDelay = respawnDelay;
            // A dummy that respawns must never be cleaned up on death — the whole point is that this same
            // object comes back. Without this the body is destroyed after the Zoe's own deathLinger (1.5s by
            // default), which is SHORTER than the default respawnDelay (2s), so the respawn coroutine dies
            // with the object it was going to revive and Target Practice silently stops after one kill.
            // Set here rather than in TargetPracticeController because "this is a Mirage-only convenience" is
            // already this method's job, and that controller deliberately holds no Zoe/ZoeState reference.
            var state = Spawned.GetComponent<ZoeState>();
            if (state != null) state.disposal = DeathDisposal.Leave;
        }

        // Only unsubscribes Player's events (mirrors DestroySpawned above) -- doesn't null Spawned/etc. or
        // touch the GameObject itself, since Unity is already tearing it down; just needs the delegates gone.
        void OnDestroy()
        {
            if (Player != null)
            {
                Player.OnMetaLayerReached -= HandleMetaLayerReached;
                Player.OnFrameEvent -= HandleFrameEvent;
                Player.OnComplete -= HandleSequenceStepComplete;
            }
        }

        void Spawn()
        {
            if (zoe == null) return;

            Spawned = ZoeSpawner.SpawnCharacter(zoe, transform.position, transform, projectileBlockers);
            Combatant = Spawned.GetComponent<Combatant>();
            Health = Spawned.GetComponent<Health>();
            Player = Spawned.GetComponent<ZonedAnimationPlayer>();   // null if the Zoe's view isn't Zoned — fine.
            if (Combatant != null) Combatant.aimDirection = previewAimDirection;
            ApplyPoseOverride();

            // Manual control is opt-in per entry, so a view assembled to look at effects or a backdrop never
            // grows character controls it has no use for. Capabilities are derived per spawn rather than
            // cached on the asset — they must never be able to disagree with the Zoe they describe.
            Capabilities = ZoeCapabilities.Derive(zoe);
            // A Zoe with its own playerController configured is driven by REAL input instead — ZoeSpawner
            // above already attached it (that's the whole point: any spawner gets it for free, Mirage
            // included), and the hand-control rig would fight it every frame (its Update() unconditionally
            // publishes a MotionState override, which would freeze/override real movement's inferred state).
            if (manualControls && (zoe == null || zoe.playerController == null))
            {
                Manual = Spawned.AddComponent<MirageManualDriver>();
                if (_hasKeptManual)
                {
                    // Carry the hand-set pose across a respawn. An asset edit rebuilds the character, and
                    // snapping it back to facing-north-and-idle every time you nudge a painted point would
                    // make the edit-and-watch loop unusable — you'd lose the exact pose you were judging.
                    Manual.aimAngleDeg = _keptAim;
                    Manual.moveAngleDeg = _keptMove;
                    Manual.walking = _keptWalking;
                    Manual.firing = _keptFiring;
                    Manual.lockMoveToAim = _keptLock;
                }
                else
                {
                    // First spawn: start facing wherever the entry was already pointing, so opening the
                    // controls doesn't snap the character out of the pose being looked at.
                    Manual.aimAngleDeg = previewAimDirection.sqrMagnitude > 1e-6f
                        ? Mathf.Repeat(Mathf.Atan2(previewAimDirection.x, previewAimDirection.y) * Mathf.Rad2Deg, 360f)
                        : 0f;
                    Manual.moveAngleDeg = Manual.aimAngleDeg;
                }
            }

            EnsureTargetPractice();

            // Target Practice owns Idle/Hurt/Death itself (via TargetPracticeController above) — the general
            // Clips-sequence engine below would fight it for the same Player.Play()/OnComplete, so it's
            // skipped entirely while this preview is in that mode.
            if (Player != null && !targetPractice && clips != null && clips.Count > 0)
            {
                _sequenceIndex = 0;
                Player.OnComplete += HandleSequenceStepComplete;
                PlaySequenceStep();
            }

            // Weapons only ever come from the Zoe's own equipped set (zoe.weapons, equipped by
            // ZoeSpawner.SpawnCharacter above via EquipWeaponSlots) -- `weapon`, if set, just selects WHICH of
            // those already-configured slots to make active for this preview. Mirage never equips a WeaponDef
            // the Zoe doesn't already have configured (Mirage doesn't edit the Zoe).
            var switcher = Spawned.GetComponent<WeaponSwitcher>();
            if (switcher != null)
            {
                int idx = weapon != null && zoe.weapons != null ? zoe.weapons.FindIndex(s => s != null && s.weapon == weapon) : -1;
                if (idx >= 0) switcher.SwitchTo(idx);
                Weapon = switcher.ActiveWeapon;
            }

            // Subscribe once if ANY step wants to fire -- HandleMetaLayerReached itself gates on whichever
            // step is ACTUALLY playing when a layer is reached (_sequenceIndex), not a single entry-wide
            // layer id, which couldn't tell "fire during THIS step" from "fire during that other one."
            bool anyStepFires = !targetPractice && clips != null && clips.Exists(s => s != null && s.fireWeapon);
            if (Player != null && anyStepFires)
            {
                Player.OnMetaLayerReached += HandleMetaLayerReached;
                Player.OnFrameEvent += HandleFrameEvent;
            }

            if (choreography != null)
            {
                var mover = Spawned.AddComponent<ChoreographyPlayer>();
                mover.choreography = choreography;
                mover.anchor = transform;
            }
        }

        void HandleMetaLayerReached(string layerId, Vector3 worldPos)
        {
            if (Weapon == null || clips == null || _sequenceIndex < 0 || _sequenceIndex >= clips.Count) return;
            // Gate on whichever STEP is actually playing right now (_sequenceIndex), not a single entry-wide
            // layer id -- two different clips in the same sequence can each paint a layer of the same id
            // ("Muzzle" on both Shoot and, say, a melee Swing) and only one of them should trigger a shot.
            var step = clips[_sequenceIndex];
            if (step == null || !step.fireWeapon || !string.Equals(layerId, step.muzzleLayerId, System.StringComparison.OrdinalIgnoreCase))
                return;
            // worldPos is the MetaLayer's live, per-frame painted position -- fire FROM there (the animation's
            // actual muzzle pixel this frame), not the weapon's cached equip-time muzzle Transform. Direction
            // still resolves normally (Combatant.aimDirection, seeded above from previewAimDirection) --
            // TryFireFrom only overrides WHERE the shot originates, never WHICH WAY it goes.
            Weapon.TryFireFrom(worldPos);
        }

        // Event-driven analog of HandleMetaLayerReached above -- a step can trigger off a lightweight FrameEvent
        // (one authored pixel on one frame) instead of a painted MetaLayer. Same _sequenceIndex gating logic.
        void HandleFrameEvent(string eventName, int frame)
        {
            if (Weapon == null || Player == null || clips == null || _sequenceIndex < 0 || _sequenceIndex >= clips.Count) return;
            var step = clips[_sequenceIndex];
            if (step == null || !step.fireWeapon || string.IsNullOrEmpty(step.muzzleEventName) ||
                !string.Equals(eventName, step.muzzleEventName, System.StringComparison.OrdinalIgnoreCase))
                return;
            if (Player.TryGetEventPoint(eventName, out var worldPos)) Weapon.TryFireFrom(worldPos);
        }

        // ZonedAnimationPlayer.OnComplete fires exactly once when a NON-looping clip finishes — playing each
        // step with loop:false and stepping to the next (wrapping) on that event is what turns a plain "play
        // one clip" player into "cycle this list forever" with no engine changes needed.
        //
        // Deliberately NOT routed through AnimationArbiter, unlike every runtime clip channel (T-0094). Three
        // reasons, all specific to this one: `Player` is the raw Launimator player on the subject's ROOT, which
        // only exists for a single-body Zoe at all (a composite keeps its players on the parts, so this whole
        // engine is already inert there); the Clips list is a MIRAGE AUTHORING preview that is meant to be the
        // only thing driving the subject, not one claimant among several; and it is skipped outright whenever
        // Target Practice is on — the one mode where real hurt/death reactions play and could be stomped. The
        // residual case it does NOT cover, stated rather than hidden: a previewed Zoe that also authors plain
        // `locomotion` clips will have its locomotion animator and this sequence writing to the same player.
        // That is pre-existing and Mirage-only; if it ever bites, the fix is a claim here at
        // AnimationArbiter.PriorityNamedState, not another special case.
        void PlaySequenceStep()
        {
            if (Player == null || clips == null || clips.Count == 0 || clips[_sequenceIndex] == null) return;
            Player.Play(clips[_sequenceIndex].clip, loop: false);
        }

        void HandleSequenceStepComplete()
        {
            if (clips == null || clips.Count == 0) return;
            _sequenceIndex = (_sequenceIndex + 1) % clips.Count;
            PlaySequenceStep();
        }

        /// Restart from frame 0 without respawning the character — back to step 0 of the Clips list.
        public void RestartClip()
        {
            if (Player == null || clips == null || clips.Count == 0) return;
            _sequenceIndex = 0;
            PlaySequenceStep();
        }

        /// Fire the equipped weapon once, on demand (e.g. an Inspector button), independent of auto-fire.
        public void Fire()
        {
            if (Weapon != null) Weapon.TryFire();
        }

        /// Instantly kill this subject for testing the death reaction, without needing a second shooter aimed at it.
        public void Kill()
        {
            if (Health != null) Health.Kill();
        }

        /// Refill health and clear i-frames — the real <see cref="Health.Revive"/>, no object recreation needed.
        public void Respawn()
        {
            if (Health != null) Health.Revive();
        }
    }
}
