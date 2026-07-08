using UnityEngine;

namespace Laubrary.Bestiarium
{
    /// <summary>
    /// A pluggable AI brain for a character: attaches the agent's decision-making to the spawned GameObject.
    /// The concrete implementation lives in an OPTIONAL bridge (<c>Bestiarium.Daemon</c> attaches a Daemon
    /// DaemonRunner), so Bestiarium core stays Combat2D-only and AI-engine-agnostic. Assigned via
    /// <c>[SerializeReference]</c> on <see cref="CharacterDef"/> — so a recipe is a full enemy: body + look +
    /// weapon + effects + brain.
    /// </summary>
    public interface IBrainSpec
    {
        /// Attach the brain to the freshly-spawned character. Movement/perception (the IAgentBody) come from the
        /// game; a bridge wires them if a component on the host already provides them.
        void Attach(GameObject host);
    }
}
