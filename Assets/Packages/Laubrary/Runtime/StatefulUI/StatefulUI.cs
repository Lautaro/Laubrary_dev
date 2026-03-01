using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Laubrary.Lau_StatefulUI
{
    public class StatefulUI : MonoBehaviour, 
        IPointerEnterHandler, 
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler
    {
        private static readonly Dictionary<string, List<StatefulUI>> RadioGroups = new Dictionary<string, List<StatefulUI>>();

        [Header("Interaction States")]
        [SerializeField] private InteractionStateConfig normalConfig = new InteractionStateConfig();
        [SerializeField] private InteractionStateConfig hoverConfig = new InteractionStateConfig();
        [SerializeField] private InteractionStateConfig selectedConfig = new InteractionStateConfig();
        [SerializeField] private bool setSelectedOnClick = false;
        [SerializeField] private InteractionStateConfig clickedConfig = new InteractionStateConfig();
        [SerializeField] private InteractionStateConfig disabledConfig = new InteractionStateConfig();
        [SerializeField] private BooleanStateConfig navigatedConfig = new BooleanStateConfig();
        [SerializeField] private BooleanStateConfig toggledConfig = new BooleanStateConfig();
        [SerializeField] private bool setToggledOnClick = false;
        [SerializeField] private BooleanStateConfig focusedConfig = new BooleanStateConfig();

        [Header("Radio Group")]
        [SerializeField] private string radioGroupId = "";
        [SerializeField] private RadioGroupBehavior groupBehavior = RadioGroupBehavior.SetToggled;

        [SerializeField] private bool applyVisualToSelf = false;

        [SerializeField] private float transitionDuration = 0f;

        [Header("Events")]
        public UnityEvent<InteractionState> OnInteractionStateChanged = new UnityEvent<InteractionState>();
        public UnityEvent<bool> OnNavigatedChanged = new UnityEvent<bool>();
        public UnityEvent<bool> OnToggledChanged = new UnityEvent<bool>();
        public UnityEvent<bool> OnFocusedChanged = new UnityEvent<bool>();

        private InteractionState _currentInteractionState = InteractionState.Normal;
        private bool _isSelected = false;
        private bool _isNavigated = false;
        private bool _isToggled = false;
        private bool _isFocused = false;
        private bool _isPointerOver = false;
        private bool _isPointerDown = false;

        private StatefulVisual[] _childVisuals;
        private Dictionary<GameObject, bool> _originalGameObjectStates = new Dictionary<GameObject, bool>();
        private StatefulVisual _selfVisual;
        private RectTransform _rectTransform;
        private Canvas _rootCanvas;

        public InteractionState CurrentInteractionState => _currentInteractionState;
        public bool IsSelected => _isSelected;
        public bool IsNavigated => _isNavigated;
        public bool IsToggled => _isToggled;
        public bool IsFocused => _isFocused;

        public InteractionStateConfig NormalConfig => normalConfig;
        public InteractionStateConfig HoverConfig => hoverConfig;
        public InteractionStateConfig SelectedConfig => selectedConfig;
        public InteractionStateConfig ClickedConfig => clickedConfig;
        public InteractionStateConfig DisabledConfig => disabledConfig;
        public BooleanStateConfig NavigatedConfig => navigatedConfig;
        public BooleanStateConfig ToggledConfig => toggledConfig;
        public BooleanStateConfig FocusedConfig => focusedConfig;
        public float TransitionDuration => transitionDuration;

        /// <summary>Returns the screen-space center of this element, used by StatefulUINavigator for directional navigation.</summary>
        public Vector2 GetScreenPosition()
        {
            if (_rectTransform == null) return Vector2.zero;
            if (_rootCanvas == null) return _rectTransform.position;

            return _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? (Vector2)_rectTransform.position
                : RectTransformUtility.WorldToScreenPoint(_rootCanvas.worldCamera, _rectTransform.position);
        }

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            Canvas canvas = GetComponentInParent<Canvas>();
            _rootCanvas = canvas != null ? canvas.rootCanvas : null;

            _childVisuals = GetComponentsInChildren<StatefulVisual>(true);
            InitializeSelfVisual();
            CaptureOriginalGameObjectStates();
            RegisterToRadioGroup();
        }

        private void OnEnable()
        {
            StatefulUINavigator.Register(this);
        }

        private void OnDisable()
        {
            StatefulUINavigator.Unregister(this);
        }

        private void OnDestroy()
        {
            UnregisterFromRadioGroup();
        }

        private void Start()
        {
            UpdateInteractionState();
            NotifyChildrenAllDimensions();
        }

        private void RegisterToRadioGroup()
        {
            if (string.IsNullOrEmpty(radioGroupId)) return;

            if (!RadioGroups.ContainsKey(radioGroupId))
            {
                RadioGroups[radioGroupId] = new List<StatefulUI>();
            }

            if (!RadioGroups[radioGroupId].Contains(this))
            {
                RadioGroups[radioGroupId].Add(this);
            }
        }

        private void UnregisterFromRadioGroup()
        {
            if (string.IsNullOrEmpty(radioGroupId)) return;

            if (RadioGroups.ContainsKey(radioGroupId))
            {
                RadioGroups[radioGroupId].Remove(this);
                if (RadioGroups[radioGroupId].Count == 0)
                {
                    RadioGroups.Remove(radioGroupId);
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isPointerOver = true;
            UpdateInteractionState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isPointerOver = false;
            UpdateInteractionState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPointerDown = true;
            UpdateInteractionState();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPointerDown = false;
            UpdateInteractionState();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            HandleClick();
        }

        private void HandleClick()
        {
            if (!string.IsNullOrEmpty(radioGroupId))
            {
                HandleRadioGroupClick();
            }
            else
            {
                if (setSelectedOnClick)
                {
                    ToggleSelection();
                }
                if (setToggledOnClick)
                {
                    SetToggled(!_isToggled);
                }
            }
        }

        private void HandleRadioGroupClick()
        {
            if (groupBehavior == RadioGroupBehavior.SetToggled)
            {
                if (!_isToggled)
                {
                    SetToggled(true);
                    NotifyRadioGroupDeselect();
                }
            }
            else
            {
                if (!_isSelected)
                {
                    SetSelected(true);
                    NotifyRadioGroupDeselect();
                }
            }
        }

        private void NotifyRadioGroupDeselect()
        {
            if (!RadioGroups.ContainsKey(radioGroupId)) return;

            foreach (var other in RadioGroups[radioGroupId])
            {
                if (other != this)
                {
                    if (groupBehavior == RadioGroupBehavior.SetToggled)
                    {
                        other.SetToggled(false);
                    }
                    else
                    {
                        other.SetSelected(false);
                    }
                }
            }
        }

        private void ToggleSelection()
        {
            SetSelected(!_isSelected);
        }

        public void SetSelected(bool value)
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                UpdateInteractionState();
            }
        }

        private void UpdateInteractionState()
        {
            InteractionState newState = InteractionState.Normal;

            if (!enabled && disabledConfig.custom)
            {
                newState = InteractionState.Disabled;
            }
            else if (_isPointerDown && _isPointerOver && clickedConfig.custom)
            {
                newState = InteractionState.Clicked;
            }
            else if (_isPointerOver && hoverConfig.custom)
            {
                newState = InteractionState.Hover;
            }
            else if (_isSelected && selectedConfig.custom)
            {
                newState = InteractionState.Selected;
            }
            else
            {
                newState = InteractionState.Normal;
            }

            SetInteractionState(newState);
        }

        private void SetInteractionState(InteractionState newState)
        {
            if (_currentInteractionState != newState)
            {
                _currentInteractionState = newState;
                OnInteractionStateChanged?.Invoke(_currentInteractionState);
                ApplyInteractionStateGameObjects();
                NotifyChildrenInteractionState();
            }
        }

        public void SetNavigated(bool value)
        {
            if (_isNavigated != value)
            {
                _isNavigated = value;
                OnNavigatedChanged?.Invoke(_isNavigated);
                ApplyNavigatedStateGameObjects();
                NotifyChildrenNavigated();
            }
        }

        public void SetToggled(bool value)
        {
            if (_isToggled != value)
            {
                _isToggled = value;
                OnToggledChanged?.Invoke(_isToggled);
                ApplyToggledStateGameObjects();
                NotifyChildrenToggled();
            }
        }

        public void SetFocused(bool value)
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                OnFocusedChanged?.Invoke(_isFocused);
                ApplyFocusedStateGameObjects();
                NotifyChildrenFocused();
            }
        }

        private void NotifyChildrenAllDimensions()
        {
            NotifyChildrenInteractionState();
            NotifyChildrenNavigated();
            NotifyChildrenToggled();
            NotifyChildrenFocused();
        }

        private void NotifyChildrenInteractionState()
        {
            if (_selfVisual != null && _selfVisual.RespondsToDimension == StateDimension.Interaction)
            {
                _selfVisual.OnInteractionStateChanged(_currentInteractionState, transitionDuration);
            }
            
            foreach (var visual in _childVisuals)
            {
                if (visual != null && visual != _selfVisual && visual.RespondsToDimension == StateDimension.Interaction)
                {
                    visual.OnInteractionStateChanged(_currentInteractionState, transitionDuration);
                }
            }
        }

        private void NotifyChildrenNavigated()
        {
            if (_selfVisual != null && _selfVisual.RespondsToDimension == StateDimension.Navigated)
            {
                _selfVisual.OnNavigatedChanged(_isNavigated, transitionDuration);
            }
            
            foreach (var visual in _childVisuals)
            {
                if (visual != null && visual != _selfVisual && visual.RespondsToDimension == StateDimension.Navigated)
                {
                    visual.OnNavigatedChanged(_isNavigated, transitionDuration);
                }
            }
        }

        private void NotifyChildrenToggled()
        {
            if (_selfVisual != null && _selfVisual.RespondsToDimension == StateDimension.Toggled)
            {
                _selfVisual.OnToggledChanged(_isToggled, transitionDuration);
            }
            
            foreach (var visual in _childVisuals)
            {
                if (visual != null && visual != _selfVisual && visual.RespondsToDimension == StateDimension.Toggled)
                {
                    visual.OnToggledChanged(_isToggled, transitionDuration);
                }
            }
        }

        private void NotifyChildrenFocused()
        {
            if (_selfVisual != null && _selfVisual.RespondsToDimension == StateDimension.Focused)
            {
                _selfVisual.OnFocusedChanged(_isFocused, transitionDuration);
            }
            
            foreach (var visual in _childVisuals)
            {
                if (visual != null && visual != _selfVisual && visual.RespondsToDimension == StateDimension.Focused)
                {
                    visual.OnFocusedChanged(_isFocused, transitionDuration);
                }
            }
        }

        public void ForceSettingsToAllChildren()
        {
            _childVisuals = GetComponentsInChildren<StatefulVisual>(true);
            foreach (var visual in _childVisuals)
            {
                if (visual != null)
                {
                    visual.ResetToDefaults();
                }
            }
        }

        public void RefreshChildVisuals()
        {
            _childVisuals = GetComponentsInChildren<StatefulVisual>(true);
        }

        private void InitializeSelfVisual()
        {
            if (!applyVisualToSelf)
            {
                _selfVisual = null;
                return;
            }

            _selfVisual = GetComponent<StatefulVisual>();
            
            if (_selfVisual == null)
            {
                _selfVisual = gameObject.AddComponent<StatefulVisual>();
            }
        }

        private void CaptureOriginalGameObjectStates()
        {
            _originalGameObjectStates.Clear();
            
            CaptureGameObjectsFromConfig(normalConfig.gameObjects);
            CaptureGameObjectsFromConfig(hoverConfig.gameObjects);
            CaptureGameObjectsFromConfig(selectedConfig.gameObjects);
            CaptureGameObjectsFromConfig(clickedConfig.gameObjects);
            CaptureGameObjectsFromConfig(disabledConfig.gameObjects);
            CaptureGameObjectsFromConfig(navigatedConfig.gameObjects);
            CaptureGameObjectsFromConfig(toggledConfig.gameObjects);
            CaptureGameObjectsFromConfig(focusedConfig.gameObjects);
        }

        private void CaptureGameObjectsFromConfig(System.Collections.Generic.List<GameObjectToggle> gameObjects)
        {
            if (gameObjects == null) return;
            
            foreach (var toggle in gameObjects)
            {
                if (toggle.gameObject != null && !_originalGameObjectStates.ContainsKey(toggle.gameObject))
                {
                    _originalGameObjectStates[toggle.gameObject] = toggle.gameObject.activeSelf;
                }
            }
        }

        private void ApplyInteractionStateGameObjects()
        {
            InteractionStateConfig config = GetCurrentInteractionStateConfig();
            if (config != null && config.enabledModifiers.HasFlag(StateModifierFlags.GameObjects))
            {
                ApplyGameObjectToggles(config.gameObjects);
            }
            else
            {
                RestoreOriginalGameObjectStates();
            }
        }

        private void ApplyNavigatedStateGameObjects()
        {
            if (_isNavigated && navigatedConfig.custom && navigatedConfig.enabledModifiers.HasFlag(StateModifierFlags.GameObjects))
            {
                ApplyGameObjectToggles(navigatedConfig.gameObjects);
            }
            else
            {
                RestoreOriginalGameObjectStates();
            }
        }

        private void ApplyToggledStateGameObjects()
        {
            if (_isToggled && toggledConfig.custom && toggledConfig.enabledModifiers.HasFlag(StateModifierFlags.GameObjects))
            {
                ApplyGameObjectToggles(toggledConfig.gameObjects);
            }
            else
            {
                RestoreOriginalGameObjectStates();
            }
        }

        private void ApplyFocusedStateGameObjects()
        {
            if (_isFocused && focusedConfig.custom && focusedConfig.enabledModifiers.HasFlag(StateModifierFlags.GameObjects))
            {
                ApplyGameObjectToggles(focusedConfig.gameObjects);
            }
            else
            {
                RestoreOriginalGameObjectStates();
            }
        }

        private void ApplyGameObjectToggles(System.Collections.Generic.List<GameObjectToggle> gameObjects)
        {
            if (gameObjects == null) return;

            foreach (var toggle in gameObjects)
            {
                if (toggle.gameObject != null)
                {
                    toggle.gameObject.SetActive(toggle.enabled);
                }
            }
        }

        private void RestoreOriginalGameObjectStates()
        {
            foreach (var kvp in _originalGameObjectStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetActive(kvp.Value);
                }
            }
        }

        private InteractionStateConfig GetCurrentInteractionStateConfig()
        {
            switch (_currentInteractionState)
            {
                case InteractionState.Normal: return normalConfig;
                case InteractionState.Hover: return hoverConfig;
                case InteractionState.Selected: return selectedConfig;
                case InteractionState.Clicked: return clickedConfig;
                case InteractionState.Disabled: return disabledConfig;
                default: return normalConfig;
            }
        }
    }
}
