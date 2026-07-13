using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// A composition "recipe" for one combat character (enemy / NPC / the player's target): its stats, its look, and
    /// the effects it plays when hit and when it dies. Look and effects are PLUGGABLE (<c>[SerializeReference]</c>),
    /// so this core asset depends on Combat2D ONLY — a project supplies concrete views (a sprite, a Launimator-driven
    /// ReelView, a Lazor shape) and effects (a Pyre blast + Chunks debris) from whatever bridge modules it includes.
    /// A small portable data asset; the runtime is assembled by <see cref="Bestiary.SpawnCharacter"/>.
    [CreateAssetMenu(menuName = "Laubrary/Zoetrope/Zoe", fileName = "Zoe")]
    public class Zoe : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Zoe";

        [Header("Stats")]
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("Team this character belongs to (drives who can hurt it). A null faction is an unaligned hazard.")]
        public Faction faction;
        [Tooltip("Seconds of invulnerability after a hit (stops one shot dealing many hits). 0 = none.")]
        public float invulnerableAfterHit = 0f;

        [Header("Look (pluggable — a sprite by default; a Reel / Lazor view via a bridge module)")]
        [SerializeReference] public ICharacterView view = new SpriteView();

        [Header("Effects (pluggable — e.g. a Pyre blast + Chunks debris via the Zoetrope.Pyre bridge)")]
        [Tooltip("Played at the hit point every time this character takes damage.")]
        [SerializeReference] public ICombatFx hit;
        [Tooltip("Played once at the killing-blow point when this character dies.")]
        [SerializeReference] public ICombatFx death;

        [Header("AI (pluggable — a Daemon brain via the Zoetrope.Daemon bridge)")]
        [Tooltip("Optional decision-making attached at spawn. The game supplies the agent body (movement/perception).")]
        [SerializeReference] public IBrainSpec brain;

        [Header("Loadout (pluggable weapons + abilities the character can activate)")]
        [Tooltip("Weapons + abilities; triggered by the brain (enemies) or input (player) via the LoadoutController.")]
        [SerializeReference] public List<IActivatable> loadout = new List<IActivatable>();

        // TODO(zounds): onHit / onDied Zound refs — embedded + registered if the Zounds engine is present.
    }
}
