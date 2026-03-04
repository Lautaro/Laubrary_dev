using System;
using UnityEngine;

namespace Laubrary.Overture
{
    /// <summary>
    /// The four transition phases of an OvertureState lifecycle.
    /// </summary>
    [Flags]
    public enum StatePhase
    {
        None     = 0,
        Entering = 1 << 0,
        Entered  = 1 << 1,
        Exiting  = 1 << 2,
        Exited   = 1 << 3,
    }

    /// <summary>
    /// Pairs a GameObject with explicit activation control for selected transition phases.
    /// controlledPhases: which phases this entry reacts to.
    /// activePhases: of those controlled phases, which ones set the GO active.
    /// Phases not in controlledPhases are left untouched.
    /// </summary>
    [Serializable]
    public class ManagedStateObject
    {
        public GameObject gameObject;
        public StatePhase controlledPhases;
        public StatePhase activePhases;
    }
}
