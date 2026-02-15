using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Monolith
{
    /// <summary>
    /// Manages game states and transitions between them for a Unity game. Monolith is designed to work with any class used as a game manager 
    /// and a game specific enum as state identifier. Monolith states can have substates. A substate can only be entered from its parent state or other sibling 
    /// substates that shares parent state. When a substate is entered the exit method of the parent is not called. When returning to the parent state 
    /// the enter method of the parent is not called. When transitioning between siblings both enter and exit of the current and next state is called. 
    /// </summary>
    /// <typeparam name="GameManager">The type of the class used as game manager. Its expected to contain context and functions that the states can use to controll the game.</typeparam>
    /// <typeparam name="GameStateEnum">The game specific custom enum type used to identify different game states.</typeparam>

    public class MonolithStateMachine<GameManager, GameStateEnum> where GameStateEnum : Enum
    {
        /// <summary>Reference to the central game manager controlling game-wide logic. </summary>
        public GameManager gameManager;

        /// <summary>If true state machine will log every transitional step</summary>
        public bool logTransitions = false;

        /// <summary> The current active game state within the state machine. </summary>
        MonolithStateBase<GameManager, GameStateEnum> currentMonolithState;

        /// <summary> The identifier of the current game state.</summary>
        private GameStateEnum currentStateEnum;

        /// <summary> This state will run on the first update if no state has been set before. This defaults to the first value in the enum if not set. </summary>
        public GameStateEnum startingState;

        List<MonolithStateBase<GameManager, GameStateEnum>> states = new();

        /// <summary>Callback when a new state is entered. </summary>
        public event Action<GameStateEnum> OnStateChanged;

        /// <summary>Public property to get or set the current game state. Setting a new state triggers a state transition.</summary>
        public GameStateEnum CurrentStateEnum
        {
            get => currentStateEnum;
            set
            {
                ChangeState(value);
            }
        }

        /// <summary>Constructs a new state machine with a reference to the game manager.</summary>
        /// <param name="gameManager">The game manager instance to control.</param>
        public MonolithStateMachine(GameManager gameManager)
        {
            this.gameManager = gameManager;
        }

        /// <summary>Adds a new state to the state machine and associates it with a state enum value. An enum value can only be associated with one instance of MonolithStateBase.
        /// Optionally, sets this state as the starting state. Returns a reference to the added state</summary>
        /// <param name="stateEnum">The identifier of the new state.</param>
        /// <param name="monolithState">The state object to add.</param>
        /// <param name="isStartingState">If true, this state will be set as the starting state.</param>
        /// <returns>The added state instance.</returns>
        public MonolithStateBase<GameManager, GameStateEnum> AddState(GameStateEnum stateEnum, MonolithStateBase<GameManager, GameStateEnum> monolithState, bool isStartingState = false)
        {
            if (IsEnumRegistered(stateEnum))
                Debug.LogError("Monolith State Machine already contains a state for : " + stateEnum.ToString());

            monolithState.gameManager = gameManager;
            monolithState.stateMachine = this;
            monolithState.SetAssignedGameState(stateEnum);

            states.Add(monolithState);

            if (isStartingState)
                if (isStartingState)
                    startingState = stateEnum;

            return monolithState;
        }

        /// <summary>Updates the current game state running the state machine. Call once from a monobehaviours Update method.</summary>
        public void UpdateState()
        {
            if (currentMonolithState == null)
            {
                ChangeState(startingState);
            }

            var nextState = currentMonolithState.UpdateState();
            ChangeState(nextState);
        }

        /// <summary>Initiates a transition to a new state, if it differs from the current state. Handles transitions between various state relationships, 
        /// including substates, siblings, and parent states, ensuring appropriate exit and entry actions are executed. </summary>
        /// <param name="nextStateEnum">The identifier of the new state to transition to. Transition is skipped if it matches the current state.</param>
        public void ChangeState(GameStateEnum nextStateEnum)
        {
            if (currentMonolithState != null && nextStateEnum.Equals(CurrentStateEnum))
                return;

            var nextState = GetStateByEnum(nextStateEnum);

            if (nextState == null)
                Debug.LogError("[MONOLITH] State Machine is being asked to transition using a stateEnum that has no associated monolithState! Requested state:  " + nextStateEnum);


            if (currentMonolithState == null)// SET STARTING STATE
            {
                EnterStartingState(nextState);
            }
            else if (!IsRelated(currentMonolithState, nextState))
            {
                NavigateToNonRelative(nextState);
            }
            else
            {
                NavigateToRelative(nextState);
            }

            OnStateChanged?.Invoke(nextStateEnum);
        }


        /// <summary>
        /// Target state and source state are children of different parents. 
        /// </summary>
        private void NavigateToNonRelative(MonolithStateBase<GameManager, GameStateEnum> targetState)
        {
            DebugLog($"Making multi state tranistion! Source: {currentMonolithState.enumValue} Target : {targetState.enumValue}");

            var currentParent = currentMonolithState.ParentState;
            var targetParent = targetState.ParentState;

            if (currentParent != null)
                NavigateToRelative(currentParent);

            if (targetParent != null)
                NavigateToRelative(targetParent);

            if (currentMonolithState != targetState)
                NavigateToRelative(targetState);
        }

        private void EnterStartingState(MonolithStateBase<GameManager, GameStateEnum> nextState)
        {
            currentMonolithState = nextState;
            currentMonolithState.EnterState(nextState.enumValue);
            currentStateEnum = nextState.enumValue;
        }

        private void NavigateToRelative(MonolithStateBase<GameManager, GameStateEnum> nextState)
        {
            if (IsChild(nextState, currentMonolithState))
            {
                DebugLog($"ENTERING:{nextState.enumValue} - From:{currentMonolithState.enumValue}");
                currentMonolithState = nextState;
                currentStateEnum = nextState.enumValue;
                
                currentMonolithState.EnterState(nextState.enumValue);

            }
            else if (IsSibling(currentMonolithState, nextState)) // Sibling includes transition between root level states
            {
                DebugLog($"EXITING:{currentMonolithState.enumValue} - To:{nextState.enumValue}");
                currentMonolithState.ExitState(nextState.enumValue); // EXIT 

                DebugLog($"ENTERING:{nextState.enumValue} - From:{currentMonolithState.enumValue}");
                currentMonolithState = nextState; // SWAP TO NEW STATE
                var previousStateEnum = currentStateEnum;
                currentStateEnum = nextState.enumValue;

                currentMonolithState.EnterState(previousStateEnum); // ENTER NEW STATE

            }
            else if (currentMonolithState.ParentState == nextState)
            {
                DebugLog($"EXITING:{currentMonolithState.enumValue} - To:{nextState.enumValue}");
                currentMonolithState.ExitState(nextState.enumValue);
                currentMonolithState = nextState;
                currentStateEnum = nextState.enumValue;
            }
            else
            {
                Debug.LogError($"[MONOLITH] State Machine can only transition to a state that is registered as a sibling, substate or parent state of the current one. Requested state:  {nextState.enumValue} Current state: {currentMonolithState.enumValue}");
            }
        }
        #region HELPERS
        List<MonolithStateBase<GameManager, GameStateEnum>> GetSiblings(MonolithStateBase<GameManager, GameStateEnum> state)
        {

            var commonParent = state.ParentState;
            //            if (commonParent == null) return new List<MonolithStateBase<GameManager, GameStateEnum>>();

            var siblings = states.Where(state => state.parentState == commonParent).ToList();
            return siblings;
        }
        List<MonolithStateBase<GameManager, GameStateEnum>> GetChildren(MonolithStateBase<GameManager, GameStateEnum> state)
        {
            var children = states.Where(child => child.parentState == state).ToList();
            return children;
        }
        bool IsAncestor(MonolithStateBase<GameManager, GameStateEnum> potentialAncestor, MonolithStateBase<GameManager, GameStateEnum> state)
        {
            while (state != null)
            {
                if (state == potentialAncestor)
                    return true;
                state = state.ParentState;
            }
            return false;
        }
        bool IsSibling(MonolithStateBase<GameManager, GameStateEnum> state, MonolithStateBase<GameManager, GameStateEnum> potentialSibling)
        {
            var siblings = GetSiblings(state);
            return siblings.Contains(potentialSibling);
        }
        List<MonolithStateBase<GameManager, GameStateEnum>> GetAncestorPath(MonolithStateBase<GameManager, GameStateEnum> state)
        {
            List<MonolithStateBase<GameManager, GameStateEnum>> path = new List<MonolithStateBase<GameManager, GameStateEnum>>();
            while (state != null)
            {
                path.Insert(0, state); // Insert at the beginning to build the path from root to the state
                state = state.ParentState;
            }
            return path;
        }
        bool IsChild(MonolithStateBase<GameManager, GameStateEnum> potentialChild, MonolithStateBase<GameManager, GameStateEnum> potentialParent)
        {
            if (potentialChild == null || potentialParent == null)
                return false;
            return potentialChild.ParentState == potentialParent;
        }

        /// <summary>Determines if two states are related by being siblings or if one is a direct child of the other.</summary>
        bool IsRelated(MonolithStateBase<GameManager, GameStateEnum> state1, MonolithStateBase<GameManager, GameStateEnum> state2)
        {
            if (IsSibling(state1, state2)
             || IsChild(state1, state2)
             || IsChild(state2, state1))
            {
                return true;
            }

            return false;
        }

        void DebugLog(string msg)
        {
            if (logTransitions)
                Debug.Log($"[MONOLITH] {msg}");
        }
        internal bool IsEnumRegistered(GameStateEnum childStateEnum)
        {
            return GetStateByEnum(childStateEnum) != null;
        }
        internal MonolithStateBase<GameManager, GameStateEnum> GetStateByEnum(GameStateEnum childStateEnum)
        {
            return states.FirstOrDefault(state => Equals(state.enumValue, childStateEnum));
        }
        #endregion
    }
}
