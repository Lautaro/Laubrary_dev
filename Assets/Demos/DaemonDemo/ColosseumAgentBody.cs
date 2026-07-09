using System;
using UnityEngine;
using Laubrary.Daemon;
using Laubrary.Combat2D;
using Laubrary.Pyre;

namespace Laubrary.Demos.DaemonDemo
{
    // ── The integration seam: Daemon brain ↔ Colosseum body ↔ Pyre/Chunks VFX ↔ (optional) Zoetrope anim ────
    // One MonoBehaviour on the enemy that IS the agent's body + condition source. A Daemon Behaviour casts
    // ctx.Body to this to move / fire; the Brain's transition conditions call Evaluate(). It subscribes to
    // Colosseum's Health.Died to fire the death VFX (Pyre blast + Chunks debris) — the "react to combat via events,
    // don't poll" pattern. The Zoe hook is a plain Action<string> you wire to ZoePlayer.Play (kept out of this
    // assembly's deps so the demo needs no Zoe asset): body.onAnim = clip => zoePlayer.Play(clip);
    public class ColosseumAgentBody : MonoBehaviour, IAgentBody, IAgentConditions
    {
        public Transform Target;
        public float AttackRange = 3f;
        public float MoveSpeed = 3f;
        public BlastSpec deathBlast;                 // Pyre: played at death position
        public Laubrary.Chunks.ChunkSpec debris;     // Chunks: flung at death position
        public Action<string> onAnim;               // optional: wire to a ZoePlayer

        Combatant _combatant; Health _health; ProjectileWeapon _weapon;
        bool _refs, _hooked, _dead;
        string _status;

        // Resolve Colosseum components lazily so this works whether or not Awake/OnEnable have run.
        void EnsureRefs()
        {
            if (_refs) return;
            _combatant = GetComponent<Combatant>();
            _health = GetComponent<Health>();
            _weapon = GetComponentInChildren<ProjectileWeapon>();
            _refs = true;
            if (!_hooked && _health != null) { _health.Died += OnDied; _hooked = true; }
        }

        void OnEnable() { EnsureRefs(); }
        void OnDisable() { if (_hooked && _health != null) { _health.Died -= OnDied; _hooked = false; } }

        // ── IAgentBody ──
        public Vector2 Position => transform.position;
        public void SetStatus(string label) { _status = label; onAnim?.Invoke(label); }
        public string Status => _status;
        public bool Dead => _dead;

        // ── actuators the Behaviours call (they cast ctx.Body to ColosseumAgentBody) ──
        public void MoveToward(Vector2 target, float speedScale = 1f)
        {
            float dt = Time.deltaTime > 0f ? Time.deltaTime : 0.016f;
            Vector2 p = Vector2.MoveTowards(transform.position, target, MoveSpeed * speedScale * dt);
            transform.position = new Vector3(p.x, p.y, transform.position.z);
        }
        public void StepToTarget(float speedScale = 1f) { if (Target != null) MoveToward(Target.position, speedScale); }
        public bool FireAtTarget()
        {
            EnsureRefs();
            if (_weapon == null || Target == null) return false;
            return _weapon.TryFire((Vector2)(Target.position - transform.position));
        }
        public float DistanceToTarget => Target != null ? Vector2.Distance(transform.position, Target.position) : Mathf.Infinity;

        // ── IAgentConditions (the Brain's transition guards) ──
        public bool Evaluate(string expression, AgentContext ctx)
        {
            EnsureRefs();
            switch (expression)
            {
                case "inRange":    return Target != null && DistanceToTarget <= AttackRange;
                case "outOfRange": return Target == null || DistanceToTarget > AttackRange;
                case "hasTarget":  return Target != null;
                case "hpLow":      return _health != null && _health.Normalized < 0.3f;
                case "dead":       return _dead || (_health != null && _health.IsDead);
                default:           return false;
            }
        }

        // ── combat reaction: death → VFX (subscribe, don't poll) ──
        void OnDied(DamageInfo info) => SpawnDeathVfx();

        public void SpawnDeathVfx()
        {
            _dead = true;
            SetStatus("Death");
            Vector3 pos = transform.position;
            if (deathBlast != null)
            {
                var go = new GameObject("Blast");
                go.transform.position = pos;
                go.AddComponent<SpriteRenderer>().sortingOrder = 500;
                var bp = go.AddComponent<BlastPlayer>();
                bp.spec = deathBlast; bp.loop = false; bp.destroyOnFinish = true; bp.playOnAwake = true;
            }
            if (debris != null) Laubrary.Chunks.Chunks.Burst(pos, debris);
        }
    }
}
