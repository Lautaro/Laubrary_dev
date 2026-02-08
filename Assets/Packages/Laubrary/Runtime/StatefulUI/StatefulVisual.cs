using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Laubrary.Lau_StatefulUI
{
    public class StatefulVisual : MonoBehaviour
    {
        [SerializeField] private StateDimension respondsToDimension = StateDimension.Interaction;

        [SerializeField] private InteractionStateOverrides interactionOverrides = InteractionStateOverrides.None;
        [SerializeField] private InteractionStateConfig customNormalConfig = new InteractionStateConfig();
        [SerializeField] private InteractionStateConfig customHoverConfig = new InteractionStateConfig();
        [SerializeField] private InteractionStateConfig customSelectedConfig = new InteractionStateConfig();
        [SerializeField] private InteractionStateConfig customClickedConfig = new InteractionStateConfig();
        [SerializeField] private InteractionStateConfig customDisabledConfig = new InteractionStateConfig();

        [SerializeField] private BooleanStateOverrides booleanOverrides = BooleanStateOverrides.None;
        [SerializeField] private BooleanStateConfig customNavigatedConfig = new BooleanStateConfig();
        [SerializeField] private BooleanStateConfig customToggledConfig = new BooleanStateConfig();
        [SerializeField] private BooleanStateConfig customFocusedConfig = new BooleanStateConfig();

        private StatefulUI _parentStatefulUI;
        private Graphic _graphicComponent;
        private Image _imageComponent;
        private RawImage _rawImageComponent;
        private TextMeshProUGUI _textComponent;
        private RectTransform _rectTransform;
        
        [SerializeField, HideInInspector] private Vector2 _originalPosition;
        [SerializeField, HideInInspector] private Vector3 _originalScale;
        [SerializeField, HideInInspector] private Quaternion _originalRotation;
        [SerializeField, HideInInspector] private Color _originalColor;
        [SerializeField, HideInInspector] private Sprite _originalSprite;
        [SerializeField, HideInInspector] private bool _hasStoredOriginals;

        private bool _isTransitioning;
        private float _transitionElapsed;
        private float _transitionDuration;
        private Color _transitionStartColor;
        private Color _transitionTargetColor;
        private Vector2 _transitionStartPosition;
        private Vector2 _transitionTargetPosition;
        private Vector3 _transitionStartScale;
        private Vector3 _transitionTargetScale;
        private Quaternion _transitionStartRotation;
        private Quaternion _transitionTargetRotation;

        public StateDimension RespondsToDimension => respondsToDimension;

        private void Update()
        {
            if (_isTransitioning)
            {
                _transitionElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_transitionElapsed / _transitionDuration);

                if (_graphicComponent != null)
                {
                    _graphicComponent.color = Color.Lerp(_transitionStartColor, _transitionTargetColor, t);
                }

                if (_rectTransform != null)
                {
                    _rectTransform.anchoredPosition = Vector2.Lerp(_transitionStartPosition, _transitionTargetPosition, t);
                    _rectTransform.localScale = Vector3.Lerp(_transitionStartScale, _transitionTargetScale, t);
                    _rectTransform.localRotation = Quaternion.Lerp(_transitionStartRotation, _transitionTargetRotation, t);
                }

                if (t >= 1f)
                {
                    if (_graphicComponent != null)
                    {
                        _graphicComponent.color = _transitionTargetColor;
                    }

                    if (_rectTransform != null)
                    {
                        _rectTransform.anchoredPosition = _transitionTargetPosition;
                        _rectTransform.localScale = _transitionTargetScale;
                        _rectTransform.localRotation = _transitionTargetRotation;
                    }

                    _isTransitioning = false;
                }
            }
        }

        private void StartTransition(Color targetColor, Vector2 targetPosition, Vector3 targetScale, Quaternion targetRotation, float duration)
        {
            _transitionStartColor = _graphicComponent != null ? _graphicComponent.color : _originalColor;
            _transitionStartPosition = _rectTransform != null ? _rectTransform.anchoredPosition : _originalPosition;
            _transitionStartScale = _rectTransform != null ? _rectTransform.localScale : _originalScale;
            _transitionStartRotation = _rectTransform != null ? _rectTransform.localRotation : _originalRotation;

            _transitionTargetColor = targetColor;
            _transitionTargetPosition = targetPosition;
            _transitionTargetScale = targetScale;
            _transitionTargetRotation = targetRotation;

            _transitionDuration = duration;
            _transitionElapsed = 0f;
            _isTransitioning = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CaptureOriginalValuesInEditor();
        }

        private void Reset()
        {
            CaptureOriginalValuesInEditor();
        }

        private void CaptureOriginalValuesInEditor()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform != null)
            {
                _originalPosition = _rectTransform.anchoredPosition;
                _originalScale = _rectTransform.localScale;
                _originalRotation = _rectTransform.localRotation;
            }

            Graphic graphic = GetComponent<Graphic>();
            if (graphic != null)
            {
                _originalColor = graphic.color;
            }

            Image image = GetComponent<Image>();
            if (image != null)
            {
                _originalSprite = image.sprite;
            }

            _hasStoredOriginals = true;
        }
