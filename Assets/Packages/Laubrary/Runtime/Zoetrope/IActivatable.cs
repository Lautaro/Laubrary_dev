using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A pluggable thing a character can ACTIVATE — a weapon (fires a projectile) or an ability (dash, shield, AoE,
    /// buff). Unified so a <see cref="Zoe"/> carries ONE loadout list of them, triggered by a Daemon brain
    /// (enemies) or by input (the player) — same loadout, two trigger sources. Concrete activatables are
    /// <c>[Serializable]</c> data; <see cref="Activate"/> is a seam the GAME implements for its own physics/effects,
    /// keeping Zoetrope engine-agnostic (mirrors <see cref="ICharacterView"/> / <see cref="ICombatFx"/>). Assigned
    /// via <c>[SerializeReference]</c>.
    /// </summary>
    public interface IActivatable
    {
        /// Display name (HUD / debug).
        string Name { get; }
        /// Seconds between activations (0 = no gate; the LoadoutController tracks the timer per slot).
        float Cooldown { get; }
        /// Fire/trigger the activatable from <paramref name="user"/> toward <paramref name="aimDir"/>.
        void Activate(GameObject user, Vector2 aimDir);
    }
}
