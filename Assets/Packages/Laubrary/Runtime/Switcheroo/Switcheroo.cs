using System.Collections.Generic;
using System;
using UnityEngine;
using Laubrary.LaubraryTicker;

namespace Laubrary.Switcheroo
{
    /// <summary>
    /// Switcheroo is a stateful transition manager, enabling smooth transitions between "On" and "Off" states.
    /// It supports custom transition actions, event hooks for state changes, and time-based progress using Unity's update loop.
    /// Uses the LaubraryTicker system.
    /// </summary>
    public class Switcheroo
    {
        public enum SwitcherooState { On, Off, TransitionToOn, TransitionToOff }
        public SwitcherooState CurrentState { get; private set; } = SwitcherooState.Off;
        public Dictionary<string, Action<float>> transitions = new();
        private float progress = 0f;
        public float transitionSpeed=1f;
        public event Action WhenOn;
        public event Action WhenOff;

        public Switcheroo()
        {
            Ticker.OnUpdate += Update;
        }

        public void AddTransition(string id, Action<float> transition)
        {
            transitions.Add(id, transition);
        }

        public void Update()
        {
            var deltaTime = Time.deltaTime;
            switch (CurrentState)
            {
                case SwitcherooState.TransitionToOn:
                    progress += deltaTime / transitionSpeed;
                    if (progress >= 1f)
                    {
                        progress = 1f;
                        CurrentState = SwitcherooState.On;
                        WhenOn?.Invoke();
                    }
                    break;
                case SwitcherooState.TransitionToOff:
                    progress -= deltaTime / transitionSpeed;
                    if (progress <= 0f)
                    {
                        progress = 0f;
                        CurrentState = SwitcherooState.Off;
                        WhenOff?.Invoke();
                    }
                    break;
            }

            ResolveTransitions();
        }

        private void ResolveTransitions()
        {
            foreach (var trans in transitions)
            {
                trans.Value.Invoke(progress);
            }
        }

        public void Flip() {
            if (CurrentState == SwitcherooState.On || CurrentState == SwitcherooState.TransitionToOn)
                SwitchToOff();
            else
                SwitchToOn();
        }

        public void SwitchToOn()
        {
            if (CurrentState != SwitcherooState.On && CurrentState != SwitcherooState.TransitionToOn)
            {
                CurrentState = SwitcherooState.TransitionToOn;
            }
        }

        public void SwitchToOff()
        {
            if (CurrentState != SwitcherooState.Off && CurrentState != SwitcherooState.TransitionToOff)
            {
                CurrentState = SwitcherooState.TransitionToOff;
            }
        }

        public void Dispose()
        {
            Ticker.OnUpdate -= Update;
        }
    }
}