#endif

        private void Awake()
        {
            FindParentStatefulUI();
            FindVisualComponent();
            CacheComponentReferences();
            CaptureOriginalValues();
        }

        private void CacheComponentReferences()
        {
            _rectTransform = GetComponent<RectTransform>();
        }

        private void CaptureOriginalValues()
        {
            if (_hasStoredOriginals) return;

            if (_rectTransform != null)
            {
                _originalPosition = _rectTransform.anchoredPosition;
                _originalScale = _rectTransform.localScale;
                _originalRotation = _rectTransform.localRotation;
            }

            if (_graphicComponent != null)
            {
                _originalColor = _graphicComponent.color;
            }

            if (_imageComponent != null)
            {
                _originalSprite = _imageComponent.sprite;
            }

            _hasStoredOriginals = true;
        }

        private void FindParentStatefulUI()
        {
            _parentStatefulUI = GetComponent<StatefulUI>();
            
            if (_parentStatefulUI == null)
            {
                _parentStatefulUI = GetComponentInParent<StatefulUI>();
            }
            
            if (_parentStatefulUI == null)
            {
                Debug.LogWarning($"StatefulVisual on {gameObject.name} could not find a StatefulUI component on this GameObject or any parent.", this);
            }
        }

        private void FindVisualComponent()
        {
            _imageComponent = GetComponent<Image>();
            if (_imageComponent != null)
            {
                _graphicComponent = _imageComponent;
                return;
            }

            _rawImageComponent = GetComponent<RawImage>();
            if (_rawImageComponent != null)
            {
                _graphicComponent = _rawImageComponent;
                return;
            }

            _textComponent = GetComponent<TextMeshProUGUI>();
            if (_textComponent != null)
            {
                _graphicComponent = _textComponent;
                return;
            }

            Debug.LogWarning($"StatefulVisual on {gameObject.name} could not find Image, RawImage, or TextMeshProUGUI component.", this);
        }

        public void OnInteractionStateChanged(InteractionState state, float transitionDuration = 0f)
        {
            if (respondsToDimension != StateDimension.Interaction) return;

            InteractionStateConfig config = GetInteractionConfig(state);
            ApplyInteractionConfig(config, transitionDuration);
        }

        public void OnNavigatedChanged(bool isNavigated, float transitionDuration = 0f)
        {
            if (respondsToDimension != StateDimension.Navigated) return;

            BooleanStateConfig config = GetNavigatedConfig(isNavigated);
            ApplyBooleanConfig(config, transitionDuration);
        }

        public void OnToggledChanged(bool isToggled, float transitionDuration = 0f)
        {
            if (respondsToDimension != StateDimension.Toggled) return;

            BooleanStateConfig config = GetToggledConfig(isToggled);
            ApplyBooleanConfig(config, transitionDuration);
        }

        public void OnFocusedChanged(bool isFocused, float transitionDuration = 0f)
        {
            if (respondsToDimension != StateDimension.Focused) return;

            BooleanStateConfig config = GetFocusedConfig(isFocused);
            ApplyBooleanConfig(config, transitionDuration);
        }

        private InteractionStateConfig GetInteractionConfig(InteractionState state)
        {
            if (_parentStatefulUI == null) return null;

            switch (state)
            {
                case InteractionState.Normal:
                    return (interactionOverrides & InteractionStateOverrides.Normal) != 0 ? customNormalConfig : _parentStatefulUI.NormalConfig;
                case InteractionState.Hover:
                    return (interactionOverrides & InteractionStateOverrides.Hover) != 0 ? customHoverConfig : _parentStatefulUI.HoverConfig;
                case InteractionState.Selected:
                    return (interactionOverrides & InteractionStateOverrides.Selected) != 0 ? customSelectedConfig : _parentStatefulUI.SelectedConfig;
                case InteractionState.Clicked:
                    return (interactionOverrides & InteractionStateOverrides.Clicked) != 0 ? customClickedConfig : _parentStatefulUI.ClickedConfig;
                case InteractionState.Disabled:
                    return (interactionOverrides & InteractionStateOverrides.Disabled) != 0 ? customDisabledConfig : _parentStatefulUI.DisabledConfig;
                default:
                    return null;
            }
        }

        private BooleanStateConfig GetNavigatedConfig(bool isNavigated)
        {
            if (!isNavigated) return null;
            if (_parentStatefulUI == null) return null;
            return (booleanOverrides & BooleanStateOverrides.Navigated) != 0 ? customNavigatedConfig : _parentStatefulUI.NavigatedConfig;
        }

        private BooleanStateConfig GetToggledConfig(bool isToggled)
        {
            if (!isToggled) return null;
            if (_parentStatefulUI == null) return null;
            return (booleanOverrides & BooleanStateOverrides.Toggled) != 0 ? customToggledConfig : _parentStatefulUI.ToggledConfig;
        }

        private BooleanStateConfig GetFocusedConfig(bool isFocused)
        {
            if (!isFocused) return null;
            if (_parentStatefulUI == null) return null;
            return (booleanOverrides & BooleanStateOverrides.Focused) != 0 ? customFocusedConfig : _parentStatefulUI.FocusedConfig;
        }

        private void ApplyInteractionConfig(InteractionStateConfig config, float transitionDuration = 0f)
        {
            if (config == null || !config.custom)
            {
                ResetToOriginalValues(transitionDuration);
                return;
            }

            if (transitionDuration > 0f)
            {
                Color targetColor = (config.enabledModifiers & StateModifierFlags.Color) != 0 ? config.color : _originalColor;
                
                if ((config.enabledModifiers & StateModifierFlags.Alpha) != 0)
                {
                    float targetAlpha = config.alphaIsAdditive ? Mathf.Clamp01(targetColor.a + config.alpha) : Mathf.Clamp01(config.alpha);
                    targetColor.a = targetAlpha;
                }

                if ((config.enabledModifiers & StateModifierFlags.ColorValue) != 0)
                {
                    Color.RGBToHSV(targetColor, out float h, out float s, out float v);
                    float targetV = config.colorValueIsAdditive ? Mathf.Clamp01(v + config.colorValue) : Mathf.Clamp01(config.colorValue);
                    targetColor = Color.HSVToRGB(h, s, targetV);
                    float currentAlpha = targetColor.a;
                    targetColor.a = currentAlpha;
                }

                if ((config.enabledModifiers & StateModifierFlags.Saturation) != 0)
                {
                    Color.RGBToHSV(targetColor, out float h, out float s, out float v);
                    float targetS = config.saturationIsAdditive ? Mathf.Clamp01(s + config.saturation) : Mathf.Clamp01(config.saturation);
                    targetColor = Color.HSVToRGB(h, targetS, v);
                    float currentAlpha = targetColor.a;
                    targetColor.a = currentAlpha;
                }

                Vector2 targetPosition = (config.enabledModifiers & StateModifierFlags.Position) != 0
                    ? (config.positionIsAdditive ? _originalPosition + config.position : config.position)
                    : _originalPosition;

                Vector3 targetScale = (config.enabledModifiers & StateModifierFlags.Scale) != 0
                    ? (config.scaleIsAdditive ? _originalScale * (1f + config.scale) : Vector3.one * config.scale)
                    : _originalScale;

                Quaternion targetRotation = (config.enabledModifiers & StateModifierFlags.Rotation) != 0
                    ? _originalRotation * Quaternion.Euler(0, 0, config.rotation)
                    : _originalRotation;

                if ((config.enabledModifiers & StateModifierFlags.Sprite) != 0)
                {
                    ApplySprite(config.sprite);
                }
                else if (_imageComponent != null)
                {
                    ApplySprite(_originalSprite);
                }

                StartTransition(targetColor, targetPosition, targetScale, targetRotation, transitionDuration);
            }
            else
            {
                ApplyInteractionConfigImmediate(config);
            }
        }

        private void ApplyInteractionConfigImmediate(InteractionStateConfig config)
        {
            _isTransitioning = false;

            if ((config.enabledModifiers & StateModifierFlags.Color) != 0)
            {
                ApplyColor(config.color);
            }
            else
            {
                ApplyColor(_originalColor);
            }

            if ((config.enabledModifiers & StateModifierFlags.Alpha) != 0)
            {
                ApplyAlpha(config.alpha, config.alphaIsAdditive);
            }

            if ((config.enabledModifiers & StateModifierFlags.ColorValue) != 0)
            {
                ApplyColorValue(config.colorValue, config.colorValueIsAdditive);
            }

            if ((config.enabledModifiers & StateModifierFlags.Saturation) != 0)
            {
                ApplySaturation(config.saturation, config.saturationIsAdditive);
            }

            if ((config.enabledModifiers & StateModifierFlags.Position) != 0)
            {
                ApplyPosition(config.position, config.positionIsAdditive);
            }
            else
            {
                ApplyPosition(_originalPosition, false);
            }

            if ((config.enabledModifiers & StateModifierFlags.Scale) != 0)
            {
                ApplyScale(config.scale, config.scaleIsAdditive);
            }
            else
            {
                ApplyScale(_originalScale);
            }

            if ((config.enabledModifiers & StateModifierFlags.Rotation) != 0)
            {
                ApplyRotation(config.rotation, true);
            }
            else
            {
                ApplyRotation(_originalRotation);
            }

            if ((config.enabledModifiers & StateModifierFlags.Sprite) != 0)
            {
                ApplySprite(config.sprite);
            }
            else if (_imageComponent != null)
            {
                ApplySprite(_originalSprite);
            }
        }

        private void ApplyBooleanConfig(BooleanStateConfig config, float transitionDuration = 0f)
        {
            if (config == null || !config.custom)
            {
                ResetToOriginalValues(transitionDuration);
                return;
            }

            if (transitionDuration > 0f)
            {
                Color targetColor = _originalColor;
                
                if ((config.enabledModifiers & StateModifierFlags.Alpha) != 0)
                {
                    float targetAlpha = config.alphaIsAdditive ? Mathf.Clamp01(targetColor.a + config.alpha) : Mathf.Clamp01(config.alpha);
                    targetColor.a = targetAlpha;
                }

                if ((config.enabledModifiers & StateModifierFlags.ColorValue) != 0)
                {
                    Color.RGBToHSV(targetColor, out float h, out float s, out float v);
                    float targetV = config.colorValueIsAdditive ? Mathf.Clamp01(v + config.colorValue) : Mathf.Clamp01(config.colorValue);
                    targetColor = Color.HSVToRGB(h, s, targetV);
                    float currentAlpha = targetColor.a;
                    targetColor.a = currentAlpha;
                }

                if ((config.enabledModifiers & StateModifierFlags.Saturation) != 0)
                {
                    Color.RGBToHSV(targetColor, out float h, out float s, out float v);
                    float targetS = config.saturationIsAdditive ? Mathf.Clamp01(s + config.saturation) : Mathf.Clamp01(config.saturation);
                    targetColor = Color.HSVToRGB(h, targetS, v);
                    float currentAlpha = targetColor.a;
                    targetColor.a = currentAlpha;
                }

                Vector2 targetPosition = (config.enabledModifiers & StateModifierFlags.Position) != 0
                    ? (config.positionIsAdditive ? _originalPosition + config.position : config.position)
                    : _originalPosition;

                Vector3 targetScale = (config.enabledModifiers & StateModifierFlags.Scale) != 0
                    ? (config.scaleIsAdditive ? _originalScale * (1f + config.scale) : Vector3.one * config.scale)
                    : _originalScale;

                Quaternion targetRotation = (config.enabledModifiers & StateModifierFlags.Rotation) != 0
                    ? _originalRotation * Quaternion.Euler(0, 0, config.rotation)
                    : _originalRotation;

                if ((config.enabledModifiers & StateModifierFlags.Sprite) != 0)
                {
                    ApplySprite(config.sprite);
                }
                else if (_imageComponent != null)
                {
                    ApplySprite(_originalSprite);
                }

                StartTransition(targetColor, targetPosition, targetScale, targetRotation, transitionDuration);
            }
            else
            {
                ApplyBooleanConfigImmediate(config);
            }
        }

        private void ApplyBooleanConfigImmediate(BooleanStateConfig config)
        {
            _isTransitioning = false;

            ApplyColor(_originalColor);

            if ((config.enabledModifiers & StateModifierFlags.Alpha) != 0)
            {
                ApplyAlpha(config.alpha, config.alphaIsAdditive);
            }

            if ((config.enabledModifiers & StateModifierFlags.ColorValue) != 0)
            {
                ApplyColorValue(config.colorValue, config.colorValueIsAdditive);
            }

            if ((config.enabledModifiers & StateModifierFlags.Saturation) != 0)
            {
                ApplySaturation(config.saturation, config.saturationIsAdditive);
            }

            if ((config.enabledModifiers & StateModifierFlags.Position) != 0)
            {
                ApplyPosition(config.position, config.positionIsAdditive);
            }
            else
            {
                ApplyPosition(_originalPosition, false);
            }

            if ((config.enabledModifiers & StateModifierFlags.Scale) != 0)
            {
                ApplyScale(config.scale, config.scaleIsAdditive);
            }
            else
            {
                ApplyScale(_originalScale);
            }

            if ((config.enabledModifiers & StateModifierFlags.Rotation) != 0)
            {
                ApplyRotation(config.rotation, true);
            }
            else
            {
                ApplyRotation(_originalRotation);
            }

            if ((config.enabledModifiers & StateModifierFlags.Sprite) != 0)
            {
                ApplySprite(config.sprite);
            }
            else if (_imageComponent != null)
            {
                ApplySprite(_originalSprite);
            }
        }

        private void ApplyColor(Color color)
        {
            if (_graphicComponent != null)
            {
                _graphicComponent.color = color;
            }
        }

        private void ApplyAlpha(float alpha, bool isAdditive)
        {
            if (_graphicComponent != null)
            {
                Color currentColor = _graphicComponent.color;
                if (isAdditive)
                {
                    currentColor.a = Mathf.Clamp01(currentColor.a + alpha);
                }
                else
                {
                    currentColor.a = Mathf.Clamp01(alpha);
                }
                _graphicComponent.color = currentColor;
            }
        }

        private void ApplyColorValue(float value, bool isAdditive)
        {
            if (_graphicComponent != null)
            {
                Color currentColor = _graphicComponent.color;
                Color.RGBToHSV(currentColor, out float h, out float s, out float v);
                
                if (isAdditive)
                {
                    v = Mathf.Clamp01(v + value);
                }
                else
                {
                    v = Mathf.Clamp01(value);
                }
                
                currentColor = Color.HSVToRGB(h, s, v);
                currentColor.a = _graphicComponent.color.a;
                _graphicComponent.color = currentColor;
            }
        }

        private void ApplySaturation(float saturation, bool isAdditive)
        {
            if (_graphicComponent != null)
            {
                Color currentColor = _graphicComponent.color;
                Color.RGBToHSV(currentColor, out float h, out float s, out float v);
                
                if (isAdditive)
                {
                    s = Mathf.Clamp01(s + saturation);
                }
                else
                {
                    s = Mathf.Clamp01(saturation);
                }
                
                currentColor = Color.HSVToRGB(h, s, v);
                currentColor.a = _graphicComponent.color.a;
                _graphicComponent.color = currentColor;
            }
        }

        private void ApplyPosition(Vector2 position, bool isAdditive)
        {
            if (_rectTransform != null)
            {
                if (isAdditive)
                {
                    _rectTransform.anchoredPosition = _originalPosition + position;
                }
                else
                {
                    _rectTransform.anchoredPosition = position;
                }
            }
        }

        private void ApplyScale(float scale, bool isAdditive)
        {
            if (_rectTransform != null)
            {
                if (isAdditive)
                {
                    _rectTransform.localScale = _originalScale * (1f + scale);
                }
                else
                {
                    _rectTransform.localScale = Vector3.one * scale;
                }
            }
        }

        private void ApplyScale(Vector3 scale)
        {
            if (_rectTransform != null)
            {
                _rectTransform.localScale = scale;
            }
        }

        private void ApplyRotation(float rotation, bool isAdditive)
        {
            if (_rectTransform != null)
            {
                if (isAdditive)
                {
                    _rectTransform.localRotation = _originalRotation * Quaternion.Euler(0, 0, rotation);
                }
                else
                {
                    _rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
                }
            }
        }

        private void ApplyRotation(Quaternion rotation)
        {
            if (_rectTransform != null)
            {
                _rectTransform.localRotation = rotation;
            }
        }

        private void ApplySprite(Sprite sprite)
        {
            if (_imageComponent != null && sprite != null)
            {
                _imageComponent.sprite = sprite;
            }
        }

        private void ResetToOriginalValues(float transitionDuration = 0f)
        {
            if (transitionDuration > 0f)
            {
                if (_imageComponent != null && _originalSprite != null)
                {
                    _imageComponent.sprite = _originalSprite;
                }

                StartTransition(_originalColor, _originalPosition, _originalScale, _originalRotation, transitionDuration);
            }
            else
            {
                ResetToOriginalValuesImmediate();
            }
        }

        private void ResetToOriginalValuesImmediate()
        {
            _isTransitioning = false;

            if (_graphicComponent != null)
            {
                _graphicComponent.color = _originalColor;
            }

            if (_rectTransform != null)
            {
                _rectTransform.anchoredPosition = _originalPosition;
                _rectTransform.localScale = _originalScale;
                _rectTransform.localRotation = _originalRotation;
            }

            if (_imageComponent != null && _originalSprite != null)
            {
                _imageComponent.sprite = _originalSprite;
            }
        }

        public void ResetToDefaults()
        {
            interactionOverrides = InteractionStateOverrides.None;
            booleanOverrides = BooleanStateOverrides.None;
        }
    }
}
