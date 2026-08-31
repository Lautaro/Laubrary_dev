using System;
using UnityEngine;
using UnityEngine.Pool;

namespace Laubrary.Pooling
{
    /// <summary>
    /// A shared, generic component pool — the same Request/Return shape Zounds already proved
    /// (<c>ZoundPool</c>), generalized so Pyre/Chunks/Combat2D don't each hand-roll their own. Wraps Unity's
    /// own <see cref="ObjectPool{T}"/> (built-in since 2021.1, no new dependency) rather than reimplementing
    /// pool mechanics. Every pooled-but-idle instance lives under one named container Transform — this is
    /// deliberate: it keeps the Hierarchy from jittering as objects come and go rapidly, since the churn is
    /// absorbed by one stable node's children instead of reordering the scene root's sibling list.
    /// </summary>
    public class ComponentPool<T> where T : Component
    {
        readonly ObjectPool<T> _pool;
        readonly Transform _root;

        public Transform Root => _root;
        public int CountActive => _pool.CountActive;
        public int CountInactive => _pool.CountInactive;

        /// <param name="containerName">Name of the shared container GameObject idle instances park under.</param>
        /// <param name="factory">Builds a brand-new instance (only called when the pool is empty).</param>
        /// <param name="onGet">Optional: reset/configure state every time an instance leaves the pool.</param>
        /// <param name="onRelease">Optional: run before an instance is deactivated and returned to the pool.</param>
        public ComponentPool(string containerName, Func<T> factory, Action<T> onGet = null, Action<T> onRelease = null,
                              int defaultCapacity = 8, int maxSize = 256)
        {
            var rootGo = new GameObject(containerName);
            // DontDestroyOnLoad is Play-mode-only — it throws outright in an editor script (T-0096: discovered
            // when the new Zoe palette Preview button, which fires a real reaction in Edit mode, hit a pooled
            // Pyre effect through this exact constructor). Surviving a scene load has no meaning in Edit mode
            // anyway (nothing is loading), so this is a pure Play-mode concern to skip, not a behaviour change
            // for the pool's actual job. Same root cause silently affected the pre-existing "Preview in Mirage"
            // button for any Zoe whose Hit/Death/custom-event effects route through a pooled Pyre/Chunks blast.
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(rootGo);
            _root = rootGo.transform;

            _pool = new ObjectPool<T>(
                createFunc: () =>
                {
                    var inst = factory();
                    inst.transform.SetParent(_root, false);
                    return inst;
                },
                actionOnGet: inst =>
                {
                    inst.gameObject.SetActive(true);
                    onGet?.Invoke(inst);
                },
                actionOnRelease: inst =>
                {
                    onRelease?.Invoke(inst);
                    inst.gameObject.SetActive(false);
                    inst.transform.SetParent(_root, false);
                },
                actionOnDestroy: inst =>
                {
                    if (inst != null) UnityEngine.Object.Destroy(inst.gameObject);
                },
                collectionCheck: false,   // a double-release shouldn't throw and crash a whole frame's fire loop
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);
        }

        public T Get() => _pool.Get();
        public void Release(T instance) => _pool.Release(instance);
    }
}
