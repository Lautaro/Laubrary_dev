using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// The one live component a Chunks burst puts on its own container, so modules have somewhere to run
    /// coroutines (a formation's stagger, a follow emitter's repeat) and somewhere to have arbitrary
    /// transforms moved for them.
    ///
    /// Why the motion half lives HERE and not as a component added to what was spawned: a spawned Pyre blast
    /// comes out of PyreBlastPool and goes back into it, so any component Chunks bolted onto it would survive
    /// into the NEXT, unrelated use of that pooled object. Driving the transform from the outside, and dropping
    /// the entry the moment the transform dies or is deactivated (which is exactly what the pool does on
    /// release), keeps the pooled object byte-identical to how Chunks found it.
    [DisallowMultipleComponent]
    public class ChunkModuleRunner : MonoBehaviour
    {
        /// One externally-driven moving thing: a transform Chunks did not create and must not add components to.
        struct MotionEntry
        {
            public Transform target;
            public Vector3 velocity;
            public float gravity;
            public float drag;
            public float age;
            public float life;
            public bool faceVelocity;
        }

        readonly List<MotionEntry> _motion = new List<MotionEntry>();

        /// Start driving target with the given physics for life seconds. A null target is ignored. life <= 0
        /// means "until the target dies", which is the right default for a Pyre blast that ends itself.
        public void Move(Transform target, Vector3 velocity, float gravity, float drag, float life,
                         bool faceVelocity = false)
        {
            if (target == null) return;
            _motion.Add(new MotionEntry
            {
                target = target, velocity = velocity, gravity = gravity, drag = drag,
                age = 0f, life = life, faceVelocity = faceVelocity
            });
        }

        /// Whether anything is currently being driven — the emitter uses this to know the burst is still busy.
        public int MovingCount => _motion.Count;

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _motion.Count - 1; i >= 0; i--)
            {
                var e = _motion[i];
                // The pool reclaims a finished blast by deactivating it; that is our cue to let go, and it is
                // why this check comes BEFORE any write — one frame of writing to a reclaimed object would
                // teleport whatever reuses it next.
                if (e.target == null || !e.target.gameObject.activeInHierarchy)
                {
                    _motion.RemoveAt(i);
                    continue;
                }

                e.age += dt;
                if (e.life > 0f && e.age >= e.life)
                {
                    _motion.RemoveAt(i);
                    continue;
                }

                e.velocity.y -= e.gravity * dt;
                if (e.drag > 0f) e.velocity *= Mathf.Exp(-e.drag * dt);
                e.target.position += e.velocity * dt;
                if (e.faceVelocity && e.velocity.sqrMagnitude > 1e-6f)
                    e.target.rotation = Quaternion.Euler(0f, 0f,
                        Mathf.Atan2(e.velocity.y, e.velocity.x) * Mathf.Rad2Deg);

                _motion[i] = e;
            }
        }
    }
}
