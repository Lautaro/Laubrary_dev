using UnityEngine;
using Laubrary.Loom;

namespace Laubrary.Daemon
{
    // One per agent (enemy). Boot() loads the behaviour set and starts the Brain; each frame it steps the Brain
    // graph (which flips/tweaks/swaps behaviours via the bridge) and then ticks the ACTIVE behaviours (which do the
    // moving/attacking through IAgentBody). The graph decides; the behaviours act. The heavy lifting — bookmarks,
    // parking, the undo stack, the traversal trace — is Loom's GraphRunner; this only supplies the agent context.
    [DisallowMultipleComponent]
    public class DaemonRunner : GraphRunner<BrainNode, AgentContext>
    {
        public Brain Brain;
        public BehaviourSet Behaviours;
        public IAgentBody Body;
        public IAgentConditions Conditions;

        public readonly BehaviourHost Host = new BehaviourHost();
        public readonly Blackboard Blackboard = new Blackboard();

        AgentContext _ctx;

        AgentContext Ctx()
        {
            if (_ctx == null)
            {
                _ctx = new AgentContext
                {
                    Runner = this, Body = Body, Host = Host, Bridge = Host,
                    Conditions = Conditions, Blackboard = Blackboard,
                };
                Host.Bind(_ctx);
            }
            _ctx.Body = Body;               // allow late-assigned body
            _ctx.Conditions = Conditions;
            _ctx.DeltaTime = Time.deltaTime;
            return _ctx;
        }

        // Start this agent: clone the behaviour set into the host and run the brain from its entry node.
        public void Boot(Brain brain = null, BehaviourSet behaviours = null)
        {
            if (brain != null) Brain = brain;
            if (behaviours != null) Behaviours = behaviours;
            Ctx();                          // build + bind the context first
            Host.Load(Behaviours);
            if (Brain != null) RunGraph(Brain);
        }

        protected override AgentContext MakeContext(IRunnableGraph<BrainNode> graph) => Ctx();

        // Step the brain (decisions) then tick the active behaviours (actions). Public so tests / a central
        // scheduler can drive it deterministically instead of Unity's Update.
        public void StepAgent()
        {
            Step();
            Host.TickActive(Ctx());
        }

        protected override void Update() => StepAgent();

        public override void Teardown()
        {
            Host.DeactivateAll();
            base.Teardown();
        }
    }
}
