using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Laubrary.Overture
{
    public class OvertureState : MonoBehaviour
    {
        [Header("Managed GameObjects")]
        [Tooltip("GameObjects whose active state is explicitly controlled at each transition phase. " +
                 "Objects not listed follow the default: active while the state is active, inactive when fully exited.")]
        [SerializeField] private List<ManagedStateObject> managedObjects = new List<ManagedStateObject>();

        protected OvertureManager manager;
        private IOvertureTransition[] transitions;
        private OvertureVisual[] childVisuals;

        public OvertureManager Manager => manager;

        protected virtual void Awake()
        {
            manager = GetComponentInParent<OvertureManager>();
            if (manager == null)
                manager = GetComponent<OvertureManager>();

            if (manager == null)
                Debug.LogError($"OvertureState '{gameObject.name}' must be a child of an OvertureManager or on the same GameObject.", this);

            transitions  = GetComponents<IOvertureTransition>();
            childVisuals = GetComponentsInChildren<OvertureVisual>(true);
        }

        // ─── Children ────────────────────────────────────────────────────────

        /// <summary>Activates all direct children of this state's GameObject.</summary>
        public void EnableChildren()
        {
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(true);
        }

        /// <summary>Deactivates all direct children of this state's GameObject.</summary>
        public void DisableChildren()
        {
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(false);
        }

        // ─── Phase hooks ─────────────────────────────────────────────────────

        /// <summary>
        /// Runs the full entering phase: applies managed objects, fires all parallel processes,
        /// and waits for all blocking ones before returning.
        /// Blocking processes: IOvertureTransition components, OvertureVisuals with DontBlockOnEnter=false,
        /// and the virtual OnEntering() override.
        /// Non-blocking: OnEnteringBackground() override and OvertureVisuals with DontBlockOnEnter=true.
        /// </summary>
        public async Task ExecuteEnteringPhase()
        {
            ApplyManagedObjects(StatePhase.Entering);

            var blockingTasks = new List<Task>();

            // User blocking code — override OnEntering() to participate in the IsEntered gate.
            blockingTasks.Add(OnEntering());

            // User non-blocking code — fires and continues independently.
            _ = OnEnteringBackground();

            // IOvertureTransition components always block.
            blockingTasks.AddRange(transitions
                .Where(t => t.enabled && (t.transitionType == TransitionType.Enter || t.transitionType == TransitionType.Both))
                .Select(t => t.Execute(true)));

            // OvertureVisuals split by their flag.
            foreach (OvertureVisual visual in childVisuals)
            {
                Task enterTask = visual.Enter();
                if (!visual.DontBlockOnEnter)
                    blockingTasks.Add(enterTask);
                // else: already running in the background
            }

            if (blockingTasks.Count > 0)
                await Task.WhenAll(blockingTasks);
        }

        /// <summary>Called by the manager at the start of the exit sequence.</summary>
        public void TriggerExiting() => ApplyManagedObjects(StatePhase.Exiting);

        private void ApplyManagedObjects(StatePhase phase)
        {
            foreach (ManagedStateObject entry in managedObjects)
            {
                if (entry.gameObject == null) continue;
                if ((entry.controlledPhases & phase) == 0) continue;
                entry.gameObject.SetActive((entry.activePhases & phase) != 0);
            }
        }

        // ─── Virtual state lifecycle — override in subclasses ────────────────

        /// <summary>
        /// Async code that runs in parallel with enter animations and contributes to the IsEntered gate.
        /// Override to add work that must complete before the state is considered fully entered.
        /// </summary>
        protected virtual Task OnEntering() => Task.CompletedTask;

        /// <summary>
        /// Async code that runs in parallel with enter animations but does NOT contribute to the IsEntered gate.
        /// Override to fire background work that continues independently after IsEntered.
        /// </summary>
        protected virtual Task OnEnteringBackground() => Task.CompletedTask;

        // ─── State lifecycle ─────────────────────────────────────────────────

        /// <summary>Called by the manager when this state is entered.</summary>
        public virtual Task OnEnter() => Task.CompletedTask;

        /// <summary>Called by the manager after enter transitions complete. Override to run state logic.</summary>
        public virtual Task OnState() => Task.CompletedTask;

        /// <summary>Called by the manager when this state is exited.</summary>
        public virtual Task OnExit() => Task.CompletedTask;

        // ─── Transitions ─────────────────────────────────────────────────────

        /// <summary>Runs all exit transitions and OvertureVisual exit animations in parallel.</summary>
        public async Task ExecuteExitTransitions()
        {
            var tasks = new List<Task>();

            tasks.AddRange(transitions
                .Where(t => t.enabled && (t.transitionType == TransitionType.Exit || t.transitionType == TransitionType.Both))
                .Select(t => t.Execute(false)));

            tasks.AddRange(childVisuals.Select(v => v.Exit()));

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }

        // ─── Navigation ──────────────────────────────────────────────────────

        /// <summary>Requests the manager to transition to this state by name.</summary>
        public void TransitionHere()
        {
            if (manager != null)
                manager.TransitionTo(gameObject.name);
            else
                Debug.LogError($"Cannot transition to '{gameObject.name}': No OvertureManager found.", this);
        }
    }
}


