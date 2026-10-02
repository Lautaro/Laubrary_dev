using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.InputGuide
{
    /// <summary>
    /// Runtime owner for one game's controls. It owns a cloned InputActionAsset, tracks the last scheme
    /// that actually triggered one of those bound actions, and persists binding overrides.
    /// </summary>
    public sealed class InputGuideRuntime : MonoBehaviour
    {
        [Tooltip("Control catalog that supplies actions, scheme names, descriptions, and the persistence key.")]
        [SerializeField] ControlCatalog catalog;

        [Tooltip("Enable every action map in the cloned asset when this component starts.")]
        [SerializeField] bool enableActionsOnStart = true;

        InputActionAsset _actions;
        InputActionRebindingExtensions.RebindingOperation _rebind;
        InputAction _rebindAction;
        bool _rebindMapWasEnabled;
        Guid _rebindBindingId;
        InputSchemeKind _rebindScheme;

        public ControlCatalog Catalog => catalog;
        public InputActionAsset Actions => _actions;
        public InputSchemeKind ActiveScheme { get; private set; } = InputSchemeKind.Unknown;
        public bool IsRebinding => _rebind != null;
        public Guid RebindingBindingId => _rebindBindingId;
        public string RebindingPrompt { get; private set; }

        public event Action<InputSchemeKind> ActiveSchemeChanged;
        public event Action BindingsChanged;
        public event Action<bool> RebindingChanged;

        void Awake()
        {
            if (catalog == null || catalog.Actions == null)
            {
                Debug.LogError("InputGuideRuntime needs a ControlCatalog with an InputActionAsset.", this);
                enabled = false;
                return;
            }

            _actions = Instantiate(catalog.Actions);
            _actions.name = catalog.Actions.name + " (InputGuide Runtime)";
            LoadBindings();
            SubscribeToBoundActions();
            SetActiveScheme(Gamepad.current != null ? InputSchemeKind.Gamepad : InputSchemeKind.KeyboardMouse);
            if (enableActionsOnStart) _actions.Enable();
        }

        void OnDestroy()
        {
            CancelRebind();
            UnsubscribeFromBoundActions();
            if (_actions != null)
            {
                _actions.Disable();
                Destroy(_actions);
            }
        }

        void SubscribeToBoundActions()
        {
            if (_actions == null) return;
            foreach (var action in _actions)
            {
                action.started += OnBoundAction;
                action.performed += OnBoundAction;
            }
        }

        void UnsubscribeFromBoundActions()
        {
            if (_actions == null) return;
            foreach (var action in _actions)
            {
                action.started -= OnBoundAction;
                action.performed -= OnBoundAction;
            }
        }

        void OnBoundAction(InputAction.CallbackContext context)
        {
            var device = context.control?.device;
            if (device is Gamepad) SetActiveScheme(InputSchemeKind.Gamepad);
            else if (device is Keyboard || device is Mouse) SetActiveScheme(InputSchemeKind.KeyboardMouse);
        }

        void SetActiveScheme(InputSchemeKind scheme)
        {
            if (scheme == InputSchemeKind.Unknown || scheme == ActiveScheme) return;
            ActiveScheme = scheme;
            ActiveSchemeChanged?.Invoke(scheme);
        }

        public InputAction Resolve(InputActionReference reference)
        {
            if (_actions == null || reference == null || reference.action == null) return null;
            return _actions.FindAction(reference.action.id);
        }

        public InputAction FindAction(string nameOrId)
        {
            return _actions == null || string.IsNullOrWhiteSpace(nameOrId)
                ? null
                : _actions.FindAction(nameOrId, false);
        }

        public string GetBindingDisplay(InputActionReference reference, InputSchemeKind scheme)
        {
            var action = Resolve(reference);
            if (action == null) return string.Empty;
            return action.GetBindingDisplayString(InputBinding.MaskByGroup(catalog.BindingGroup(scheme)));
        }

        public ControlActionDescription GetDescription(InputActionReference reference)
        {
            return reference == null || reference.action == null ? null : catalog.FindDescription(reference.action.id);
        }

        public void SaveBindings()
        {
            if (_actions == null) return;
            PlayerPrefs.SetString(catalog.PersistenceKey, _actions.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public void LoadBindings()
        {
            if (_actions == null || !PlayerPrefs.HasKey(catalog.PersistenceKey)) return;
            string json = PlayerPrefs.GetString(catalog.PersistenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return;
            try { _actions.LoadBindingOverridesFromJson(json); }
            catch (Exception ex) { Debug.LogWarning("InputGuide could not load saved bindings: " + ex.Message, this); }
        }

        public void ResetBindings()
        {
            CancelRebind();
            if (_actions == null) return;
            _actions.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(catalog.PersistenceKey);
            PlayerPrefs.Save();
            BindingsChanged?.Invoke();
        }

        public bool BeginRebind(InputActionReference reference, Guid bindingId, InputSchemeKind scheme)
        {
            return BeginRebind(Resolve(reference), bindingId, scheme);
        }

        public bool BeginRebind(InputAction action, Guid bindingId, InputSchemeKind scheme)
        {
            CancelRebind();
            if (action == null || scheme == InputSchemeKind.Unknown) return false;

            int bindingIndex = FindBindingIndex(action, bindingId);
            if (bindingIndex < 0 || action.bindings[bindingIndex].isComposite) return false;

            _rebindAction = action;
            _rebindBindingId = bindingId;
            _rebindScheme = scheme;
            _rebindMapWasEnabled = action.actionMap != null && action.actionMap.enabled;
            if (_rebindMapWasEnabled) action.actionMap.Disable();

            string part = action.bindings[bindingIndex].isPartOfComposite
                ? " " + Nicify(action.bindings[bindingIndex].name)
                : string.Empty;
            RebindingPrompt = "Press a new " + SchemeName(scheme) + " control for " + action.name + part;

            _rebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithActionEventNotificationsBeingSuppressed()
                .OnPotentialMatch(OnPotentialMatch)
                .OnCancel(_ => FinishRebind(false))
                .OnComplete(_ => FinishRebind(true));
            _rebind.Start();
            RebindingChanged?.Invoke(true);
            return true;
        }

        void OnPotentialMatch(InputActionRebindingExtensions.RebindingOperation operation)
        {
            var control = operation.selectedControl;
            if (control == null) return;

            // Escape / B are the conventional cancel controls while the prompt is open. Existing bindings
            // may still use them; they are only reserved during the short capture operation itself.
            if (control == Keyboard.current?.escapeKey || control == Gamepad.current?.buttonEast)
            {
                operation.Cancel();
                return;
            }

            bool accepted = _rebindScheme == InputSchemeKind.Gamepad
                ? control.device is Gamepad
                : control.device is Keyboard || control.device is Mouse;
            if (!accepted)
            {
                operation.RemoveCandidate(control);
                return;
            }
            operation.Complete();
        }

        public void CancelRebind()
        {
            if (_rebind != null) _rebind.Cancel();
        }

        void FinishRebind(bool completed)
        {
            var operation = _rebind;
            var action = _rebindAction;
            bool reenable = _rebindMapWasEnabled;
            _rebind = null;
            _rebindAction = null;
            _rebindBindingId = Guid.Empty;
            RebindingPrompt = null;
            operation?.Dispose();
            if (reenable && action != null && action.actionMap != null) action.actionMap.Enable();

            if (completed)
            {
                SaveBindings();
                SetActiveScheme(_rebindScheme);
                BindingsChanged?.Invoke();
            }
            RebindingChanged?.Invoke(false);
        }

        public static int FindBindingIndex(InputAction action, Guid bindingId)
        {
            if (action == null || bindingId == Guid.Empty) return -1;
            for (int i = 0; i < action.bindings.Count; i++)
                if (action.bindings[i].id == bindingId) return i;
            return -1;
        }

        public bool BindingBelongsToScheme(InputAction action, int bindingIndex, InputSchemeKind scheme)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return false;
            string groups = action.bindings[bindingIndex].groups;
            if (string.IsNullOrWhiteSpace(groups) && action.bindings[bindingIndex].isPartOfComposite)
            {
                for (int i = bindingIndex - 1; i >= 0; i--)
                {
                    if (!action.bindings[i].isPartOfComposite)
                    {
                        groups = action.bindings[i].groups;
                        break;
                    }
                }
            }
            return GroupContains(groups, catalog.BindingGroup(scheme));
        }

        static bool GroupContains(string groups, string wanted)
        {
            if (string.IsNullOrWhiteSpace(groups) || string.IsNullOrWhiteSpace(wanted)) return false;
            var split = groups.Split(';');
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], wanted, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string SchemeName(InputSchemeKind scheme) => scheme == InputSchemeKind.Gamepad ? "gamepad" : "keyboard / mouse";
        static string Nicify(string value) => string.IsNullOrWhiteSpace(value) ? "binding" : char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
