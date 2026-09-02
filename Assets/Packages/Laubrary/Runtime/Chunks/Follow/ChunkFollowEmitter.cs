using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// <summary>Which way the burst direction points on each tick. This is the ONE direction decision this
    /// component makes; what a module then does with it (aim a spray, angle a spawned blast) is the module's
    /// own business, read off <see cref="ChunkModuleContext.DirectionDeg"/>.</summary>
    public enum ChunkFollowAim
    {
        /// The reverse of the target's travel — debris thrown out BEHIND a moving character. The default, and
        /// the case the design doc describes.
        Behind = 0,
        /// Along the target's travel — a jet/thrust reading, spray thrown out in front.
        Ahead = 1,
        /// Ignore travel entirely and fire with the spec's own authored directionDeg, exactly as a one-shot
        /// burst would.
        SpecDirection = 2,
    }

    /// <summary>
    /// Standalone module #9 (CHUNKS_OVERHAUL_DESIGN.md "Standalone modules"): the CONTINUOUS counterpart to
    /// <see cref="ChunkEmitter"/>. Every other Chunks entry point is strictly one-shot — fire once, done. This
    /// one chases a moving Transform and periodically flares up behind it: it tracks the target every frame,
    /// sprays the spec's Particle Splash counter to the target's travel, and re-fires the spec's Pyre Spawner
    /// on an interval at the tracked position.
    ///
    /// It is a SIBLING of <see cref="ChunkEmitter"/>, not a replacement, and it reads as one on purpose: same
    /// `spec` + `sortingOrder` + optional palette shape, same "make a container, put a
    /// <see cref="ChunkModuleRunner"/> on it, build a <see cref="ChunkModuleContext"/>, hand it to modules"
    /// sequence. The two differences are both consequences of being continuous:
    ///  • the container is built ONCE at <see cref="Play"/> and lives for the whole emission (a one-shot makes
    ///    one per burst; making one per interval here would litter the scene with a runner per tick), and
    ///  • it does NOT go through <see cref="ChunkModules.Run"/>. Run fires every enabled module once, through
    ///    the timeline — which is precisely the one-shot semantics. A follow emitter calls the two modules it
    ///    drives directly, on their own independent intervals, which is a different thing and would corrupt a
    ///    timeline's meaning (a delay measured from "burst start" has no meaning when there is no single
    ///    start). Fragments/formations are deliberately not driven here: repeating a formation every interval
    ///    is a different feature, and this component's contract is the two modules the design names.
    ///
    /// Everything is optional and independently switchable: it works with a spec that has ONLY the splash on,
    /// ONLY the spawn on, or both.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChunkFollowEmitter : MonoBehaviour
    {
        // ── what it follows and what it fires ────────────────────────────────────
        [Tooltip("The transform this emitter follows. Leave empty to follow its own transform.")]
        public Transform target;

        [Tooltip("The burst recipe this emitter repeatedly fires.")]
        public ChunkSpec spec;

        [Tooltip("Sorting order every spawned particle/blast falls back to when the spec has no layer stack.")]
        public int sortingOrder = ChunkEmitter.DefaultSortingOrder;

        // ── playback ─────────────────────────────────────────────────────────────
        [Tooltip("Start emitting as soon as this component wakes up. Off means nothing happens until game code " +
                 "calls Play().")]
        public bool playOnAwake = true;

        [Tooltip("Total emission time in seconds. 0 = keep going until Stop() is called.")]
        [Min(0f)] public float duration = 0f;

        // ── travel direction ─────────────────────────────────────────────────────
        [Tooltip("Which way the burst direction points: opposite the target's travel (debris thrown behind), " +
                 "along it, or the spec's own fixed direction.")]
        public ChunkFollowAim aim = ChunkFollowAim.Behind;

        [Tooltip("How long the measured direction takes to catch up with a change of course, in seconds. " +
                 "Higher = steadier but laggier; 0 = raw per-frame delta, which jitters.")]
        [Min(0f)] public float directionSmoothing = 0.1f;

        [Tooltip("Speed below which the target counts as standing still, world units/sec. Under it the last " +
                 "good direction is HELD instead of recomputed from a meaningless near-zero movement.")]
        [Min(0f)] public float minTravelSpeed = 0.05f;

        [Tooltip("Only emit while the target is actually moving faster than the minimum speed above. Off (the " +
                 "default) keeps emitting from a standing target, aimed along the last direction it travelled.")]
        public bool emitOnlyWhileMoving = false;

        // ── the two things it repeats ────────────────────────────────────────────
        [Tooltip("Spray the spec's Particle Splash on an interval. Does nothing unless the spec's Particle " +
                 "Splash module is itself enabled.")]
        public bool spraySplash = true;

        [Tooltip("Seconds between splash sprays. Small values read as a continuous trail, larger ones as puffs.")]
        [Min(0.01f)] public float splashInterval = 0.08f;

        [Tooltip("Re-fire the spec's Pyre Spawner on an interval. Does nothing unless the spec's Pyre Spawn " +
                 "module is itself enabled.")]
        public bool repeatSpawn = true;

        [Tooltip("Seconds between spawns — the rate this emitter 'flares up' behind the target.")]
        [Min(0.02f)] public float spawnInterval = 0.5f;

        // ── live state (never serialized: all of it is per-play) ─────────────────
        [System.NonSerialized] Transform _container;
        [System.NonSerialized] ChunkModuleRunner _runner;
        [System.NonSerialized] readonly ChunkTravelDirection _travel = new ChunkTravelDirection();
        [System.NonSerialized] IList<Color32> _palette;
        [System.NonSerialized] bool _playing;
        [System.NonSerialized] bool _followsExternal;
        [System.NonSerialized] float _elapsed;
        [System.NonSerialized] float _splashTimer;
        [System.NonSerialized] float _spawnTimer;
        [System.NonSerialized] int _spawnOrderOffset;

        /// Draw-order offsets cycle rather than climbing forever: OrderFor(layer, offset) adds the offset to
        /// the resolved order, so an ever-increasing counter on a continuous emitter would walk the sorting
        /// order off into the thousands within a minute. A short cycle keeps consecutive flares distinguishable
        /// in depth (which is all the offset is for) without drifting.
        const int OrderCycle = 8;

        /// Whether this emitter is currently emitting.
        public bool IsPlaying => _playing;

        /// The container everything spawned lives under while playing, or null when stopped. Exposed so a
        /// caller can inspect/parent against it; it is owned by this component and destroyed by Stop().
        public Transform Container => _container;

        /// The live travel measurement, for a caller that wants the same direction this emitter is firing with
        /// (a thruster flame aligning itself, a debug gizmo). Read-only in spirit — this component owns it.
        public ChunkTravelDirection Travel => _travel;

        /// The transform actually followed: the assigned target, or this component's own transform when none
        /// is assigned. Null only when an assigned target has been destroyed.
        public Transform FollowTarget => target != null ? target : (_followsExternal ? null : transform);

        /// Whether anything at all can be emitted right now: a spec is assigned and at least one of the two
        /// modules this emitter drives is switched on in it. The editor surfaces this; a caller can check it
        /// before wondering why Play() looked like it did nothing.
        public bool CanEmit => spec != null && (spec.Has<PaletteSplash>() || spec.Has<PyreBlast>());

        /// Whether the spec's Particle Splash will actually AIM at the direction this emitter computes. It only
        /// does so when the splash's own `inheritBurstDirection` is on — otherwise the splash uses its own
        /// authored angle and "spray backwards" silently does nothing visible. This component deliberately
        /// does NOT flip that flag on the user's behalf (it is another module's authored setting, and a tool
        /// that edits data you did not point it at is worse than one that tells you); it reports the state so
        /// the UI can show it.
        public bool SplashWillAim
        {
            get
            {
                var splash = spec != null ? spec.FirstEnabled<PaletteSplash>() : null;
                return splash != null && splash.inheritBurstDirection;
            }
        }

        /// Colours sampled off whatever this emitter represents, handed to modules exactly as
        /// ChunkEmitter.Burst's palette overload does. Optional — null means "the modules resolve their own
        /// colours", which is the normal case.
        public void SetPalette(IList<Color32> palette) => _palette = palette;

        void Awake() { if (playOnAwake) Play(); }

        // fromDestroy: Unity forbids DestroyImmediate from inside OnDestroy, and an edit-mode container can
        // only exist if something called Play() outside play mode — so on this path we just let go of it.
        void OnDestroy() => TearDownContainer(true);

        void OnValidate()
        {
            splashInterval = Mathf.Max(0.01f, splashInterval);
            spawnInterval = Mathf.Max(0.02f, spawnInterval);
            duration = Mathf.Max(0f, duration);
            directionSmoothing = Mathf.Max(0f, directionSmoothing);
            minTravelSpeed = Mathf.Max(0f, minTravelSpeed);
        }

        /// <summary>Start (or restart) emitting. Builds the one container + runner this emitter uses for its
        /// whole life, re-anchors the travel measurement on the target's current position (so the first frame
        /// does not measure the distance from wherever it last was as one frame of travel), and resets the
        /// spawner's randomness ONCE — per PyreSpawnModule.ResetRandom's own contract, a driver calling
        /// SpawnOne itself must reset at start rather than per spawn, or a seeded spawner would restart its
        /// sequence on every single tick and every flare would come out identical.</summary>
        public void Play()
        {
            if (spec == null) return;

            Stop();

            _followsExternal = target != null;
            Transform t = FollowTarget;
            if (t == null) return; // an assigned-but-destroyed target: nothing to follow, so nothing to build

            EnsureContainer(t.position);

            _travel.smoothingSeconds = directionSmoothing;
            _travel.minSpeed = minTravelSpeed;
            _travel.Reset(t.position);

            spec.FirstEnabled<PyreBlast>()?.ResetRandom();

            _elapsed = 0f;
            _splashTimer = 0f;
            _spawnTimer = 0f;
            _spawnOrderOffset = 0;
            _playing = true;
        }

        /// <summary>Stop emitting AND tear down everything this emitter spawned — the container goes, and every
        /// particle parented under it goes with it, so one emitter's output is one thing to clean up. Spawned
        /// blasts are NOT parented under the container (they come from, and return to, their own pool — see
        /// ChunkModuleRunner's note on why Chunks never bolts anything onto a pooled object), so they are left
        /// to finish and recycle themselves normally. Safe to call at any time, including when not playing.</summary>
        public void Stop()
        {
            _playing = false;
            TearDownContainer();
        }

        /// <summary>Stop emitting but let what is already in the air finish: the container survives until its
        /// contents have had time to die, then is destroyed. This is what "the character stopped boosting"
        /// wants — cutting a trail off mid-air by deleting its particles reads as a glitch, not as stopping.
        /// The grace period is the spec's own longest debris lifetime plus slack, reusing
        /// ChunkEmitter.ContainerLifetime so the two entry points cannot drift apart on that number.</summary>
        public void StopEmitting()
        {
            if (!_playing) return;
            _playing = false;
            if (_container != null)
            {
                var doomed = _container.gameObject;
                _container = null;
                _runner = null;
                Destroy(doomed, ChunkEmitter.ContainerLifetime(spec));
            }
        }

        void EnsureContainer(Vector3 position)
        {
            if (_container != null) return;

            var go = new GameObject("ChunkFollowBurst");
            _container = go.transform;
            // Deliberately a ROOT object that never moves: debris is left BEHIND a moving target, so parenting
            // the container to the target (or to this emitter, which is usually on it) would drag every
            // already-spawned particle along with the character and the trail would never fall behind. Its own
            // position is cosmetic — every module places what it spawns at ctx.Origin, which is re-read from
            // the tracked transform on each tick.
            _container.position = position;
            _runner = go.AddComponent<ChunkModuleRunner>();
        }

        void TearDownContainer(bool fromDestroy = false)
        {
            if (_container == null) { _runner = null; return; }
            var doomed = _container.gameObject;
            _container = null;
            _runner = null;
            if (Application.isPlaying) Destroy(doomed);
            else if (!fromDestroy) DestroyImmediate(doomed);
        }

        void Update()
        {
            if (!_playing) return;

            // A destroyed target is a normal end, not an error: stop cleanly and take our container with us
            // rather than throwing a MissingReferenceException every frame or leaving particles orphaned.
            if (_followsExternal && target == null) { Stop(); return; }
            if (spec == null) { Stop(); return; }

            Transform t = FollowTarget;
            if (t == null) { Stop(); return; }

            float dt = Time.deltaTime;
            _elapsed += dt;

            // Dials are live: a value changed in the inspector mid-play takes effect on the next frame rather
            // than needing a restart, which is what makes tuning the aim possible at all.
            _travel.smoothingSeconds = directionSmoothing;
            _travel.minSpeed = minTravelSpeed;
            bool moving = _travel.Sample(t.position, dt);

            if (!emitOnlyWhileMoving || moving)
            {
                Vector3 pos = t.position;
                float dirDeg = ResolveDirectionDeg();

                if (spraySplash) TickSplash(dt, pos, dirDeg);
                else _splashTimer = 0f;

                if (repeatSpawn) TickSpawn(dt, pos, dirDeg);
                else _spawnTimer = 0f;
            }

            // Duration is checked AFTER the tick so a short duration still gets its first emission out; a
            // check first would make duration == one frame emit nothing at all.
            if (duration > 0f && _elapsed >= duration) Stop();
        }

        /// The burst direction handed to modules this tick. Behind/Ahead fall back to the spec's own direction
        /// until the tracker has ever seen real movement — aiming at a direction that does not exist yet is how
        /// a stationary start ends up spraying due east.
        float ResolveDirectionDeg()
        {
            float specDeg = spec != null ? spec.directionDeg : 90f;
            switch (aim)
            {
                case ChunkFollowAim.Behind:
                    return _travel.HasDirection ? _travel.ReverseDirectionDeg : specDeg;
                case ChunkFollowAim.Ahead:
                    return _travel.HasDirection ? _travel.DirectionDeg : specDeg;
                default:
                    return specDeg;
            }
        }

        /// INTERVAL, not a per-frame rate. Both halves of this component repeat, and an interval says the same
        /// thing on both ("a puff every 0.06s", "a flare every 0.5s"), which a rate does not — a per-frame
        /// "spray N particles per frame" ties density to frame rate, and a per-frame "N per second" has to
        /// accumulate a fractional remainder anyway, which is an interval with extra steps. An interval is also
        /// the number an author can actually picture.
        void TickSplash(float dt, Vector3 pos, float dirDeg)
        {
            var splash = spec.FirstEnabled<PaletteSplash>();
            if (splash == null) return;

            float interval = Mathf.Max(0.01f, splashInterval);
            _splashTimer += dt;
            if (_splashTimer < interval) return;
            // One spray per tick even after a long hitch (a domain reload, a loading spike): catching up on a
            // half-second of missed intervals all in one frame is a visual explosion, not a trail.
            _splashTimer = _splashTimer >= interval * 2f ? 0f : _splashTimer - interval;

            splash.Fire(BuildContext(pos, dirDeg));
        }

        void TickSpawn(float dt, Vector3 pos, float dirDeg)
        {
            var blast = spec.FirstEnabled<PyreBlast>();
            if (blast == null) return;

            float interval = Mathf.Max(0.02f, spawnInterval);
            _spawnTimer += dt;
            if (_spawnTimer < interval) return;
            _spawnTimer = _spawnTimer >= interval * 2f ? 0f : _spawnTimer - interval;

            // SpawnOne, not Fire: Fire would ResetRandom on every single spawn (making a seeded blast repeat
            // one identical flare forever) and would lay out its whole pattern again each tick.
            blast.SpawnOne(BuildContext(pos, dirDeg), pos + (Vector3)blast.offset, _spawnOrderOffset);
            _spawnOrderOffset = (_spawnOrderOffset + 1) % OrderCycle;
        }

        /// One context per tick, rebuilt because its whole point is that Origin and DirectionDeg are current.
        /// Draw order is NEVER stamped here — it goes out as the spec's layer stack plus this emitter's flat
        /// sortingOrder fallback, and ChunkModuleContext.OrderFor/ApplyOrder makes that decision for every
        /// module in one place.
        ChunkModuleContext BuildContext(Vector3 pos, float dirDeg)
            => new ChunkModuleContext(pos, _container, dirDeg, spec, spec.ResolveLayers(), sortingOrder,
                                      _runner, _palette);

#if UNITY_EDITOR
        /// The aim is the single most likely thing to LOOK broken (a spray pointing the wrong way reads as a
        /// bug in the spec, not as a stationary target holding its last direction), so while playing the
        /// emitter draws the direction it is actually firing with.
        void OnDrawGizmosSelected()
        {
            if (!_playing) return;
            Transform t = FollowTarget;
            if (t == null) return;

            float rad = ResolveDirectionDeg() * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            Gizmos.color = _travel.IsMoving ? Color.cyan : new Color(0.4f, 0.5f, 0.55f);
            Gizmos.DrawLine(t.position, t.position + dir);
            Gizmos.DrawWireSphere(t.position + dir, 0.08f);
        }
#endif
    }
}
