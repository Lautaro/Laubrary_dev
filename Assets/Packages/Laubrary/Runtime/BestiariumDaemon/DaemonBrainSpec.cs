using UnityEngine;
using Laubrary.Bestiarium;
using Laubrary.Daemon;

namespace Laubrary.BestiariumDaemon
{
    /// <summary>
    /// An <see cref="IBrainSpec"/> that gives a character a Daemon brain: attaches a <c>DaemonRunner</c> booted on a
    /// Brain graph + a BehaviourSet. The OPTIONAL Daemon bridge — pulled in only by projects using Daemon.
    /// Movement + perception (the <c>IAgentBody</c> / <c>IAgentConditions</c>) live in the GAME; if a component on the
    /// spawned object already implements them they're wired here, otherwise the game sets <c>runner.Body</c> after spawn.
    /// </summary>
    [System.Serializable]
    public class DaemonBrainSpec : IBrainSpec
    {
        [Tooltip("The Brain graph (Daemon) this agent runs.")]
        public Brain brain;
        [Tooltip("The behaviour set the brain enables/disables/tweaks.")]
        public BehaviourSet behaviours;

        public void Attach(GameObject host)
        {
            var runner = host.GetComponent<DaemonRunner>();
            if (runner == null) runner = host.AddComponent<DaemonRunner>();

            var body = host.GetComponent<IAgentBody>();
            if (body != null) runner.Body = body;
            var cond = host.GetComponent<IAgentConditions>();
            if (cond != null) runner.Conditions = cond;

            runner.Boot(brain, behaviours);
        }
    }
}
