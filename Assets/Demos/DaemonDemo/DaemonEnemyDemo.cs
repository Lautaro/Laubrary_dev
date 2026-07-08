using System.Collections.Generic;
using UnityEngine;
using Laubrary.Loom;
using Laubrary.Daemon;
using Laubrary.Colosseum;

namespace Laubrary.Demos.DaemonDemo
{
    // ── Example Behaviours (game-authored; the per-agent analogue of GameRules) ───────────────────────────
    // Each drives the Colosseum body it's cast from. A State node in the Brain enables one of these; the host
    // ticks the active one.

    [System.Serializable]
    public class SeekBehaviour : AgentBehaviour   // walk toward the target
    {
        public float SpeedScale = 1f;
        public override void OnActivate(AgentContext ctx) => ctx.Body?.SetStatus("Walk");
        public override void Tick(AgentContext ctx) => (ctx.Body as ColosseumAgentBody)?.StepToTarget(SpeedScale);
    }

    [System.Serializable]
    public class ShootBehaviour : AgentBehaviour  // fire the ProjectileWeapon at the target
    {
        public override void OnActivate(AgentContext ctx) => ctx.Body?.SetStatus("Attack");
        public override void Tick(AgentContext ctx) => (ctx.Body as ColosseumAgentBody)?.FireAtTarget();
    }

    // ── Assembly: build a complete Daemon-driven Colosseum enemy at a position ─────────────────────────────
    public static class DaemonEnemyDemo
    {
        // A default brain: Seek toward the target until in range, Shoot until out of range, forever.
        public static Brain DefaultBrain()
        {
            var brain = ScriptableObject.CreateInstance<Brain>();
            var entry = new EntryNode { Id = "entry" };
            var seek = new StateNode { Id = "seek", BehaviourId = "SeekBehaviour",
                Transitions = new List<Transition> { new Transition { Port = "toShoot", Condition = "inRange" } } };
            var shoot = new StateNode { Id = "shoot", BehaviourId = "ShootBehaviour",
                Transitions = new List<Transition> { new Transition { Port = "toSeek", Condition = "outOfRange" } } };
            brain.Nodes = new List<BrainNode> { entry, seek, shoot };
            brain.Edges = new List<Edge>
            {
                new Edge("entry", "out", "seek"),
                new Edge("seek", "toShoot", "shoot"),
                new Edge("shoot", "toSeek", "seek"),
            };
            brain.EntryId = "entry";
            return brain;
        }

        public static BehaviourSet DefaultBehaviours()
        {
            var set = ScriptableObject.CreateInstance<BehaviourSet>();
            set.Behaviours = new List<AgentBehaviour> { new SeekBehaviour(), new ShootBehaviour() };
            return set;
        }

        // Spawn: Colosseum body (Combatant+Health+Hurtbox) + the adapter + a DaemonRunner booted on the brain.
        // Pass a target to chase, a Faction, and optional VFX specs. Returns the enemy GameObject.
        public static GameObject Spawn(Vector3 pos, Faction faction, Transform target,
                                       Brain brain = null, BehaviourSet behaviours = null,
                                       float maxHealth = 30f, float attackRange = 3f)
        {
            var go = new GameObject("DaemonEnemy");
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f;
            var col = go.AddComponent<CircleCollider2D>(); col.isTrigger = true; col.radius = 0.5f;

            var combatant = go.AddComponent<Combatant>(); combatant.faction = faction;
            var health = go.AddComponent<Health>(); health.maxHealth = maxHealth;
            go.AddComponent<Hurtbox>();                     // owner auto-resolves to the Combatant

            var body = go.AddComponent<ColosseumAgentBody>();
            body.Target = target; body.AttackRange = attackRange;

            var runner = go.AddComponent<DaemonRunner>();
            runner.Body = body;
            runner.Conditions = body;
            runner.Boot(brain != null ? brain : DefaultBrain(), behaviours != null ? behaviours : DefaultBehaviours());
            return go;
        }
    }
}
