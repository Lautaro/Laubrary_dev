using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Zounds;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Plays a <see cref="WeaponDef"/>'s muzzle effect on every successful shot, at wherever <paramref name="muzzle"/>
    /// (an "~Muzzle" child <see cref="Transform"/>, kept live-tracked by <see cref="MuzzleTracker"/>/
    /// <see cref="MuzzleVectorTracker"/> when a "Muzzle" MetaLayer is painted, or left at
    /// <see cref="WeaponDef.muzzleOffset"/> when it isn't — see those classes' own doc comments) currently is —
    /// <c>OnEnable</c>/<c>OnDisable</c> register/unregister, which is what makes weapon-slot switching correct for
    /// free: deactivating a slot automatically drops its muzzle cue, no extra bookkeeping needed elsewhere.
    ///
    /// <para>WHEN the VFX plays and WHERE it renders used to be conflated by going through
    /// <see cref="ICueSink"/>/<c>OnMetaLayerReached</c>: a composite Zoe (one WITH an ICueSink) only got its
    /// muzzle flash the instant animation playback reached a frame carrying real "Muzzle" data, so an unpainted
    /// layer meant the flash silently never played at all — the exact bug <see cref="MuzzleTracker"/>'s own doc
    /// comment already warns against ("a presentation omission breaking gameplay"), just not yet fixed here. Now
    /// timing and position are separate questions like everywhere else: this fires on every shot, unconditionally,
    /// and reads whatever position <paramref name="muzzle"/> already has this frame.</para>
    ///
    /// <para>An explicit <see cref="WeaponDef.muzzle"/> plus a non-empty <c>muzzleEventName</c> (from the
    /// equipping Zoe's <c>ZoeWeaponSlot</c>) is a deliberate OPT-IN to the old frame-precise behavior instead —
    /// syncing the flash to one authored pixel on a specific reaction/clip frame, via whatever ICueSink is
    /// available. That opt-in still requires the event to actually be painted; it is for someone who wants
    /// exact animation-frame timing and is authoring for it, not the default path.</para>
    ///
    /// Also plays <see cref="WeaponDef.fireZoundName"/> (if any) on the SAME <see cref="ProjectileWeapon.Fired"/>
    /// event, once per successful shot, since ZoundEngine.PlayZound is name-only (no position to relay), so
    /// there's no positional-cue equivalent for audio the way there is for the muzzle VFX.
    ///
    /// Also asks the shooter to show a fire STATE, once per successful shot — something happening to ITS OWN
    /// sprite each time it fires (a body-relight flash, a recoil pose), same shape hit/death already use. By
    /// default that is the reserved <see cref="FireEventId"/> name, asked for quietly, so it stays a harmless
    /// no-op for any Zoe that hasn't declared one. Game code that wants a different look per weapon (a lazer
    /// rifle showing "Shooting Lazer") puts an <see cref="IFireStateSource"/> on the character and chooses the
    /// name itself — see <c>RaiseFireState</c>.
    ///
    /// <para>The request carries the LIVE muzzle position and the shot's resolved aim, so a fire state's
    /// effects land at the gun barrel pointing along the shot rather than at the character's anchor pointing
    /// nowhere, which is all an un-positioned raise could ever say.</para>
    ///
    /// <para>COVERAGE, across every way a shot can be triggered: the hook is at the WEAPON, not at the input.
    /// It subscribes to <see cref="ProjectileWeapon.Fired"/> and <see cref="ProjectileWeapon.HitscanFired"/>,
    /// which are raised from inside <c>FireInternal</c>/<c>TryFireAt</c>/<c>TryHitscanAt</c> regardless of who
    /// pulled the trigger — player input via <c>ZoeWeaponDriver</c>, <c>autoFire</c>, a Daemon brain, Mirage's
    /// <c>fireWeapon</c> preview step, <c>TargetPracticeController</c>. So coverage does not depend on the
    /// game's PERSPECTIVE: the perspective-specific component is the MOTION driver, and firing sits downstream
    /// of <c>Combatant.aimDirection</c>, which every perspective already funnels into. Platformer, top-down,
    /// twin-stick and light-gun are the same path. The one gap worth knowing: nothing inside Laubrary calls
    /// <c>TryHitscanAt</c>, so the hitscan half is covered by construction but exercised only by consumer game
    /// code (or a deliberate synthetic call).</para>
    /// </summary>
    [AddComponentMenu("Laubrary/Zoetrope/Weapon Muzzle Cue")]
    public class WeaponMuzzleCue : MonoBehaviour
    {
        public WeaponDef def;
        public ProjectileWeapon weapon;
        [Tooltip("Where the muzzle VFX plays. Kept live-tracked by MuzzleTracker/MuzzleVectorTracker when a " +
                 "\"Muzzle\" layer is painted; falls back to this transform's own position (def.muzzleOffset) " +
                 "when it isn't.")]
        public Transform muzzle;
        /// Which MetaLayer/FrameEvent to register with the ICueSink — the equipping Zoe's OWN choice
        /// (ZoeWeaponSlot.muzzleLayerId/muzzleEventName), not anything stored on def. See Configure's own doc.
        string _muzzleLayerId, _muzzleEventName;

        /// Reserved state name a character declares a custom reaction under to have something happen to its
        /// OWN sprite each time it fires. The DEFAULT only — an <see cref="IFireStateSource"/> on the
        /// character replaces it outright, including with "nothing". See this class's own doc comment.
        public const string FireEventId = "Fire";

        readonly string _key = System.Guid.NewGuid().ToString("N");
        ICueSink _sink;
        System.Action<Projectile> _vfxHandler;
        System.Action<Vector3, int> _vfxHitscanHandler;
        System.Action<Projectile> _audioHandler;
        System.Action<Vector3, int> _hitscanAudio;
        ReactionFxPlayer _reactions;
        System.Action<Projectile> _fireReactionHandler;
        System.Action<Vector3, int> _fireReactionHitscanHandler;
        /// Game code's chooser for which state a shot shows, if it installed one. Resolved lazily — see
        /// RaiseFireState, where the ordering reason lives.
        IFireStateSource _fireState;
        /// The frame the fire state was last raised on, so a multi-projectile shot raises it ONCE. -1 rather
        /// than 0 because frame 0 is a real frame and a shot on it would otherwise be swallowed.
        int _lastFireStateFrame = -1;
        bool _configured;

        /// Call this right after AddComponent — OnEnable already ran before the caller could set fields the
        /// normal way, so this both sets them AND performs the initial registration immediately. muzzleLayerId/
        /// muzzleEventName come from the EQUIPPING ZOE's ZoeWeaponSlot, not from def — a WeaponDef itself
        /// carries no character-specific wiring, so the same weapon stays equippable by any character.
        public void Configure(WeaponDef def, ProjectileWeapon weapon, Transform muzzle,
                               string muzzleLayerId = "", string muzzleEventName = "")
        {
            this.def = def;
            this.weapon = weapon;
            this.muzzle = muzzle;
            _muzzleLayerId = muzzleLayerId;
            _muzzleEventName = muzzleEventName;
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
                if (!string.IsNullOrEmpty(_muzzleEventName))
                {
                    // Explicit opt-in to frame-precise timing: sync to one authored pixel on a specific
                    // reaction/clip frame, via whatever ICueSink is available. No sink (plain SpriteView) still
                    // needs a working flash, so it falls back to firing on every shot, same as the default path.
                    _sink = GetComponentInParent<ICueSink>();
                    if (_sink != null)
                    {
                        _sink.Register(_key, _muzzleLayerId, def.muzzle, _muzzleEventName);
                    }
                    else if (weapon != null)
                    {
                        _vfxHandler = _ => PlayMuzzleFx(weapon.ResolvedAimDirection());
                        weapon.Fired += _vfxHandler;
                    }
                }
                else if (weapon != null)
                {
                    // Default: fire on every successful shot, at wherever `muzzle` currently is — already kept
                    // live-tracked-or-fallback by MuzzleTracker/MuzzleVectorTracker (see this class's own doc
                    // comment). Same for every shooter, composite or not — no ICueSink branch needed here.
                    _vfxHandler = _ => PlayMuzzleFx(weapon.ResolvedAimDirection());
                    _vfxHitscanHandler = (target, __) =>
                        PlayMuzzleFx(((Vector2)(target - MuzzleTransform.position)).normalized);
                    weapon.Fired += _vfxHandler;
                    weapon.HitscanFired += _vfxHitscanHandler;
                }
            }

            if (weapon != null && !string.IsNullOrEmpty(def.fireZoundName))
            {
                _audioHandler = _ => ZoundEngine.PlayZound(def.fireZoundName);
                // A hitscan shot spawns no projectile, so ProjectileWeapon.Fired never raises for it and the
                // weapon would be silent. HitscanFired is the same event for a shot that does not travel.
                _hitscanAudio = (_, __) => ZoundEngine.PlayZound(def.fireZoundName);
                weapon.HitscanFired += _hitscanAudio;
                weapon.Fired += _audioHandler;
            }

            if (weapon != null)
            {
                // Both events land on ONE helper, so the projectile path and the instant-hit path can never
                // drift about which state gets asked for or what the request carries. The hitscan handler is
                // the only one that knows where the shot went, so it passes its target through.
                _fireReactionHandler = _ => RaiseFireState(hitscan: false, hitscanTarget: Vector3.zero);
                _fireReactionHitscanHandler = (target, __) => RaiseFireState(hitscan: true, hitscanTarget: target);
                weapon.Fired += _fireReactionHandler;
                weapon.HitscanFired += _fireReactionHitscanHandler;
            }
        }

        Transform MuzzleTransform => muzzle != null ? muzzle : transform;

        /// Play the weapon's OWN muzzle effect at the live muzzle, ORIENTED along the shot: turned to face
        /// <paramref name="aimDir"/> and mirrored when the shot points left, so a left-facing shot shows the
        /// mirrored flash rather than the right-facing one rotated upside down — the same orientation rule a
        /// Zoe Fire event applies to its own effects (EventContext.ResolveOrientation), so whichever of the two
        /// owns the flash, it points the same way. A zero aim plays it upright, as before.
        void PlayMuzzleFx(Vector2 aimDir)
        {
            if (def == null || def.muzzle == null) return;
            Vector2 at = MuzzleTransform.position;
            if (aimDir.sqrMagnitude <= 1e-6f) { def.muzzle.Play(at); return; }
            float aimDeg = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
            def.muzzle.PlayOriented(at, aimDeg, flipX: aimDir.x < 0f);
        }

        /// Ask the shooter to show its fire state for the shot that just happened — the whole of "let game
        /// code choose what firing looks like", in one place.
        ///
        /// <para>WHO CHOOSES THE NAME. No <see cref="IFireStateSource"/> on the character → the reserved
        /// <see cref="FireEventId"/>, exactly as before, asked for QUIETLY. One present → its answer is the
        /// name, asked for LOUDLY, and an empty answer asks for nothing at all. That pairing is the point:
        /// a name nobody chose may miss in silence, a name game code chose must never miss in silence.</para>
        void RaiseFireState(bool hitscan, Vector3 hitscanTarget)
        {
            // ONE raise per SHOT, not per projectile. ProjectileWeapon.FireInternal raises Fired once for
            // EVERY projectile, so a three-pellet shotgun asked its character for the fire state three times
            // in the same frame — the arbiter refuses two of the three clips while all three still fire the
            // reaction's Immediate effects, i.e. a triple muzzle burst nobody authored. Deliberately scoped to
            // the STATE raise only: the muzzle VFX and the fire zound registered above are per-projectile
            // today as well, and de-duplicating those is a separate visible change nobody asked for.
            if (_lastFireStateFrame == Time.frameCount) return;
            _lastFireStateFrame = Time.frameCount;

            // Resolved lazily via GetComponentInParent, same reasoning as _sink above — a spawner may add this
            // component before ReactionFxPlayer exists.
            if (_reactions == null) _reactions = GetComponentInParent<ReactionFxPlayer>();
            if (_reactions == null) return;

            // The LIVE muzzle, read this frame — the same transform the muzzle VFX plays at, so the state's
            // effects and the weapon's own flash land in the same place instead of one at the gun barrel and
            // one at the character's feet.
            var m = muzzle != null ? muzzle : transform;
            Vector2 origin = m.position;

            // The SHOT's aim, not any one pellet's (see ProjectileWeapon.ResolvedAimDirection). A hitscan shot
            // has no aim field to read — it was told a world point — so its facing is muzzle → target. A
            // degenerate target ON the muzzle normalises to zero, which reads downstream as "no facing", which
            // is the honest answer.
            Vector2 dir = hitscan
                ? ((Vector2)(hitscanTarget - m.position)).normalized
                : (weapon != null ? weapon.ResolvedAimDirection() : Vector2.zero);

            var req = new ReactionRequest(origin, dir, 0f, weapon != null ? weapon.gameObject : null);

            // Re-resolved while null rather than cached once, and it MUST be lazy: ZoeSpawner builds this
            // component during weapon equipping, before the game has had any chance to add a chooser of its
            // own. Same pattern _sink and _reactions use, for the same reason.
            if (_fireState == null) _fireState = GetComponentInParent<IFireStateSource>();

            if (_fireState == null)
            {
                // Nobody chose this name — it is the reserved one every weapon offers on spec, and a character
                // that never declared a Fire reaction is the normal case, not a mistake. TryRaise, so that
                // stays silent; plain Raise here would log for every character in every project on its first
                // shot.
                _reactions.TryRaise(FireEventId, req);
                return;
            }

            string state = _fireState.FireStateFor(new FireShot(weapon, def, origin, dir, hitscan));
            // "Nothing" means nothing. There is deliberately no fallback to FireEventId: game code that
            // installed a chooser is in charge, and a hidden fallback would show a state nobody asked for in
            // answer to a decision to ask for none. (Owner's decision — see IFireStateSource.)
            if (string.IsNullOrEmpty(state)) return;

            // LOUD: this name was deliberately chosen, so a character that does not declare it is a bug and
            // Raise says so once, listing what it does declare. The name goes to Raise — never to
            // view.PlayClip or arbiter.Play — so it resolves the WHOLE state card (clip, duration, body flash,
            // effect list) instead of being reduced to a bare clip.
            _reactions.Raise(state, req);
        }

        void UnregisterNow()
        {
            _sink?.Unregister(_key);
            _sink = null;
            if (weapon != null)
            {
                if (_vfxHandler != null) { weapon.Fired -= _vfxHandler; _vfxHandler = null; }
                if (_vfxHitscanHandler != null) { weapon.HitscanFired -= _vfxHitscanHandler; _vfxHitscanHandler = null; }
            }
            if (_audioHandler != null && weapon != null)
            {
                weapon.Fired -= _audioHandler;
                _audioHandler = null;
                if (_hitscanAudio != null) { weapon.HitscanFired -= _hitscanAudio; _hitscanAudio = null; }
            }
            if (weapon != null)
            {
                if (_fireReactionHandler != null) { weapon.Fired -= _fireReactionHandler; _fireReactionHandler = null; }
                if (_fireReactionHitscanHandler != null) { weapon.HitscanFired -= _fireReactionHitscanHandler; _fireReactionHitscanHandler = null; }
            }
        }
    }
}
