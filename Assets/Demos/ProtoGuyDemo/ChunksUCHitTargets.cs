using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Zoetrope;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// Stands a fixed set of characters still in front of ProtoGuy so their HURT reaction can be judged from a
    /// real shot — the counterpart to <see cref="ChunksUCDeathSwitcher"/>, which judges the death reaction.
    ///
    /// Two things make a hurt reaction awkward to look at in the flying-disc demo, and this fixes both: the
    /// discs move (so a spray leaves frame before it can be read), and they die in a few shots (so what you
    /// actually see is the death reaction). A target here holds its position and is given enough health that
    /// the demo's weapon cannot kill it, so every shot lands as a hurt and nothing else.
    ///
    /// It spawns whatever character it is handed, so the same rig shows one recipe answering to two different
    /// characters side by side: which recipe plays is the CHARACTER's hurt row, never anything this component
    /// decides — a demo that chose the effect itself would be testing the demo, not the character.
    /// </summary>
    public class ChunksUCHitTargets : MonoBehaviour
    {
        [System.Serializable]
        public class Target
        {
            [Tooltip("Character to stand here. Its own hurt row owns what a hit looks like.")]
            public Zoe zoe;
            [Tooltip("Where it stands, relative to this object.")]
            public Vector2 position;
        }

        [Tooltip("One entry per character standing in the line-up.")]
        public List<Target> targets = new();

        [Tooltip("Health each target is given, replacing its own. High enough that the demo's weapon cannot " +
                 "kill it, so every shot shows the hurt reaction instead of the death one.")]
        [Min(1f)] public float health = 100000f;

        readonly List<GameObject> _spawned = new();

        /// The live targets, in the order they were authored — what a probe reads to tell one from the other.
        public IReadOnlyList<GameObject> Spawned => _spawned;

        void Start()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || t.zoe == null) continue;

                var go = ZoeSpawner.SpawnCharacter(t.zoe, transform.position + (Vector3)t.position, transform);
                go.name = "Hit Target " + i + " — " + t.zoe.name;

                // Overridden after spawn rather than on the asset: the characters here are shared with other
                // scenes, and a demo must never retune a ScriptableObject every other scene also loads.
                var hp = go.GetComponent<Health>();
                if (hp != null) { hp.maxHealth = health; hp.Revive(health); }

                _spawned.Add(go);
            }
        }
    }
}
