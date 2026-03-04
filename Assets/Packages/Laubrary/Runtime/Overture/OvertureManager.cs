using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Laubrary.Overture
{
    public class OvertureManager : MonoBehaviour
    {
        [Header("Initial State")]
        [SerializeField] private OvertureState initialState;

        [Header("Boot Up State (Optional)")]
        [Tooltip("Runs once before entering the initial state. Assign any OvertureState.")]
        [SerializeField] private bool useBootUpState = false;
        [SerializeField] private OvertureState bootUpState;

        private OvertureState currentState;
        private bool isTransitioning;

        private async void Awake()
        {
            OvertureState[] normalStates = GetNormalStates();

            if (normalStates.Length == 0)
            {
                Debug.LogWarning($"OvertureManager '{gameObject.name}' has no child OvertureStates.", this);
                return;
            }

            DisableAllNormalStates(normalStates);

            if (useBootUpState)
            {
                await InitializeAndRunBootUpState();
            }

            OvertureState targetState = initialState;
            if (targetState == null)
            {
                targetState = normalStates.FirstOrDefault(state => state.transform.parent == transform);
            }

            if (targetState != null)
            {
                TransitionTo(targetState.gameObject.name);
            }
        }

        #region Boot-Up State (Isolated Logic)

        private async Task InitializeAndRunBootUpState()
        {
            if (bootUpState == null)
            {
                Debug.LogWarning($"OvertureManager '{gameObject.name}' has 'Use Boot Up State' enabled but no Boot Up State is assigned.", this);
                return;
            }

            bootUpState.gameObject.SetActive(true);
            bootUpState.EnableChildren();
            await EnterState(bootUpState);
            await bootUpState.OnState();
            await ExitState(bootUpState);
            bootUpState.gameObject.SetActive(false);
        }

        #endregion

        #region Normal State Management

        private OvertureState[] GetNormalStates()
        {
            OvertureState[] allStates = GetComponentsInChildren<OvertureState>(true);
            return allStates.Where(state => state.transform.parent != null).ToArray();
        }

        private void DisableAllNormalStates(OvertureState[] normalStates)
        {
            foreach (OvertureState state in normalStates)
            {
                state.gameObject.SetActive(false);
                for (int i = 0; i < state.transform.childCount; i++)
                {
                    state.transform.GetChild(i).gameObject.SetActive(false);
                }
            }
        }

        public async void TransitionTo(string stateId)
        {
            if (isTransitioning)
            {
                Debug.LogWarning("Already transitioning, ignoring request.");
                return;
            }

            OvertureState targetState = FindStateById(stateId);
            if (targetState == null)
            {
                Debug.LogError($"State with ID '{stateId}' not found in OvertureManager '{gameObject.name}'.", this);
                return;
            }

            Debug.Log($"Transitioning from {(currentState != null ? currentState.gameObject.name : "null")} to {targetState.gameObject.name}");

            if (currentState == targetState)
            {
                Debug.LogWarning("Already in target state.");
                return;
            }

            isTransitioning = true;

            try
            {
                await PerformTransition(currentState, targetState);
                currentState = targetState;
                Debug.Log($"Transition complete. Current state is now: {currentState.gameObject.name}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Exception during transition: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                isTransitioning = false;
            }

            // OnState runs after the lock is released so that TransitionTo calls from within OnState are not blocked.
            await targetState.OnState();
        }

        private async Task PerformTransition(OvertureState fromState, OvertureState toState)
        {
            if (fromState == null)
            {
                Debug.Log($"Entering initial state: {toState.gameObject.name}");
                List<OvertureState> enterPath = GetPathToRoot(toState);
                enterPath.Reverse();

                foreach (OvertureState state in enterPath)
                {
                    Debug.Log($"Enabling and entering state: {state.gameObject.name}");
                    state.gameObject.SetActive(true);
                    state.EnableChildren();
                    await EnterState(state);
                }
            }
            else
            {
                List<OvertureState> exitPath = new List<OvertureState>();
                List<OvertureState> enterPath = new List<OvertureState>();

                OvertureState lca = FindLastCommonAncestor(fromState, toState);

                OvertureState current = fromState;
                while (current != lca && current != null)
                {
                    exitPath.Add(current);
                    Transform parentTransform = current.transform.parent;
                    if (parentTransform == null || parentTransform == transform)
                        break;
                    current = parentTransform.GetComponent<OvertureState>();
                }

                current = toState;
                while (current != lca && current != null)
                {
                    enterPath.Add(current);
                    Transform parentTransform = current.transform.parent;
                    if (parentTransform == null || parentTransform == transform)
                        break;
                    current = parentTransform.GetComponent<OvertureState>();
                }

                foreach (OvertureState state in exitPath)
                {
                    await ExitState(state);
                    state.gameObject.SetActive(false);
                }

                enterPath.Reverse();
                foreach (OvertureState state in enterPath)
                {
                    state.gameObject.SetActive(true);
                    state.EnableChildren();
                    await EnterState(state);
                }
            }
        }

        private async Task EnterState(OvertureState state)
        {
            await state.ExecuteEnteringPhase();
            await state.OnEnter();
        }

        private async Task ExitState(OvertureState state)
        {
            state.TriggerExiting();
            await state.ExecuteExitTransitions();
            await state.OnExit();
            state.DisableChildren();
        }

        private OvertureState FindLastCommonAncestor(OvertureState state1, OvertureState state2)
        {
            List<OvertureState> path1 = GetPathToRoot(state1);
            List<OvertureState> path2 = GetPathToRoot(state2);

            HashSet<OvertureState> ancestors1 = new HashSet<OvertureState>(path1);

            foreach (OvertureState state in path2)
            {
                if (ancestors1.Contains(state))
                {
                    return state;
                }
            }

            return null;
        }

        private List<OvertureState> GetPathToRoot(OvertureState state)
        {
            List<OvertureState> path = new List<OvertureState>();
            OvertureState current = state;

            while (current != null && current.transform != transform)
            {
                path.Add(current);
                Transform parent = current.transform.parent;
                current = parent != null ? parent.GetComponent<OvertureState>() : null;
            }

            return path;
        }

        private OvertureState FindStateById(string stateId)
        {
            OvertureState[] normalStates = GetNormalStates();
            return normalStates.FirstOrDefault(state => state.gameObject.name == stateId);
        }

        #endregion
    }
}