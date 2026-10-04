using System.Collections.Generic;
using UnityEngine;
using Laubrary.GoreLab;
using Laubrary.Zoetrope;
using Laubrary.ZoetropeLaunimator;

namespace Laubrary.GoreLabDemo
{
    /// <summary>
    /// Fills an arena with imps that wander in random directions, lets imps be added and removed, and gives every one the gore body that lets it be wounded.
    /// The imp does not know about gore: the body is added to it here.
    /// </summary>
    public sealed class GoreLabDemoSpawner : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("The imp character to spawn.")]
        public Zoe imp;
        [Tooltip("The gore rig for the imp: where its head and torso are on every frame.")]
        public GoreRig rig;

        [Header("Crowd")]
        [Tooltip("How many imps stand in the arena when the demo starts.")]
        public int startCount = 8;
        [Tooltip("The area the imps wander in: where their feet may go (lower left and upper right).")]
        public Vector2 arenaMin = new Vector2(-9f, -5.5f);
        public Vector2 arenaMax = new Vector2(9f, 3.5f);

        /// <summary>The imps in the order they were added; the first added is the first removed.</summary>
        public readonly List<GoreBody> bodies = new List<GoreBody>();
        int _serial;

        void Start()
        {
            if (imp == null || rig == null) { Debug.LogWarning("GoreLabDemoSpawner needs an imp and a rig.", this); return; }
            for (int i = 0; i < startCount; i++) Add();
        }

        /// <summary>Adds one imp at a random spot, walking in a random direction.</summary>
        public GoreBody Add()
        {
            if (imp == null || rig == null) return null;
            var pos = new Vector3(Random.Range(arenaMin.x, arenaMax.x), Random.Range(arenaMin.y, arenaMax.y), 0f);
            GameObject go = ZoeSpawner.SpawnCharacter(imp, pos, transform);
            go.name = "Imp " + (++_serial);
            var walker = go.AddComponent<GoreLabDemoWalker>();
            walker.arenaMin = arenaMin; walker.arenaMax = arenaMax;
            var body = GoreBody.Attach(go, rig);
            bodies.Add(body);
            return body;
        }

        /// <summary>Removes the imp that was added first. Returns false when there are none.</summary>
        public bool RemoveOldest()
        {
            while (bodies.Count > 0)
            {
                var b = bodies[0];
                bodies.RemoveAt(0);
                if (b == null) continue;
                Destroy(b.gameObject);
                return true;
            }
            return false;
        }

        public void ResetAll()
        {
            foreach (var b in bodies) if (b != null) b.ResetWounds();
        }
    }
}
