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

        void Start()
        {
            if (zoeDef == null) { Debug.LogError("BarbarianDemo: zoeDef not assigned."); return; }
            ZoeSpawner.SpawnCharacter(zoeDef, transform.position);
        }
    }
}
