using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// A composition "recipe" for one combat character (enemy / NPC / the player's target): its stats, its look, and
    /// the effects it plays when hit and when it dies. Look and effects are PLUGGABLE (<c>[SerializeReference]</c>),
    /// so this core asset depends on Combat2D ONLY — a project supplies concrete views (a sprite, a Launimator-driven
    /// ZonedReelView, a Lazor shape) and effects (a Pyre blast + Chunks debris) from whatever bridge modules it includes.
    /// A small portable data asset; the runtime is assembled by <see cref="ZoeSpawner.SpawnCharacter"/>.
    [CreateAssetMenu(menuName = "Laubrary/Zoetrope/Zoe", fileName = "Zoe")]
    public class Zoe : ScriptableObject, Laubrary.PreviewKit.IVisualPreview
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
        [Tooltip("Pluggable — a sprite by default; a Reel / Lazor view via a bridge module; a composite " +
                 "multi-part body via the Zoetrope.Launimator bridge's CompositeReelView.")]
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

        [Header("Reactions")]
        [Tooltip("What happens on a non-killing hit: the clip to play (via the view's IAnimatedView, if it " +
                 "provides one) plus the FX list triggered off that clip's frame events/meta-layers. Real " +
                 "character data, same as everything else on this asset — NOT a Mirage-only concept (Mirage's " +
                 "Target Practice mode is a testing convenience that USES this, it doesn't own it).")]
        public ReactionFx hit = new ReactionFx();
        [Tooltip("What happens on the killing blow — same shape as Hit above.")]
        public ReactionFx death = new ReactionFx();

        [Header("AI")]
        [Tooltip("Optional decision-making attached at spawn. The game supplies the agent body " +
                 "(movement/perception). Pluggable — a Daemon brain via the Zoetrope.Daemon bridge.")]
        [SerializeReference] public IBrainSpec brain;

        [Header("Loadout")]
        [Tooltip("Pluggable weapons + abilities the character can activate; triggered by the brain (enemies) " +
                 "or input (player) via the LoadoutController.")]
        [SerializeReference] public List<IActivatable> loadout = new List<IActivatable>();

        [Header("Weapons")]
        [Tooltip("Switchable slots, separate from Loadout above (which stays unused today). One child slot " +
                 "per weapon, only the active one enabled. Switch via the spawned character's WeaponSwitcher. " +
                 "Empty = no weapon slots at all (today's single-EquipWeapon callers still work).")]
        public List<WeaponDef> weapons = new List<WeaponDef>();
        [Min(0)] public int defaultActiveWeapon = 0;

        [Header("Cues")]
        [Tooltip("This character's own always-on cues (footstep dust, a cast sparkle, ...) — an animation " +
                 "MetaLayer triggers an effect at that point, via ICueSink, independent of whatever's equipped. " +
                 "Only takes effect if the view provides an ICueSink (e.g. ZonedReelView).")]
        public List<CueBinding> cues = new List<CueBinding>();

        // TODO(zounds): onHit / onDied Zound refs — embedded + registered if the Zounds engine is present.

        // ── IVisualPreview ──
        // A Zoe browser with no thumbnails is unusable for its actual job: picking the right character out of
        // a folder of them. The look lives in the pluggable view, so this asks the view — via the optional
        // IPreviewableView — rather than learning what a Reel is. A view with no art, or one that cannot
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
