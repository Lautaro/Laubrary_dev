using System.Collections.Generic;
using Laubrary.Pooling;
using UnityEngine;

namespace Laubrary.Combat2D
{
    /// <summary>
    /// Projectiles are pooled PER PREFAB, not universally like Pyre/Chunks — different AmmoDefs genuinely
    /// have different visuals/colliders, so there's no single shared "shape" to reuse across all of them.
    /// One <see cref="ComponentPool{T}"/> gets created lazily per distinct prefab reference the first time
    /// it's fired.
    /// </summary>
    public static class ProjectilePool
    {
        static readonly Dictionary<Projectile, ComponentPool<Projectile>> _pools = new Dictionary<Projectile, ComponentPool<Projectile>>();

        static ComponentPool<Projectile> PoolFor(Projectile prefab)
        {
            if (!_pools.TryGetValue(prefab, out var pool))
            {
                pool = new ComponentPool<Projectile>(
                    $"~Pool: {prefab.name}",
                    factory: () => Object.Instantiate(prefab));
                _pools[prefab] = pool;
            }
            return pool;
        }

        public static Projectile Get(Projectile prefab)
        {
            var instance = PoolFor(prefab).Get();
            instance.SourcePrefab = prefab;
            return instance;
        }

        public static void Release(Projectile instance)
        {
            if (instance == null) return;
            if (instance.SourcePrefab == null) { Object.Destroy(instance.gameObject); return; }
            PoolFor(instance.SourcePrefab).Release(instance);
        }
    }
}
