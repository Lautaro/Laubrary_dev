using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// A composition "recipe" for one combat character (enemy / NPC / the player's target): its stats, its look, and
    /// the effects it plays when hit and when it dies. Look and effects are PLUGGABLE (<c>[SerializeReference]</c>),
    /// so this core asset depends on Combat2D ONLY — a project supplies concrete views (a sprite, a Launimator-driven
    /// ZonedLauminaryView, a Lazor shape) and effects (a Pyre blast + Chunks debris) from whatever bridge modules it includes.
    /// A small portable data asset; the runtime is assembled by <see cref="ZoeSpawner.SpawnCharacter"/>.
    [CreateAssetMenu(menuName = "Laubrary/Zoetrope/Zoe", fileName = "Zoe")]
    public partial class Zoe : ScriptableObject, Laubrary.PreviewKit.IVisualPreview
    {
        [Header("Identity")]
        public string displayName = "New Zoe";

        [Header("Stats")]
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("Team this character belongs to (drives who can hurt it). A null faction is an unaligned hazard.")]
        public Faction faction;
        [Tooltip("Seconds of invulnerability after a hit (stops one shot dealing many hits). 0 = none.")]
        public float invulnerableAfterHit = 0f;

        [Header("Look")]
        [Tooltip("Pluggable — a sprite by default; a Lauminary / Lazor view via a bridge module; a composite " +
                 "multi-part body via the Zoetrope.Launimator bridge's CompositeLauminaryView.")]
        [SerializeReference] public ICharacterView view = new SpriteView();

        // hit/death used to be split across THREE disconnected areas: hit/death (VFX-only ICombatFx) and
        // hitReaction (clip-name-only IHitReaction) — the same "which clip plays on Hurt" question authored in
        // two different places that had to be kept in sync by hand. Unified: each reaction is one ReactionFx
        // (clip + the FX list triggered off that same clip's own authored frame events/meta-layers), so there's
        // exactly one place to author "what happens when this character gets hurt" (or dies).
        [Header("Death")]
        [Tooltip("What happens to the body once it dies.")]
        public DeathDisposal deathDisposal = DeathDisposal.WhenDeathClipEnds;
        [Tooltip("Seconds the body lingers before vanishing. Used by After Delay, and as the fallback when " +
                 "When Death Clip Ends has no death clip to wait for.")]
        [Min(0f)] public float deathLinger = 1.5f;

        [Tooltip("Confirm hits against the sprite's own alpha, so a shot that clips the collider's corner but " +
                 "misses the drawing is a miss. Needs no authoring — every sprite already knows which of its " +
                 "pixels are transparent.")]
        public bool pixelPerfectHits = true;

        [Header("Movement")]
        [Tooltip("Which clips play just for moving. Character data — a body walks the same whatever it holds.")]
        public Locomotion locomotion = new Locomotion();

        [Tooltip("Directional replacement for Locomotion above — an ordered condition→LauminationSet rule list " +
                 "resolved against MotionState (heading/aim/speed), instead of a fixed idle/move pair. " +
                 "Additive and unauthored by default (no rules = IsAuthored false): ZoeSpawner prefers this over " +
                 "Locomotion ONLY when authored, so an existing Zoe with no MotionPose is unaffected.")]
        public MotionPose motionPose = new MotionPose();

        [Header("Reactions")]
        [Tooltip("What happens on a non-killing hit: the clip to play (via the view's IAnimatedView, if it " +
                 "provides one) plus the FX list triggered off that clip's frame events/meta-layers. Real " +
                 "character data, same as everything else on this asset — NOT a Mirage-only concept (Mirage's " +
                 "Target Practice mode is a testing convenience that USES this, it doesn't own it).")]
        public ReactionFx hit = new ReactionFx();
        [Tooltip("What happens on the killing blow — same shape as Hit above.")]
        public ReactionFx death = new ReactionFx();

        [Header("Custom events")]
        [Tooltip("Reactions this character can play beyond hit and death — a teleport, a spawn, a taunt, a "+
                 "special attack. Each is raised BY NAME from gameplay code or an animation frame cue, and "+
                 "carries the same clip/body-FX/effect list the fixed reactions do.")]
        public List<NamedReaction> events = new List<NamedReaction>();

        /// The reaction registered under `id`, or null. Case-INSENSITIVE, matching the one comparison rule
        /// every other authored name in this module already uses (CueRelay's frame events and meta-layer ids,
        /// the Zoe window's own clip/part/meta-layer lookups, FxEntry.Resolve's override names). This used to
        /// be ordinal-exact, and that was the odd one out: "fire" typed in game code against a state declared
        /// as "Fire" resolved as an override name and failed as a state name, on the same request, which is
        /// precisely the "works here, mysteriously doesn't there" trap. Widening only ever makes a previously
        /// failing near-miss resolve — no id already matching stops matching — and a genuinely wrong name
        /// still misses, now loudly (see ReactionFxPlayer.Raise).
        public ReactionFx EventNamed(string id)
        {
            if (string.IsNullOrEmpty(id) || events == null) return null;
            for (int i = 0; i < events.Count; i++)
                if (events[i] != null &&
                    string.Equals(events[i].id, id, System.StringComparison.OrdinalIgnoreCase))
                    return events[i].reaction;
            return null;
        }

        /// Every declared event id, for a PICKER. The ids are typed once, here, where the event is declared;
        /// everywhere else — a frame cue, an effect list, a gameplay call — chooses from this list rather
        /// than accepting free text. A free-text key that only fails at runtime is a bug generator, and this
        /// project already has one on its books (Zoe Preview's clip field).
        public IEnumerable<string> EventIds
        {
            get
            {
                if (events == null) yield break;
                foreach (var e in events)
                    if (e != null && !string.IsNullOrEmpty(e.id)) yield return e.id;
            }
        }

        /// The full <see cref="NamedReaction"/> (id + role chip + reaction) declared under `id`, or null.
        /// Case-insensitive, same rule as <see cref="EventNamed"/>. Unlike EventNamed this returns the whole
        /// entry, so a caller can check its <see cref="NamedReaction.role"/> chip — used by
        /// <see cref="ReactionFxPlayer"/> to resolve an answered hurt/death look: a name that exists but
        /// isn't chipped for the question being asked is "no legal answer", not "play it anyway".
        public NamedReaction FindEvent(string id)
        {
            if (string.IsNullOrEmpty(id) || events == null) return null;
            for (int i = 0; i < events.Count; i++)
                if (events[i] != null && string.Equals(events[i].id, id, System.StringComparison.OrdinalIgnoreCase))
                    return events[i];
            return null;
        }

        /// Every declared event id carrying `role` — "which of this character's rows are legal answers to
        /// the hurt-look / death-look question". Built-in Hit/Death themselves carry their role by POSITION,
        /// not by a stored chip (see ZOE_PALETTE_TAKE.md — "the two built-in hurt and death slots get the
        /// equivalent chip drawn for them... rather than stored"), so they are not part of this list; they are
        /// always the implicit "no answer" fallback (see ReactionFxPlayer.OnHit/OnDeath).
        public IEnumerable<string> EventIdsWithRole(ReactionRole role)
        {
            if (events == null) yield break;
            foreach (var e in events)
                if (e != null && e.role == role && !string.IsNullOrEmpty(e.id)) yield return e.id;
        }

        /// Every declared state, as plain text — for someone writing gameplay code without Unity open in front
        /// of them (ZOE_PALETTE_BUILD_PLAN.md task 7: "each character can print its own list of states on
        /// demand"). Hit and Death are always legal (they're the built-in fallback, not name-raised), so they're
        /// listed for context but marked as such; every custom event id is exactly what <see cref="Raise"/>-style
        /// code should type, picker-sourced everywhere else. Deterministic order (declaration order) so pasting
        /// this into a comment or a chat doesn't reshuffle between calls.
        public string DescribeDeclaredStates()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(string.IsNullOrEmpty(displayName) ? name : displayName).Append(" — declared states:\n");
            sb.Append("  Hit    (built-in — plays automatically on a non-killing hit unless answered otherwise)\n");
            sb.Append("  Death  (built-in — plays automatically on the killing blow unless answered otherwise)\n");
            if (events == null || events.Count == 0)
            {
                sb.Append("  (no custom events declared)");
                return sb.ToString();
            }
            foreach (var e in events)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                string role = e.role == ReactionRole.None ? "" : $"  [{e.role} — legal answer]";
                sb.Append("  ").Append(e.id).Append(role).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        [Header("AI")]
        [Tooltip("Optional decision-making attached at spawn. The game supplies the agent body " +
                 "(movement/perception). Pluggable — a Daemon brain via the Zoetrope.Daemon bridge.")]
        [SerializeReference] public IBrainSpec brain;

        [Tooltip("Optional real input rig attached at spawn — a person drives this character instead of (or " +
                 "as well as) a brain. Pluggable — the reusable gamepad/keyboard stack via the " +
                 "Zoetrope.ZoeCharacter bridge. Its presence is what tells any spawner, Mirage included, that " +
                 "this Zoe has a player controller configured.")]
        [SerializeReference] public IPlayerControllerSpec playerController;

        [Tooltip("Which aiming technique this character's weapons use — where a shot starts and which way it " +
                 "goes. Pluggable, one shelf entry per technique (see IAimSpec). Unset = the muzzle painted on " +
                 "the animation, the long-standing behaviour.")]
        [SerializeReference] public IAimSpec aiming;

        [Header("Loadout")]
        [Tooltip("Pluggable weapons + abilities the character can activate; triggered by the brain (enemies) " +
                 "or input (player) via the LoadoutController.")]
        [SerializeReference] public List<IActivatable> loadout = new List<IActivatable>();

        [Header("Weapons")]
        [Tooltip("Switchable slots, separate from Loadout above (which stays unused today). One child slot " +
                 "per weapon, only the active one enabled. Switch via the spawned character's WeaponSwitcher. " +
                 "Empty = no weapon slots at all (today's single-EquipWeapon callers still work). Each slot " +
                 "names its OWN attach part + muzzle layer (see ZoeWeaponSlot) — a WeaponDef itself carries no " +
                 "character-specific wiring, so the same weapon asset can be equipped by any Zoe correctly.")]
        public List<ZoeWeaponSlot> weapons = new List<ZoeWeaponSlot>();
        [Min(0)] public int defaultActiveWeapon = 0;

        [Header("Cues")]
        [Tooltip("This character's own always-on cues (footstep dust, a cast sparkle, ...) — an animation " +
                 "MetaLayer triggers an effect at that point, via ICueSink, independent of whatever's equipped. " +
                 "Only takes effect if the view provides an ICueSink (e.g. ZonedLauminaryView).")]
        public List<CueBinding> cues = new List<CueBinding>();

        [Header("Metadata")]
        [Tooltip("Named shapes, points and areas on this character — a foot circle, a hurt box — in its own " +
                 "LOCAL UNITS, origin at its root (the point it stands on). Laubrary gives them no meaning and " +
                 "builds nothing from them by itself: game code looks a layer up by id and decides what it is " +
                 "for. Drawn over the character's picture with Edit metadata… in the Zoe window.")]
        public Laubrary.MetaMapper.MetaMapData meta =
            new Laubrary.MetaMapper.MetaMapData { space = Laubrary.MetaMapper.MapSpace.LocalUnits };

        // TODO(zounds): onHit / onDied Zound refs — embedded + registered if the Zounds engine is present.

        // ── IVisualPreview ──
        // A Zoe browser with no thumbnails is unusable for its actual job: picking the right character out of
        // a folder of them. The look lives in the pluggable view, so this asks the view — via the optional
        // IPreviewableView — rather than learning what a Lauminary is. A view with no art, or one that cannot
        // preview itself, yields null and the browser draws its blank.
        //
        // ANIMATED where it can be, because a walk cycle is most of what distinguishes one character sprite
        // from another, and a single still of a shambler and a runner look identical.
        Sprite[] PreviewSprites() => view is IPreviewableView p ? p.PreviewFrames() : System.Array.Empty<Sprite>();

        public Texture2D RenderPreviewTexture()
        {
            var f = PreviewSprites();
            return f.Length > 0 ? Laubrary.PreviewKit.PreviewTex.CropSprite(f[0]) : null;
        }

        public bool CanAnimatePreview => PreviewSprites().Length > 1;

        public float PreviewFps
        {
            get
            {
                float fps = view is IPreviewableView p ? p.PreviewFps : 0f;
                return fps > 0f ? fps : 12f;
            }
        }

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            var f = PreviewSprites();
            if (tex == null || f.Length <= 1) return;

            int i = Mathf.Abs((int)(time * PreviewFps)) % f.Length;
            // BlitInto refuses a size mismatch rather than throwing — a caller mid-repaint wants a skipped
            // frame, not an exception every frame.
            Laubrary.PreviewKit.PreviewTex.BlitInto(tex, f[i]);
        }
    }
}
