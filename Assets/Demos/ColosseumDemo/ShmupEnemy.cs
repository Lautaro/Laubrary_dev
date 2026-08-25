using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Pyre;
using PyreAsset = Laubrary.Pyre.Pyre;   // the class is shadowed by the namespace inside a Laubrary.* namespace
using Laubrary.Chunks;
using Laubrary.Choreographer;
using ChunksFx = Laubrary.Chunks.Chunks;   // the static class is shadowed by the namespace inside a namespaced file

namespace Laubrary.Demos.ColosseumShmup
{
    /// A choreographed enemy ship. The Choreographer sweeps its transform; this script handles combat: it's a
    /// Combatant (Enemy faction) with a Health + Hurtbox that the player's bullets damage, and it periodically
    /// fires its own weapon at the player. When Health raises Died it plays a random Pyre blast + a tinted Chunks
    /// burst and hides, then respawns as its choreography dancer loops back to the start — the same Choreographer +
    /// Pyre + Chunks trio as the Arena demo, now driven by real Colosseum health instead of a click counter.
    ///
    /// ZOE SWAP: the placeholder diamond sprite + circle hurtbox stands in for a Zoetrope Zoe, which would supply
    /// the animated sprite and a meta-layer for pixel-perfect hit detection (via Colosseum's IHitFilter seam).
    [RequireComponent(typeof(Health))]
    public class ShmupEnemy : MonoBehaviour
    {
        public Color tint = Color.white;
        public List<PyreAsset> deathBlasts;
        public ChunkSpec debris;
        public ProjectileWeapon weapon;
        public Transform playerTarget;
        public float fireInterval = 1.6f;
        public ChoreographyPlayer choreoPlayer;
        public int choreoIndex;
        public float blastScale = 1f;             // driven by the director's "explosion intensity" slider

        public event Action<ShmupEnemy> Killed;   // director listens (score)

        Health health;
        SpriteRenderer sr;
        Collider2D col;
        float fireTimer;
        bool dead;

        void Awake()
        {
            health = GetComponent<Health>();
            sr = GetComponent<SpriteRenderer>();
            col = GetComponent<Collider2D>();
        }

        void Start()
        {
            if (sr != null) sr.color = tint;
            health.Died += OnDied;
            fireTimer = UnityEngine.Random.Range(0f, fireInterval);
        }

        void OnDestroy() { if (health != null) health.Died -= OnDied; }

        void Update()
        {
            if (dead)
            {
                if (choreoPlayer != null && choreoPlayer.ProgressOf(choreoIndex) < 0.05f) Respawn();
                return;
            }

            if (weapon != null && playerTarget != null)
            {
                fireTimer -= Time.deltaTime;
                if (fireTimer <= 0f)
                {
                    fireTimer = Mathf.Max(0.15f, fireInterval);
                    Vector2 dir = (Vector2)(playerTarget.position - transform.position);
                    if (dir.y > -0.1f) dir.y = -1f;    // always at least somewhat downward
                    weapon.TryFire(dir);
                }
            }
        }

        void OnDied(DamageInfo info)
        {
            if (dead) return;
            dead = true;
            Vector3 c = transform.position;

            if (deathBlasts != null && deathBlasts.Count > 0)
                SpawnBlast(deathBlasts[UnityEngine.Random.Range(0, deathBlasts.Count)], c);

            if (debris != null)
            {
                Color32 t = tint;
                ChunksFx.Burst(c, debris, new Color32[] { t, t, t });
            }

            if (sr != null) sr.enabled = false;
            if (col != null) col.enabled = false;
            Killed?.Invoke(this);
        }

        void Respawn()
        {
            dead = false;
            health.Revive();
            if (sr != null) { sr.enabled = true; sr.color = tint; }
            if (col != null) col.enabled = true;
        }

        void SpawnBlast(PyreAsset s, Vector3 pos)
        {
            if (s == null) return;
            var go = new GameObject("Blast");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Mathf.Max(0.05f, blastScale);
            var bsr = go.AddComponent<SpriteRenderer>();
            bsr.sortingOrder = 500;
            var bp = go.AddComponent<PyreBlastPlayer>();
            bp.spec = s;
            bp.loop = false;
            bp.destroyOnFinish = true;
            bp.playOnAwake = true;
        }
    }
}
