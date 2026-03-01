using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Lau_StatefulUI
{
    /// <summary>
    /// Opt-in singleton manager for directional navigation between StatefulUI elements.
    /// Responds to WASD keys and gamepad left stick. Navigates based on screen-space positions.
    /// Only one element can hold the Navigated state at a time.
    /// Add this component to a GameObject in your scene to enable navigation.
    /// </summary>
    public class StatefulUINavigator : MonoBehaviour
    {
        private static StatefulUINavigator _instance;

        private readonly List<StatefulUI> _elements = new();
        private StatefulUI _current;

        private Vector2 _heldDirection;
        private float _nextNavigateTime;

        private const float Deadzone = 0.5f;
        private const float InitialDelay = 0.4f;
        private const float RepeatRate = 0.12f;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>Registers a StatefulUI element for directional navigation. Called automatically by StatefulUI on enable.</summary>
        public static void Register(StatefulUI element)
        {
            if (_instance == null || _instance._elements.Contains(element)) return;
            _instance._elements.Add(element);
        }

        /// <summary>Unregisters a StatefulUI element. Called automatically by StatefulUI on disable.</summary>
        public static void Unregister(StatefulUI element)
        {
            if (_instance == null) return;
            _instance._elements.Remove(element);
            if (_instance._current == element)
                _instance.SetCurrent(null);
        }

        private void Update()
        {
            Vector2 input = ReadInput();

            if (input.magnitude < Deadzone)
            {
                _heldDirection = Vector2.zero;
                return;
            }

            Vector2 direction = SnapToCardinal(input);

            if (direction != _heldDirection)
            {
                _heldDirection = direction;
                _nextNavigateTime = Time.unscaledTime + InitialDelay;
                Navigate(direction);
            }
            else if (Time.unscaledTime >= _nextNavigateTime)
            {
                _nextNavigateTime = Time.unscaledTime + RepeatRate;
                Navigate(direction);
            }
        }

        private Vector2 ReadInput()
        {
            Vector2 input = Vector2.zero;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) input.y += 1f;
                if (Keyboard.current.sKey.isPressed) input.y -= 1f;
                if (Keyboard.current.aKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed) input.x += 1f;
            }

            if (Gamepad.current != null)
                input += Gamepad.current.leftStick.ReadValue();

            return Vector2.ClampMagnitude(input, 1f);
        }

        private static Vector2 SnapToCardinal(Vector2 input)
        {
            return Mathf.Abs(input.x) >= Mathf.Abs(input.y)
                ? new Vector2(Mathf.Sign(input.x), 0f)
                : new Vector2(0f, Mathf.Sign(input.y));
        }

        private void Navigate(Vector2 direction)
        {
            if (_elements.Count == 0) return;

            if (_current == null)
            {
                SetCurrent(GetClosestToCenter());
                return;
            }

            StatefulUI next = FindBestCandidate(direction, _current.GetScreenPosition());
            if (next != null)
                SetCurrent(next);
        }

        private StatefulUI FindBestCandidate(Vector2 direction, Vector2 fromPos)
        {
            StatefulUI best = null;
            float bestScore = float.MinValue;

            foreach (StatefulUI candidate in _elements)
            {
                if (candidate == _current) continue;

                Vector2 toCandidate = candidate.GetScreenPosition() - fromPos;
                float forward = Vector2.Dot(direction, toCandidate);

                if (forward <= 0f) continue;

                // Cross product magnitude gives lateral offset — penalises elements not straight ahead
                float lateral = Mathf.Abs(direction.x * toCandidate.y - direction.y * toCandidate.x);
                float score = forward / (1f + lateral);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private StatefulUI GetClosestToCenter()
        {
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            StatefulUI closest = null;
            float closestDist = float.MaxValue;

            foreach (StatefulUI element in _elements)
            {
                float dist = Vector2.Distance(element.GetScreenPosition(), center);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = element;
                }
            }

            return closest;
        }

        private void SetCurrent(StatefulUI next)
        {
            if (_current == next) return;
            _current?.SetNavigated(false);
            _current = next;
            _current?.SetNavigated(true);
        }
    }
}
