using UnityEngine;
using UnityEngine.InputSystem;
using Lautaro.Stats;

namespace Laubrary.SmartStats.Demo
{
    /// <summary>
    /// Demonstrates BoolStat with all three BoolStatModifier types and priority ordering.
    ///
    /// Only the modifier with the LOWEST priority number takes effect.
    ///
    ///   isAlive (base: true)
    ///   Z - Toggle Flip modifier        (priority  0) → inverts base to false
    ///   X - Toggle AlwaysTrue modifier  (priority  5) → forces true, loses to Flip at 0
    ///   C - Toggle AlwaysFalse modifier (priority -10) → forces false, beats everything
    ///   V - Remove all modifiers
    /// </summary>
    public class SmartStatsDemoBool : MonoBehaviour
    {
        [Header("Stats")]
        public BoolStat isAlive = new BoolStat(true);

        private BoolStatModifier flipMod;
        private BoolStatModifier alwaysTrueMod;
        private BoolStatModifier alwaysFalseMod;

        void Awake()
        {
            isAlive.Init();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.zKey.wasPressedThisFrame) ToggleFlip();
            if (kb.xKey.wasPressedThisFrame) ToggleAlwaysTrue();
            if (kb.cKey.wasPressedThisFrame) ToggleAlwaysFalse();
            if (kb.vKey.wasPressedThisFrame) ClearAllModifiers();
        }

        /// <summary>Flip at priority 0 — inverts baseValue (true → false). Loses to AlwaysFalse at -10.</summary>
        void ToggleFlip()
        {
            if (IsActive(flipMod))
            {
                flipMod.Remove();
                flipMod = null;
                Debug.Log($"<color=yellow>Removed Flip</color> | isAlive: {(bool)isAlive}");
            }
            else
            {
                flipMod = new BoolStatModifier("Flip (P:0)", BoolStatModifierBase.BoolStatModifierType.Flip);
                flipMod.priority = 0;
                isAlive.AddModifier(flipMod);
                Debug.Log($"<color=yellow>Added Flip (P:0)</color> | isAlive: {(bool)isAlive}");
            }
        }

        /// <summary>AlwaysTrue at priority 5 — forces true, but loses to Flip (P:0) and AlwaysFalse (P:-10).</summary>
        void ToggleAlwaysTrue()
        {
            if (IsActive(alwaysTrueMod))
            {
                alwaysTrueMod.Remove();
                alwaysTrueMod = null;
                Debug.Log($"<color=green>Removed AlwaysTrue</color> | isAlive: {(bool)isAlive}");
            }
            else
            {
                alwaysTrueMod = new BoolStatModifier("AlwaysTrue (P:5)", BoolStatModifierBase.BoolStatModifierType.AlwaysTrue);
                alwaysTrueMod.priority = 5;
                isAlive.AddModifier(alwaysTrueMod);
                Debug.Log($"<color=green>Added AlwaysTrue (P:5)</color> | isAlive: {(bool)isAlive}");
            }
        }

        /// <summary>AlwaysFalse at priority -10 — lowest number, always wins over all other modifiers.</summary>
        void ToggleAlwaysFalse()
        {
            if (IsActive(alwaysFalseMod))
            {
                alwaysFalseMod.Remove();
                alwaysFalseMod = null;
                Debug.Log($"<color=red>Removed AlwaysFalse</color> | isAlive: {(bool)isAlive}");
            }
            else
            {
                alwaysFalseMod = new BoolStatModifier("AlwaysFalse (P:-10)", BoolStatModifierBase.BoolStatModifierType.AlwaysFalse);
                alwaysFalseMod.priority = -10;
                isAlive.AddModifier(alwaysFalseMod);
                Debug.Log($"<color=red>Added AlwaysFalse (P:-10)</color> | isAlive: {(bool)isAlive}");
            }
        }

        /// <summary>Removes all active modifiers.</summary>
        public void ClearAllModifiers()
        {
            flipMod?.Remove();       flipMod = null;
            alwaysTrueMod?.Remove(); alwaysTrueMod = null;
            alwaysFalseMod?.Remove(); alwaysFalseMod = null;
            Debug.Log($"<color=white>Bool: Cleared all modifiers</color> | isAlive: {(bool)isAlive}");
        }

        void OnDestroy()
        {
            isAlive.Cleanup();
        }

        private static bool IsActive(BoolStatModifier mod) => mod != null && !mod.FlaggedForRemoval;
    }
}

