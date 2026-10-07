using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Zoetrope;
using Laubrary.ZoeCharacter;

namespace Laubrary.ZoetropeZoeCharacter
{
    /// <summary>
    /// Plays a side-view character's command list (<see cref="SideViewCommand"/>): reads the buttons and the
    /// stick, resolves the stick to a direction relative to the facing, and raises the matching declared
    /// action. Attached and configured by <see cref="SideViewPlayerControllerSpec"/>.
    ///
    /// Matching: on a button press the command with that button and the EXACT direction wins, then that
    /// button with Any. A stick-only command fires when the stick enters its direction and stays there for
    /// its hold time with no button pressed meanwhile.
    /// </summary>
    [DisallowMultipleComponent]
    public class SideViewCommandDriver : MonoBehaviour
    {
        List<SideViewCommand> _commands = new List<SideViewCommand>();
        readonly Dictionary<string, InputAction> _buttons = new Dictionary<string, InputAction>();
        float _threshold = 0.5f;

        ReactionFxPlayer _reactions;
        SideViewMotionDriver _mover;
        IZoeInputSource _input;

        StickDirection _lastDir = StickDirection.Neutral;
        float _dirSince;
        bool _pressedSinceDir;
        SideViewCommand _firedStick;   // the stick-only command already fired for the current direction

        ReactionFxPlayer Reactions => _reactions != null ? _reactions : (_reactions = GetComponent<ReactionFxPlayer>());
        SideViewMotionDriver Mover => _mover != null ? _mover : (_mover = GetComponent<SideViewMotionDriver>());
        IZoeInputSource InputSource => _input != null ? _input : (_input = GetComponent<IZoeInputSource>());

        public void Configure(IList<SideViewCommand> commands, InputActionMap map, float directionThreshold)
        {
            _commands = commands != null ? new List<SideViewCommand>(commands) : new List<SideViewCommand>();
            _threshold = directionThreshold;
            foreach (var a in _buttons.Values) a.Disable();
            _buttons.Clear();
            if (map == null) return;
            foreach (var c in _commands)
            {
                if (c == null || string.IsNullOrEmpty(c.button) || _buttons.ContainsKey(c.button)) continue;
                var action = map.FindAction(c.button);
                if (action == null) { Debug.LogWarning($"[SideView] No button action \"{c.button}\" in the controls' Player map.", this); continue; }
                _buttons[c.button] = action;
                if (isActiveAndEnabled) action.Enable();
            }
        }

        // Subscribed lazily: the spawner may add the action player after this component.
        bool _subscribed;
        void EnsureSubscribed()
        {
            if (_subscribed || Reactions == null) return;
            Reactions.EventFinished += OnFinished;
            _subscribed = true;
        }

        void OnEnable()  { foreach (var a in _buttons.Values) a.Enable(); }
        void OnDisable()
        {
            foreach (var a in _buttons.Values) a.Disable();
            if (_subscribed && _reactions != null) _reactions.EventFinished -= OnFinished;
            _subscribed = false;
        }

        void OnFinished(string id, bool interrupted)
        {
            if (interrupted || Mover == null) return;
            foreach (var c in _commands)
                if (c != null && c.turnWhenDone && string.Equals(c.eventId, id, System.StringComparison.OrdinalIgnoreCase))
                { Mover.Turn(); return; }
        }

        void Update()
        {
            if (Reactions == null) return;
            EnsureSubscribed();
            var dir = ReadDirection();
            if (dir != _lastDir) { _lastDir = dir; _dirSince = Time.time; _pressedSinceDir = false; _firedStick = null; }

            // Buttons: the most specific command for the pressed button wins.
            foreach (var kv in _buttons)
            {
                if (!kv.Value.WasPressedThisFrame()) continue;
                _pressedSinceDir = true;
                var cmd = Find(kv.Key, dir) ?? Find(kv.Key, StickDirection.Any);
                if (cmd != null) Reactions.Raise(cmd.eventId);
            }

            // Stick-only: entered this direction, held for the hold time, no button pressed meanwhile.
            if (_pressedSinceDir || _firedStick != null) return;
            var stick = Find("", dir);
            if (stick != null && Time.time - _dirSince >= stick.holdSeconds)
            {
                _firedStick = stick;
                Reactions.Raise(stick.eventId);
            }
        }

        SideViewCommand Find(string button, StickDirection dir)
        {
            foreach (var c in _commands)
                if (c != null && c.direction == dir && string.Equals(c.button ?? "", button, System.StringComparison.Ordinal))
                    return c;
            return null;
        }

        StickDirection ReadDirection()
        {
            Vector2 s = InputSource != null ? InputSource.ReadMove() : Vector2.zero;
            if (s.magnitude < _threshold) return StickDirection.Neutral;
            bool right = Mover == null || Mover.FacingRight;
            float fwd = right ? s.x : -s.x;
            int sector = Mathf.RoundToInt(Mathf.Atan2(s.y, fwd) * Mathf.Rad2Deg / 45f);   // 0 = forward, 2 = up
            switch (sector)
            {
                case 0: return StickDirection.Forward;
                case 1: return StickDirection.ForwardUp;
                case 2: return StickDirection.Up;
                case 3: return StickDirection.BackUp;
                case -3: return StickDirection.BackDown;
                case -2: return StickDirection.Down;
                case -1: return StickDirection.ForwardDown;
                default: return StickDirection.Back;   // ±4
            }
        }
    }
}
