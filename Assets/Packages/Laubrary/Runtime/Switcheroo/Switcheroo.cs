using System.Collections.Generic;
using System;
using UnityEngine;
using Laubrary.LaubraryTicker;

namespace Laubrary.Switcheroo
{
    /// <summary>
    /// Switcheroo is a stateful transition manager, enabling smooth transitions between "On" and "Off" states.
    /// It supports custom transition actions, event hooks for state changes, and time-based progress using Unity's update loop.
    /// Uses the LaubraryTicker system. Only ticks while actively transitioning — idle instances have zero Ticker overhead.
    /// </summary>
    public class Switcheroo
    {
        public enum SwitcherooState { On, Off, TransitionToOn, TransitionToOff }
        public SwitcherooState CurrentState { get; private set; } = SwitcherooState.Off;
        public Dictionary<string, Action<float>> transitions = new();
        private float progress = 0f;
        public float transitionSpeed = 1f;
        public event Action WhenOn;
        public event Action WhenOff;
        private bool _isSubscribed;

        public Switcheroo()
        {
            Subscribe();
        }

        public void AddTransition(string id, Action<float> transition)
        {
            transitions.Add(id, transition);
        }

        private void Subscribe()
        {
            if (!_isSubscribed)
            {
                Ticker.OnUpdate += Update;
                _isSubscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (_isSubscribed)
            {
                Ticker.OnUpdate -= Update;
                _isSubscribed = false;
            }
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
                        ResolveTransitions();
                        Unsubscribe();
                        return;
                    }
                    break;
                case SwitcherooState.TransitionToOff:
                    progress -= deltaTime / transitionSpeed;
                    if (progress <= 0f)
                    {
                        progress = 0f;
                        CurrentState = SwitcherooState.Off;
                        WhenOff?.Invoke();
                        ResolveTransitions();
                        Unsubscribe();
                        return;
                    }
                    break;
                default:
                    ResolveTransitions();
                    Unsubscribe();
                    return;
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

        public void Flip()
        {
            if (CurrentState == SwitcherooState.On || CurrentState == SwitcherooState.TransitionToOn)
                SwitchToOff();
            else
                SwitchToOn();
        }

        /// <summary>Transitions progress toward 1. Pass force=true to restart even if already transitioning to On.</summary>
        public void SwitchToOn(bool force = false)
        {
            if (force || (CurrentState != SwitcherooState.On && CurrentState != SwitcherooState.TransitionToOn))
            {
                CurrentState = SwitcherooState.TransitionToOn;
                Subscribe();
            }
        }

        /// <summary>Transitions progress toward 0. Pass force=true to restart even if already transitioning to Off.</summary>
        public void SwitchToOff(bool force = false)
        {
            if (force || (CurrentState != SwitcherooState.Off && CurrentState != SwitcherooState.TransitionToOff))
            {
                CurrentState = SwitcherooState.TransitionToOff;
                Subscribe();
            }
        }

        /// <summary>
        /// Directly sets progress to any value between 0 and 1. Setting to 0 resolves CurrentState
        /// to Off, setting to 1 resolves to On. Intermediate values leave CurrentState unchanged.
        /// Immediately applies the new value to all registered transitions.
        /// </summary>
        public void SetProgress(float value)
        {
            progress = Mathf.Clamp01(value);
            if (progress >= 1f) { CurrentState = SwitcherooState.On; Unsubscribe(); }
            else if (progress <= 0f) { CurrentState = SwitcherooState.Off; Unsubscribe(); }
            ResolveTransitions();
        }

        public void Dispose()
        {
            Unsubscribe();
        }
    }
}
