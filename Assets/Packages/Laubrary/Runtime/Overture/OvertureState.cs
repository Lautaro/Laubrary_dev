using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Laubrary.Overture
{
    /// <summary>
    /// Base class for all Overture states. Place on a GameObject that is a direct child
    /// of an OvertureManager or another OvertureState to form a hierarchical state machine.
    /// Override lifecycle hooks (OnEntering, OnEnter, OnState, OnExit, etc.) in subclasses.
    /// </summary>
    [DisallowMultipleComponent]
    public class OvertureState : MonoBehaviour
    {
        [Header("Managed GameObjects")]
        [Tooltip("GameObjects whose active state is explicitly controlled at each transition phase. " +
                 "Objects not listed follow the default: active while the state is active, inactive when fully exited.")]
        [SerializeField] private List<ManagedStateObject> managedObjects = new List<ManagedStateObject>();

        protected OvertureManager manager;
        private OvertureVisual[] childVisuals;
        private HashSet<GameObject> managedGoCache;

        /// <summary>The OvertureManager that owns this state.</summary>
        public OvertureManager Manager => manager;

        protected virtual void Awake()
        {
            manager = GetComponentInParent<OvertureManager>();
            if (manager == null)
                Debug.LogError($"OvertureState '{gameObject.name}' must be a child of an OvertureManager.", this);

            childVisuals = CollectOwnVisuals();
            RebuildManagedCache();
        }

        // ─── Managed objects cache ───────────────────────────────────────────

        private void RebuildManagedCache()
        {
            managedGoCache = new HashSet<GameObject>();
            foreach (ManagedStateObject entry in managedObjects)
            {
                if (entry.gameObject != null)
                    managedGoCache.Add(entry.gameObject);
            }
        }

        // ─── Visual collection (stops at substate boundaries) ────────────────

        private OvertureVisual[] CollectOwnVisuals()
        {
            var result = new List<OvertureVisual>();
            result.AddRange(GetComponents<OvertureVisual>());
            CollectVisualsRecursive(transform, result);
            return result.ToArray();
        }

        private static void CollectVisualsRecursive(Transform parent, List<OvertureVisual> result)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.GetComponent<OvertureState>() != null) continue;
                result.AddRange(child.GetComponents<OvertureVisual>());
                CollectVisualsRecursive(child, result);
            }
        }

        // ─── Children activation ─────────────────────────────────────────────

        /// <summary>
        /// Activates the subtree under this state, recursively skipping substates
        /// and managed objects at any depth. Managed objects are controlled solely
        /// by ApplyManagedObjects at each phase.
        /// </summary>
        internal void EnableChildren()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<OvertureState>() != null) continue;
                ActivateSubtree(child);
            }
        }

        /// <summary>
        /// Deactivates all direct children except substates.
        /// Managed objects are included because the state is being fully deactivated —
        /// ResetManagedObjects restores their activeSelf afterward for clean re-entry.
        /// </summary>
        internal void DisableChildren()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<OvertureState>() != null) continue;
                child.gameObject.SetActive(false);
            }
        }

        private void ActivateSubtree(Transform node)
        {
            if (managedGoCache.Contains(node.gameObject)) return;

            node.gameObject.SetActive(true);

            for (int i = 0; i < node.childCount; i++)
                ActivateSubtree(node.GetChild(i));
        }

        // ─── Managed object phase control ────────────────────────────────────

        /// <summary>Applies managed object rules for the given phase.</summary>
        internal void ApplyManagedObjects(StatePhase phase)
        {
            foreach (ManagedStateObject entry in managedObjects)
            {
                if (entry.gameObject == null) continue;
                if ((entry.controlledPhases & phase) == 0) continue;
                entry.gameObject.SetActive((entry.activePhases & phase) != 0);
            }
        }

        /// <summary>
        /// Resets all managed objects to active so the next enter cycle starts from
        /// a clean slate. Called after the state's GameObject has been deactivated.
        /// </summary>
        internal void ResetManagedObjects()
        {
            foreach (ManagedStateObject entry in managedObjects)
            {
                if (entry.gameObject != null)
                    entry.gameObject.SetActive(true);
            }
        }

        // ─── Enter / Exit animations ─────────────────────────────────────────

        /// <summary>
        /// Runs enter animations (OvertureVisual.Enter) and the blocking OnEntering()
        /// hook in parallel. Waits for all blocking tasks before returning.
        /// </summary>
        internal async Task ExecuteEnterAnimations()
        {
            var blocking = new List<Task>();
            blocking.Add(OnEntering());

            _ = OnEnteringBackground();

            foreach (OvertureVisual visual in childVisuals)
            {
                Task task = visual.Enter();
                if (!visual.DontBlockOnEnter)
                    blocking.Add(task);
            }

            if (blocking.Count > 0)
                await Task.WhenAll(blocking);
        }

        /// <summary>Runs all exit animations (OvertureVisual.Exit) in parallel.</summary>
        internal async Task ExecuteExitAnimations()
        {
            var tasks = new List<Task>();
            foreach (OvertureVisual visual in childVisuals)
                tasks.Add(visual.Exit());

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }

        /// <summary>Runs ToSubstate visuals before a substate enters.</summary>
        internal async Task ExecuteToSubstateVisuals()
        {
            var tasks = new List<Task>();
            foreach (OvertureVisual visual in childVisuals)
                tasks.Add(visual.ExitToSubstate());

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }

        /// <summary>Runs FromSubstate visuals after a substate exits.</summary>
        internal async Task ExecuteFromSubstateVisuals()
        {
            var tasks = new List<Task>();
            foreach (OvertureVisual visual in childVisuals)
                tasks.Add(visual.EnterFromSubstate());

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }

        // ─── Virtual lifecycle hooks — override in subclasses ────────────────

        /// <summary>
        /// Runs in parallel with enter animations and contributes to the "entered" gate.
        /// Override to add blocking work that must complete before the state is entered.
        /// </summary>
        protected virtual Task OnEntering() => Task.CompletedTask;

        /// <summary>
        /// Runs in parallel with enter animations but does NOT block the "entered" gate.
        /// Override for fire-and-forget background work during enter.
        /// </summary>
        protected virtual Task OnEnteringBackground() => Task.CompletedTask;

        /// <summary>Called on the superState before its substate begins entering.</summary>
        public virtual Task OnEnteringSubstate(OvertureState substate) => Task.CompletedTask;

        /// <summary>Called on the superState after its substate has fully exited.</summary>
        public virtual Task OnReturningFromSubstate(OvertureState substate) => Task.CompletedTask;

        /// <summary>Called after the entering phase completes.</summary>
        public virtual Task OnEnter() => Task.CompletedTask;

        /// <summary>Called after OnEnter. Override to run continuous state logic.</summary>
        public virtual Task OnState() => Task.CompletedTask;

        /// <summary>Called during the exit sequence, after exit animations.</summary>
        public virtual Task OnExit() => Task.CompletedTask;

        // ─── Navigation ──────────────────────────────────────────────────────

        /// <summary>Requests the manager to transition to this state by name.</summary>
        public void TransitionHere()
        {
            if (manager != null)
                manager.TransitionTo(gameObject.name);
            else
                Debug.LogError($"Cannot transition to '{gameObject.name}': no OvertureManager found.", this);
        }
    }
}


