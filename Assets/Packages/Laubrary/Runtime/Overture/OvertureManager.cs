using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Laubrary.Overture
{
    /// <summary>
    /// Orchestrates a hierarchical state machine defined by child OvertureState GameObjects.
    /// Handles state transitions, including substate enter/exit, with full animation support.
    /// </summary>
    public class OvertureManager : MonoBehaviour
    {
        [Header("Initial State")]
        [SerializeField] private OvertureState initialState;

        [Header("Boot Up State (Optional)")]
        [Tooltip("Runs once before entering the initial state.")]
        [SerializeField] private bool useBootUpState = false;
        [SerializeField] private OvertureState bootUpState;

        private OvertureState currentState;
        private bool isTransitioning;
        private Dictionary<string, OvertureState> stateCache;

        // ─── Initialization ──────────────────────────────────────────────────

        private async void Awake()
        {
            CacheStates();

            if (stateCache.Count == 0)
            {
                Debug.LogWarning($"OvertureManager '{gameObject.name}' has no child OvertureStates.", this);
                return;
            }

            ValidateHierarchy();
            DisableAllStates();

            if (useBootUpState && bootUpState != null)
                await RunBootUpState();

            OvertureState target = initialState
                ?? stateCache.Values.FirstOrDefault(s => s.transform.parent == transform);

            if (target != null)
                TransitionTo(target.gameObject.name);
        }

        private void CacheStates()
        {
            stateCache = new Dictionary<string, OvertureState>();

            foreach (OvertureState state in GetComponentsInChildren<OvertureState>(true))
            {
                if (state.transform == transform) continue;

                string id = state.gameObject.name;
                if (stateCache.ContainsKey(id))
                {
                    Debug.LogWarning(
                        $"OvertureManager '{gameObject.name}': duplicate state name '{id}'. " +
                        $"Only the first occurrence will be reachable.", state);
                    continue;
                }

                stateCache[id] = state;
            }
        }

        private void ValidateHierarchy()
        {
            foreach (OvertureState state in stateCache.Values)
            {
                Transform parent = state.transform.parent;
                if (parent == null || parent == transform) continue;

                if (parent.GetComponent<OvertureState>() == null)
                {
                    Debug.LogWarning(
                        $"OvertureState '{state.gameObject.name}' has a non-state parent '{parent.name}'. " +
                        $"States must be direct children of the OvertureManager or another OvertureState " +
                        $"for path resolution to work correctly.", state);
                }
            }
        }

        private void DisableAllStates()
        {
            foreach (OvertureState state in stateCache.Values)
            {
                state.gameObject.SetActive(false);
                for (int i = 0; i < state.transform.childCount; i++)
                    state.transform.GetChild(i).gameObject.SetActive(false);
            }
        }

        // ─── Boot Up ─────────────────────────────────────────────────────────

        private async Task RunBootUpState()
        {
            bootUpState.gameObject.SetActive(true);
            await ActivateState(bootUpState);
            await bootUpState.OnState();
            await DeactivateState(bootUpState);
            bootUpState.gameObject.SetActive(false);
        }

        // ─── Public API ──────────────────────────────────────────────────────

        /// <summary>Transitions to the state with the given name.</summary>
        public async void TransitionTo(string stateId)
        {
            if (isTransitioning)
            {
                Debug.LogWarning(
                    $"OvertureManager '{gameObject.name}': transition already in progress, ignoring '{stateId}'.", this);
                return;
            }

            if (!stateCache.TryGetValue(stateId, out OvertureState target))
            {
                Debug.LogError(
                    $"OvertureManager '{gameObject.name}': state '{stateId}' not found.", this);
                return;
            }

            if (currentState == target)
            {
                Debug.LogWarning(
                    $"OvertureManager '{gameObject.name}': already in state '{stateId}'.", this);
                return;
            }

            isTransitioning = true;

            try
            {
                await PerformTransition(currentState, target);
                currentState = target;
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"OvertureManager '{gameObject.name}': transition failed — {ex.Message}\n{ex.StackTrace}", this);
            }
            finally
            {
                isTransitioning = false;
            }

            // OnState runs after the lock is released so that TransitionTo calls
            // from within OnState are not blocked.
            await target.OnState();
        }

        // ─── Transition orchestration ────────────────────────────────────────

        private async Task PerformTransition(OvertureState from, OvertureState to)
        {
            if (from == null)
            {
                // Initial entry — activate the full path top-down.
                List<OvertureState> path = BuildPathToRoot(to);
                path.Reverse();

                foreach (OvertureState state in path)
                {
                    state.gameObject.SetActive(true);
                    await ActivateState(state);
                }

                return;
            }

            OvertureState ancestor = FindCommonAncestor(from, to);
            List<OvertureState> exitPath  = BuildPathToAncestor(from, ancestor);
            List<OvertureState> enterPath = BuildPathToAncestor(to,   ancestor);

            bool enteringSubstate      = ancestor == from;
            bool returningFromSubstate = ancestor == to;

            // ── SuperState notification: descending into a substate ───────────
            if (enteringSubstate)
            {
                await ancestor.OnEnteringSubstate(to);
                await ancestor.ExecuteToSubstateVisuals();
            }

            // ── Exit (bottom-up) ─────────────────────────────────────────────
            foreach (OvertureState state in exitPath)
            {
                await DeactivateState(state);
                state.gameObject.SetActive(false);
            }

            // ── Enter (top-down) ─────────────────────────────────────────────
            enterPath.Reverse();
            foreach (OvertureState state in enterPath)
            {
                state.gameObject.SetActive(true);
                await ActivateState(state);
            }

            // ── SuperState notification: returning from a substate ───────────
            if (returningFromSubstate)
            {
                await ancestor.ExecuteFromSubstateVisuals();
                await ancestor.OnReturningFromSubstate(from);
            }
        }

        // ─── Symmetric lifecycle ─────────────────────────────────────────────

        private async Task ActivateState(OvertureState state)
        {
            state.ApplyManagedObjects(StatePhase.Entering);
            state.EnableChildren();
            await state.ExecuteEnterAnimations();
            await state.OnEnter();
            state.ApplyManagedObjects(StatePhase.Entered);
        }

        private async Task DeactivateState(OvertureState state)
        {
            state.ApplyManagedObjects(StatePhase.Exiting);
            await state.ExecuteExitAnimations();
            await state.OnExit();
            state.DisableChildren();
            state.ApplyManagedObjects(StatePhase.Exited);
            state.ResetManagedObjects();
        }

        // ─── Path resolution ─────────────────────────────────────────────────

        /// <summary>Builds path from state up to (not including) ancestor.</summary>
        private List<OvertureState> BuildPathToAncestor(OvertureState from, OvertureState ancestor)
        {
            var path = new List<OvertureState>();
            OvertureState current = from;

            while (current != ancestor && current != null)
            {
                path.Add(current);
                Transform parent = current.transform.parent;
                if (parent == null || parent == transform) break;
                current = parent.GetComponent<OvertureState>();
            }

            return path;
        }

        /// <summary>Builds path from state up to the manager root.</summary>
        private List<OvertureState> BuildPathToRoot(OvertureState state)
        {
            var path = new List<OvertureState>();
            OvertureState current = state;

            while (current != null && current.transform != transform)
            {
                path.Add(current);
                Transform parent = current.transform.parent;
                current = parent != null ? parent.GetComponent<OvertureState>() : null;
            }

            return path;
        }

        private OvertureState FindCommonAncestor(OvertureState a, OvertureState b)
        {
            var ancestorsA = new HashSet<OvertureState>(BuildPathToRoot(a));

            foreach (OvertureState state in BuildPathToRoot(b))
            {
                if (ancestorsA.Contains(state))
                    return state;
            }

            return null;
        }
    }
}