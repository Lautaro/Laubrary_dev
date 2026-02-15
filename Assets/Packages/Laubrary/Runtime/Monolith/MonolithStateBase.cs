using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Monolith
{
    /// <summary>
    /// Base class for all game states, providing default implementations for state lifecycle methods.
    /// </summary>
    /// /// <typeparam name="GameManager">The type of the class used as game manager. Its expected to contain context and functions that the states can use to controll the game.</typeparam>
    /// <typeparam name="GameStateEnum">The game specific custom enum type used to identify different game states.</typeparam>

    public abstract class MonolithStateBase<GameManager, GameStateEnum> where GameStateEnum : Enum
    {
        /// <summary>Reference to the game manager for accessing global game functionality.</summary>
        public GameManager gameManager;

        /// <summary>The enum identifier assigned to this state.</summary>
        protected GameStateEnum assignedGameState;
        public GameStateEnum enumValue => assignedGameState;
        internal GameStateEnum SetAssignedGameState (GameStateEnum gameStateEnum) => assignedGameState = gameStateEnum;
        internal MonolithStateMachine<GameManager, GameStateEnum> stateMachine;
        /// <summary>If state is a substate this will reference the parent state</summary>
        internal MonolithStateBase<GameManager, GameStateEnum> parentState;
        public MonolithStateBase<GameManager, GameStateEnum> ParentState => parentState;

        /// <summary>Called when entering this state, allowing for setup or initialization specific to the state.</summary>
        /// <param name="previousStateEnum">The identifier of the state being transitioned from.</param>
        public virtual void EnterState(GameStateEnum previousStateEnum) { }

        /// <summary>Updates the state logic and determines the next state.</summary>
        /// <returns>The identifier of the next state. Return assignedGameState to stay in the same state or another state to transition.</returns>
        public virtual GameStateEnum UpdateState()
        {
            return assignedGameState;
        }

        /// <summary>Called when exiting this state, allowing for cleanup or other finalization specific to the state.
        /// </summary>
        /// <param name="nextStateEnum">The identifier of the state being transitioned to.</param>
        public virtual void ExitState(GameStateEnum nextStateEnum) { }

        /// <summary>Adds a child state to this state. States can only have one parent. A state which is a child can not be a parent (Only one level is allowed)</summary>
        /// <param name="childStateEnum">The enum value representing the child state.</param>
        /// <param name="childState">The child state instance to be added.</param>
        public void AddChildState(GameStateEnum childStateEnum, MonolithStateBase<GameManager, GameStateEnum> childState)
        {
            if (parentState != null)
                Debug.LogError($"[MONOLITH] You are adding a child to a parent that is a child itself. " +
                    $"Only one level of states are allowed. Parent: {parentState.GetType()}");

            if (childState.parentState != null)
                Debug.LogError($"[MONOLITH] You are adding a child that already has a registered parent. " +
                    $"This is not allowed. Already registered parent: {childState.parentState.GetType()} - Failed parent: {GetType()}");

            var alreadyRegistered = stateMachine.GetStateByEnum(childStateEnum);
            if (alreadyRegistered != null)
            {
                Debug.LogError($"[MONOLITH] You are adding a child with an enum value that is already registered to state {alreadyRegistered.GetType()}- Enum value : {childStateEnum} - Attempted state: {GetType()}");
            }

            childState.assignedGameState = assignedGameState;
            childState.parentState = this;
            stateMachine.AddState(childStateEnum, childState);
        }
    }
}