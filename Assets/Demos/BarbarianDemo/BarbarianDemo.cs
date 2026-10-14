using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.Demos.BarbarianDemo
{
    /// <summary>
    /// Spawns the Barbarian Zoe at Play-time (same reason as ProtoGuySpawner: the spawn wires runtime-only state
    /// that a pre-baked scene object would lose).
    ///
    /// Everything else is the character's own data, edited in the Zoe window: its side-view player controller
    /// (movement along one line, facing, and the command list mapping buttons and stick directions to its
    /// actions), its walk/backpedal/stand rules, and each action's look, hold-still and travel.
    /// </summary>
    public class BarbarianDemo : MonoBehaviour
    {
        [Tooltip("The character to spawn.")]
        public Zoe zoeDef;

        [Header("CPU Barbarian")]
        [Tooltip("The CPU controlled character. Leave empty for the player alone.")]
        public Zoe enemyDef;
        [Tooltip("Where the CPU character stands, from the player's spawn point, in world units.")]
        public Vector2 enemyOffset = new Vector2(3.5f, 0f);
        [Tooltip("Colour the CPU character is drawn in, so it reads as the other fighter. White leaves the art as drawn.")]
        public Color enemyTint = new Color(1f, 0.62f, 0.6f, 1f);

        void Start()
        {
            if (zoeDef == null) { Debug.LogError("BarbarianDemo: zoeDef not assigned."); return; }
            var player = ZoeSpawner.SpawnCharacter(zoeDef, transform.position);
            if (enemyDef == null) return;

            // The CPU character is the same art drawn in a tint, standing on the same line and facing the player: the
            // mirror image. It has no controller, so its facing is held by a small driver of its own.
            var enemy = ZoeSpawner.SpawnCharacter(enemyDef, transform.position + (Vector3)enemyOffset);
            foreach (var sr in enemy.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = enemyTint;
            enemy.AddComponent<HoldFacing>().facing = Vector2.left;

            var duel = new GameObject("Barbarian duel").AddComponent<BarbarianDuel>();
            duel.player = player; duel.enemy = enemy;
        }
    }

    /// <summary>Keeps a character facing one way. Everything that reads aim (the walk and stand poses, the mirror) follows it.</summary>
    public sealed class HoldFacing : MonoBehaviour
    {
        public Vector2 facing = Vector2.left;
        Laubrary.Combat2D.Combatant _combatant;
        void Update()
        {
            if (_combatant == null) _combatant = GetComponent<Laubrary.Combat2D.Combatant>();
            if (_combatant != null) _combatant.aimDirection = facing;
        }
    }
}
