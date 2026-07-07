using UnityEngine;
using Laubrary.Colosseum;

namespace Laubrary.Codex
{
    /// A composition "recipe" for one combat character (enemy / NPC / the player's target): its stats, its look, and
    /// the VFX it plays when hit and when it dies. It REFERENCES the primitives (a Faction, Pyre blasts, Chunks
    /// bursts) rather than owning art, so a Character is a small portable data asset. The runtime side is built by a
    /// spawner that adds Colosseum's Combatant + Health + a view + a CombatPresenter wired to these VFX.
    ///
    /// Look is a placeholder Sprite for now; the Zoetrope Zoe swap-point is marked below.
    [CreateAssetMenu(menuName = "Laubrary/Codex/Character", fileName = "Character")]
    public class CharacterDef : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Character";

        [Header("Stats")]
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("Team this character belongs to (drives who can hurt it). A null faction is an unaligned hazard.")]
        public Faction faction;
        [Tooltip("Seconds of invulnerability after a hit (stops one shot dealing many hits). 0 = none.")]
        public float invulnerableAfterHit = 0f;

        [Header("Look (placeholder — Zoe swap later)")]
        [Tooltip("Idle sprite for now. TODO: swap for a Zoetrope Zoe (idle/hit/death clips) once wired.")]
        public Sprite idleSprite;
        public float spriteScale = 1f;

        [Header("VFX")]
        [Tooltip("Played at the hit point every time this character takes damage.")]
        public CombatVfx hit = new CombatVfx();
        [Tooltip("Played once at the killing-blow point when this character dies.")]
        public CombatVfx death = new CombatVfx();

        // TODO(zounds): onHit / onDied Zound refs — embedded + registered if the Zounds engine is present.
    }
}
