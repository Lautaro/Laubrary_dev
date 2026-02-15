using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace Laubrary.SimpleMenu
{   
    public abstract class SimpleMenuBase : MonoBehaviour
    {
        private static bool isCreatingSubMenu = false;
        private const string DEFAULT_SETTINGS_FOLDER = "Assets/SimpleMenu Settings";

        private static Dictionary<string, Type> s_MenuTypeCache = null;
        private static bool s_MenuTypeCacheInitialized = false;

        protected GameObject menuContainer;
        protected GameObject contentContainer;
        protected Dictionary<string, SimpleMenuBase> subMenus = new Dictionary<string, SimpleMenuBase>();
        protected Dictionary<string, GameObject> controls = new Dictionary<string, GameObject>();
        protected SimpleMenuBase parentMenu;
        protected bool isMainMenu = false;
        protected bool isInitialized = false;

        [Header("Menu Settings")]
        [Tooltip("Settings for this menu. If null, will auto-find first available settings.")]
        public SimpleMenuSettings menuSettings;

        private SimpleMenuSettings cachedSettings;

        protected SimpleMenuSettings Settings
        {
            get
            {
                if (cachedSettings != null) return cachedSettings;

                if (menuSettings != null)
                {
                    cachedSettings = menuSettings;
                    return cachedSettings;
                }

                cachedSettings = FindMenuSettings();
                return cachedSettings;
            }
        }

        private SimpleMenuSettings FindMenuSettings()
        {
            #if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SimpleMenuSettings");
            if (guids.Length > 0)
            {
                foreach (string guid in guids)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    SimpleMenuSettings settings = UnityEditor.AssetDatabase.LoadAssetAtPath<SimpleMenuSettings>(path);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            #endif

            SimpleMenuSettings[] allSettings = Resources.FindObjectsOfTypeAll<SimpleMenuSettings>();
            
            if (allSettings.Length == 0)
            {
                Debug.LogWarning("No SimpleMenuSettings found in project. Using code-generated controls.");
                return null;
            }

            return allSettings[0];
        }

        protected virtual void Reset()
        {
            EnsureCanvasParent();
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                rectTransform = gameObject.AddComponent<RectTransform>();
            }
            if (rectTransform == null)
                Debug.LogWarning($"SimpleMenuBase requires a RectTransform but was not able to auto add one. On {gameObject.name}.");
        }

        protected virtual void Awake()
        {
            OverrideSettings();

            if (isCreatingSubMenu)
            {
                return;
            }

            if (!isInitialized && menuContainer == null)
            {
                isMainMenu = true;
                isInitialized = true;
                EnsureEventSystem();
                CreateMenuContainer();
                BuildMenu();
            }
        }

        private void EnsureCanvasParent()
        {
            if (GetComponentInParent<Canvas>() == null)
            {
                CreateCanvasParent();
            }
        }

        private void CreateCanvasParent()
        {
            GameObject canvasObj = new GameObject("Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            Transform currentParent = transform.parent;
            int siblingIndex = transform.GetSiblingIndex();

            if (currentParent != null)
            {
                canvasObj.transform.SetParent(currentParent);
                canvasObj.transform.SetSiblingIndex(siblingIndex);
            }

            transform.SetParent(canvasObj.transform);
        }

        private void EnsureEventSystem()
        {
            EventSystem existingEventSystem = FindAnyObjectByType<EventSystem>();
            if (existingEventSystem != null)
            {
                return;
            }

            GameObject eventSystemObj = new GameObject("EventSystem");
            EventSystem eventSystem = eventSystemObj.AddComponent<EventSystem>();

            #if UNITY_INPUTSYSTEM_PACKAGE
            var inputModule = eventSystemObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("SimpleMenu: Created EventSystem with InputSystemUIInputModule for UI interaction.");
            #else
            Debug.LogError("SimpleMenu: New Input System package is required but not found. UI interaction will not work. Please install com.unity.inputsystem package.");
            #endif
        }


        protected virtual void OverrideSettings()
        {
        }
        protected void CreateMenuContainer()
        {
            menuContainer = new GameObject(GetMenuName());
            menuContainer.transform.SetParent(transform, false);

            var hasRectTransform = menuContainer.GetComponent<RectTransform>();
            if (hasRectTransform == null)
            {
                RectTransform rectTransform = menuContainer.AddComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
            }

            VerticalLayoutGroup layoutGroup = menuContainer.AddComponent<VerticalLayoutGroup>();
            
            if (Settings != null)
            {
                layoutGroup.padding = new RectOffset(
                    Settings.paddingLeft,
                    Settings.paddingRight,
                    Settings.paddingTop,
                    Settings.paddingBottom
                );
            }
            else
            {
                layoutGroup.padding = new RectOffset(0, 0, 0, 0);
            }
            
            layoutGroup.spacing = 20;
            layoutGroup.childAlignment = TextAnchor.MiddleCenter;
            layoutGroup.reverseArrangement = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childScaleWidth = false;
            layoutGroup.childScaleHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;

            contentContainer = menuContainer;
        }

        protected virtual void BuildMenu()
        {
            CreateMenuHeader();

            FieldInfo[] fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (FieldInfo field in fields)
            {
                if (typeof(SimpleMenuBase).IsAssignableFrom(field.FieldType) && 
                    field.FieldType != typeof(SimpleMenuBase) &&
                    field.DeclaringType != typeof(SimpleMenuBase))
                {
                    object fieldValue = field.GetValue(this);
                    if (fieldValue != null)
                    {
                        Debug.LogWarning($"SimpleMenu: Field '{field.Name}' on '{GetType().Name}' should not have a reference assigned. " +
                                       $"Submenu fields are created dynamically at runtime. Clearing the reference.", this);
                        field.SetValue(this, null);
                    }
                    
                    SimpleSubMenuAttribute linkAttr = field.GetCustomAttribute<SimpleSubMenuAttribute>();
                    Type menuType = field.FieldType;
                    string label = linkAttr?.Label ?? GetMenuLabelFromType(menuType);
                    CreateButton(label, () => SwitchToMenu(menuType));
                    continue;
                }

                SimpleMenuTextBoxAttribute textBoxAttr = field.GetCustomAttribute<SimpleMenuTextBoxAttribute>();
                if (textBoxAttr != null)
                {
                    CreateTextBox(textBoxAttr);
                    continue;
                }

                SimpleMenuLabelAttribute labelAttr = field.GetCustomAttribute<SimpleMenuLabelAttribute>();
                if (labelAttr != null)
                {
                    CreateLabel(labelAttr);
                    continue;
                }

                SimpleMenuToggleAttribute toggleAttr = field.GetCustomAttribute<SimpleMenuToggleAttribute>();
                if (toggleAttr != null)
                {
                    CreateToggle(field, toggleAttr);
                    continue;
                }

                SimpleMenuDropdownAttribute dropdownAttr = field.GetCustomAttribute<SimpleMenuDropdownAttribute>();
                if (dropdownAttr != null)
                {
                    CreateDropdown(field, dropdownAttr);
                    continue;
                }

                SimpleMenuSliderAttribute sliderAttr = field.GetCustomAttribute<SimpleMenuSliderAttribute>();
                if (sliderAttr != null)
                {
                    CreateSlider(field, sliderAttr);
                    continue;
                }

                SimpleMenuInputFieldAttribute inputFieldAttr = field.GetCustomAttribute<SimpleMenuInputFieldAttribute>();
                if (inputFieldAttr != null)
                {
                    CreateInputField(field, inputFieldAttr);
                    continue;
                }
            }

            MethodInfo[] methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (MethodInfo method in methods)
            {
                SimpleMenuButtonAttribute buttonAttr = method.GetCustomAttribute<SimpleMenuButtonAttribute>();
                if (buttonAttr != null)
                {
                    CreateButton(buttonAttr.Label ?? method.Name, () => method.Invoke(this, null));
                    continue;
                }

                SubMenuAttribute subMenuAttr = method.GetCustomAttribute<SubMenuAttribute>();
                if (subMenuAttr != null)
                {
                    Debug.LogWarning($"[SubMenu] attribute on method '{method.Name}' is deprecated. Use [SimpleMenuLink] on a field instead.");
                    CreateButton(subMenuAttr.Label ?? subMenuAttr.MenuType.Name, () => SwitchToMenu(subMenuAttr.MenuType));
                }
            }
        }

        protected GameObject CreateButton(string label, Action onClick)
        {
            if (Settings == null || Settings.buttonPrefab == null)
            {
                Debug.LogError("SimpleMenuSettings or buttonPrefab is null. Cannot create button.");
                return null;
            }

            GameObject buttonObj = Instantiate(Settings.buttonPrefab, contentContainer.transform, false);
            buttonObj.name = label;

            TextMeshProUGUI textComponent = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = label;
                ApplyTextSettings(textComponent);
            }

            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => onClick?.Invoke());
                ApplyControlTint(button.targetGraphic as Image);
            }

            return buttonObj;
        }

        protected void CreateMenuHeader()
        {
            if (Settings == null || Settings.headerPrefab == null)
            {
                return;
            }

            GameObject headerObj = Instantiate(Settings.headerPrefab, contentContainer.transform, false);
            headerObj.name = "MenuHeader";

            TextMeshProUGUI textComponent = headerObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = GetMenuName();
            }

            VerticalLayoutGroup layoutGroup = contentContainer.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.spacing = 40;
            }
        }

        protected GameObject CreateTextBox(SimpleMenuTextBoxAttribute attr)
        {
            if (Settings == null || Settings.textBoxPrefab == null)
            {
                Debug.LogError("SimpleMenuSettings or textBoxPrefab is null. Cannot create text box.");
                return null;
            }

            GameObject textBoxObj = Instantiate(Settings.textBoxPrefab, contentContainer.transform, false);
            textBoxObj.name = "TextBox";

            TextMeshProUGUI textComponent = textBoxObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = attr.Text;
            }

            return textBoxObj;
        }

        protected GameObject CreateLabel(SimpleMenuLabelAttribute attr)
        {
            if (Settings == null || Settings.labelPrefab == null)
            {
                Debug.LogError("SimpleMenuSettings or labelPrefab is null. Cannot create label.");
                return null;
            }

            GameObject labelObj = Instantiate(Settings.labelPrefab, contentContainer.transform, false);
            labelObj.name = "Label";

            TextMeshProUGUI textComponent = labelObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = attr.Text;
                ApplyTextSettings(textComponent);
            }

            return labelObj;
        }

        protected void ApplyTextSettings(TMP_Text text)
        {
            if (Settings == null || text == null) return;

            if (Settings.useControlTextStyleOverride && Settings.controlTextStylePrefab != null)
            {
                TMP_Text styleSource = Settings.controlTextStylePrefab.GetComponentInChildren<TMP_Text>();
                if (styleSource != null)
                {
                    text.font = styleSource.font;
                    text.fontSharedMaterial = styleSource.fontSharedMaterial;
                    text.fontSize = styleSource.fontSize;
                    text.fontStyle = styleSource.fontStyle;
                    text.color = styleSource.color;
                    text.enableAutoSizing = styleSource.enableAutoSizing;
                    text.fontSizeMin = styleSource.fontSizeMin;
                    text.fontSizeMax = styleSource.fontSizeMax;
                    text.characterSpacing = styleSource.characterSpacing;
                    text.wordSpacing = styleSource.wordSpacing;
                    text.lineSpacing = styleSource.lineSpacing;
                    text.paragraphSpacing = styleSource.paragraphSpacing;
                    
                    text.colorGradient = styleSource.colorGradient;
                    text.colorGradientPreset = styleSource.colorGradientPreset;
                    text.enableVertexGradient = styleSource.enableVertexGradient;
                    
                    return;
                }
            }

            if (Settings.HasFontSize)
            {
                text.fontSize = Settings.fontSize;
            }

            if (Settings.autoSizeFont)
            {
                text.enableAutoSizing = true;
            }
        }

        protected void ApplyControlTint(Image image)
        {
            if (Settings == null || image == null) return;

            if (Settings.HasControlTint)
            {
                image.color = Settings.controlTintColor;
            }
        }

        protected GameObject CreateToggle(FieldInfo field, SimpleMenuToggleAttribute attr)
        {
            if (field.FieldType != typeof(bool))
            {
                Debug.LogError($"Field {field.Name} must be of type bool to use SimpleMenuToggle");
                return null;
            }

            if (Settings == null || Settings.togglePrefab == null)
            {
                Debug.LogError("SimpleMenuSettings or togglePrefab is null. Cannot create toggle.");
                return null;
            }

            GameObject toggleObj = Instantiate(Settings.togglePrefab, contentContainer.transform, false);
            toggleObj.name = attr.Label;

            TextMeshProUGUI textComponent = toggleObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = attr.Label;
                ApplyTextSettings(textComponent);
            }

            string persistenceKey = null;
            if (attr.PersistenceId != null)
            {
                persistenceKey = string.IsNullOrEmpty(attr.PersistenceId) ? null : attr.PersistenceId;
            }
            else
            {
                string menuName = GetMenuName().Replace(" ", "");
                string labelName = attr.Label.Replace(" ", "");
                persistenceKey = $"{menuName}_{labelName}";
            }

            bool defaultValue = (bool)field.GetValue(this);
            bool loadedValue = defaultValue;

            if (persistenceKey != null && PlayerPrefs.HasKey(persistenceKey))
            {
                loadedValue = PlayerPrefs.GetInt(persistenceKey, defaultValue ? 1 : 0) == 1;
                field.SetValue(this, loadedValue);
            }

            UnityEngine.UI.Toggle toggle = toggleObj.GetComponent<UnityEngine.UI.Toggle>();
            if (toggle != null)
            {
                toggle.isOn = loadedValue;
                toggle.onValueChanged.AddListener((value) =>
                {
                    field.SetValue(this, value);
                    if (persistenceKey != null)
                    {
                        PlayerPrefs.SetInt(persistenceKey, value ? 1 : 0);
                    }
                    InvokeCallback(attr.OnValueChanged, value);
                });
                var tintBackgroundImage = toggle.transform.Find("TintBackground").GetComponent<Image>(); 
                ApplyControlTint(tintBackgroundImage);
            }

            controls[field.Name] = toggleObj;
            return toggleObj;
        }

        protected GameObject CreateDropdown(FieldInfo field, SimpleMenuDropdownAttribute attr)
        {
            if (field.FieldType != typeof(string) && field.FieldType != typeof(int) && !field.FieldType.IsEnum)
            {
                Debug.LogError($"Field {field.Name} must be of type string, int, or enum to use SimpleMenuDropdown");
                return null;
            }

            if (Settings == null || Settings.dropdownPrefab == null)
            {
                Debug.LogError("SimpleMenuSettings or dropdownPrefab is null. Cannot create dropdown.");
                return null;
            }

            GameObject dropdownObj = Instantiate(Settings.dropdownPrefab, contentContainer.transform, false);
            dropdownObj.name = attr.Label;

            TextMeshProUGUI textComponent = dropdownObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = attr.Label;
                ApplyTextSettings(textComponent);
            }

            string persistenceKey = null;
            if (attr.PersistenceId != null)
            {
                persistenceKey = string.IsNullOrEmpty(attr.PersistenceId) ? null : attr.PersistenceId;
            }
            else
            {
                string menuName = GetMenuName().Replace(" ", "");
                string labelName = attr.Label.Replace(" ", "");
                persistenceKey = $"{menuName}_{labelName}";
            }

            TMP_Dropdown dropdown = dropdownObj.GetComponent<TMP_Dropdown>();
            if (dropdown != null)
            {
                dropdown.ClearOptions();

                if (field.FieldType.IsEnum)
                {
                    var enumNames = Enum.GetNames(field.FieldType);
                    dropdown.AddOptions(new System.Collections.Generic.List<string>(enumNames));
                    
                    int defaultValue = (int)field.GetValue(this);
                    int loadedValue = defaultValue;

                    if (persistenceKey != null && PlayerPrefs.HasKey(persistenceKey))
                    {
                        loadedValue = PlayerPrefs.GetInt(persistenceKey, defaultValue);
                        object enumValue = Enum.ToObject(field.FieldType, loadedValue);
                        field.SetValue(this, enumValue);
                    }

                    dropdown.value = loadedValue;
                }
                else
                {
                    string[] options = attr.Options;
                    if (!string.IsNullOrEmpty(attr.GetOptionsMethod))
                    {
                        MethodInfo getOptionsMethod = GetType().GetMethod(attr.GetOptionsMethod, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (getOptionsMethod != null && getOptionsMethod.ReturnType == typeof(string[]))
                        {
                            options = (string[])getOptionsMethod.Invoke(this, null);
                        }
                    }

                    if (options != null && options.Length > 0)
                    {
                        dropdown.AddOptions(new System.Collections.Generic.List<string>(options));

                        if (field.FieldType == typeof(string))
                        {
                            string defaultValue = (string)field.GetValue(this);
                            string loadedValue = defaultValue;

                            if (persistenceKey != null && PlayerPrefs.HasKey(persistenceKey))
                            {
                                loadedValue = PlayerPrefs.GetString(persistenceKey, defaultValue);
                                field.SetValue(this, loadedValue);
                            }

                            int index = System.Array.IndexOf(options, loadedValue);
                            if (index >= 0)
                            {
                                dropdown.value = index;
                            }
                        }
                        else if (field.FieldType == typeof(int))
                        {
                            int defaultValue = (int)field.GetValue(this);
                            int loadedValue = defaultValue;

                            if (persistenceKey != null && PlayerPrefs.HasKey(persistenceKey))
                            {
                                loadedValue = PlayerPrefs.GetInt(persistenceKey, defaultValue);
                                field.SetValue(this, loadedValue);
                            }

                            dropdown.value = Mathf.Clamp(loadedValue, 0, options.Length - 1);
                        }
                    }
                }

                dropdown.onValueChanged.AddListener((index) =>
                {
                    if (field.FieldType == typeof(string))
                    {
                        string newValue = dropdown.options[index].text;
                        field.SetValue(this, newValue);
                        if (persistenceKey != null)
                        {
                            PlayerPrefs.SetString(persistenceKey, newValue);
                        }
                        InvokeCallback(attr.OnValueChanged, newValue);
                    }
                    else if (field.FieldType == typeof(int))
                    {
                        field.SetValue(this, index);
                        if (persistenceKey != null)
                        {
                            PlayerPrefs.SetInt(persistenceKey, index);
                        }
                        InvokeCallback(attr.OnValueChanged, index);
                    }
                    else if (field.FieldType.IsEnum)
                    {
                        object enumValue = Enum.ToObject(field.FieldType, index);
                        field.SetValue(this, enumValue);
                        if (persistenceKey != null)
                        {
                            PlayerPrefs.SetInt(persistenceKey, index);
                        }
                        InvokeCallback(attr.OnValueChanged, enumValue);
                    }
                });

                ApplyControlTint(dropdown.targetGraphic as Image);
            }

            controls[field.Name] = dropdownObj;
            return dropdownObj;
        }

        protected GameObject CreateSlider(FieldInfo field, SimpleMenuSliderAttribute attr)
        {
            if (field.FieldType != typeof(float) && field.FieldType != typeof(int))
            {
                Debug.LogError($"Field {field.Name} must be of type float or int to use SimpleMenuSlider");
                return null;
            }

            if (Settings == null || Settings.sliderPrefab == null)
            {
                Debug.LogError("SimpleMenuSettings or sliderPrefab is null. Cannot create slider.");
                return null;
            }

            GameObject wrapperObj = new GameObject($"{attr.Label}_Wrapper");
            wrapperObj.transform.SetParent(contentContainer.transform, false);
            RectTransform wrapperRect = wrapperObj.AddComponent<RectTransform>();

            ContentSizeFitter wrapperFitter = wrapperObj.AddComponent<ContentSizeFitter>();
            wrapperFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            VerticalLayoutGroup layoutGroup = wrapperObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = 5f;

            TextMeshProUGUI valueLabelText = null;

            if (Settings.labelPrefab != null && attr.ShowTitleLabel)
            {
                if (attr.ShowValueLabel)
                {
                    float valueLabelWidth = Settings.sliderValueLabelWidth;
                    float spacing = 10f;
                    float labelHeight = Settings.labelPrefab.GetComponent<RectTransform>().sizeDelta.y;

                    GameObject labelContainer = new GameObject("LabelContainer");
                    labelContainer.transform.SetParent(wrapperObj.transform, false);
                    RectTransform labelContainerRect = labelContainer.AddComponent<RectTransform>();
                    labelContainerRect.anchorMin = new Vector2(0, 1);
                    labelContainerRect.anchorMax = new Vector2(1, 1);
                    labelContainerRect.pivot = new Vector2(0.5f, 1);
                    labelContainerRect.anchoredPosition = Vector2.zero;
                    labelContainerRect.sizeDelta = new Vector2(0, labelHeight);

                    GameObject titleLabelObj = Instantiate(Settings.labelPrefab, labelContainer.transform, false);
                    titleLabelObj.name = "TitleLabel";
                    RectTransform titleRect = titleLabelObj.GetComponent<RectTransform>();
                    titleRect.anchorMin = new Vector2(0, 1);
                    titleRect.anchorMax = new Vector2(1, 1);
                    titleRect.pivot = new Vector2(0, 1);
                    titleRect.anchoredPosition = Vector2.zero;
                    titleRect.sizeDelta = new Vector2(0, labelHeight);
                    titleRect.offsetMax = new Vector2(-(valueLabelWidth + spacing), 0);

                    TextMeshProUGUI titleLabelText = titleLabelObj.GetComponentInChildren<TextMeshProUGUI>();
                    if (titleLabelText != null)
                    {
                        titleLabelText.text = attr.Label;
                        titleLabelText.alignment = TextAlignmentOptions.Left;
                        ApplyTextSettings(titleLabelText);
                    }

                    GameObject valueLabelObj = Instantiate(Settings.labelPrefab, labelContainer.transform, false);
                    valueLabelObj.name = "ValueLabel";
                    RectTransform valueRect = valueLabelObj.GetComponent<RectTransform>();
                    valueRect.anchorMin = new Vector2(1, 1);
                    valueRect.anchorMax = new Vector2(1, 1);
                    valueRect.pivot = new Vector2(1, 1);
                    valueRect.anchoredPosition = Vector2.zero;
                    valueRect.sizeDelta = new Vector2(valueLabelWidth, labelHeight);
                    
                    valueLabelText = valueLabelObj.GetComponentInChildren<TextMeshProUGUI>();
                    if (valueLabelText != null)
                    {
                        valueLabelText.text = "0";
                        valueLabelText.alignment = TextAlignmentOptions.Right;
                        ApplyTextSettings(valueLabelText);
                    }
                }
                else
                {
                    GameObject labelObj = Instantiate(Settings.labelPrefab, wrapperObj.transform, false);
                    labelObj.name = "Label";
                    TextMeshProUGUI labelText = labelObj.GetComponentInChildren<TextMeshProUGUI>();
                    if (labelText != null)
                    {
                        labelText.text = attr.Label;
                        ApplyTextSettings(labelText);
                    }
                }
            }

            GameObject sliderObj = Instantiate(Settings.sliderPrefab, wrapperObj.transform, false);
            sliderObj.name = field.Name;

            UnityEngine.UI.Slider slider = sliderObj.GetComponent<UnityEngine.UI.Slider>();
            if (slider != null)
            {
                if (attr.MinValue.HasValue)
                    slider.minValue = attr.MinValue.Value;
                
                if (attr.MaxValue.HasValue)
                    slider.maxValue = attr.MaxValue.Value;
                
                if (attr.WholeNumbers.HasValue)
                    slider.wholeNumbers = attr.WholeNumbers.Value || field.FieldType == typeof(int);
                else if (field.FieldType == typeof(int))
                    slider.wholeNumbers = true;

                string persistenceKey = null;
                if (attr.PersistenceId != null)
                {
                    persistenceKey = string.IsNullOrEmpty(attr.PersistenceId) ? null : attr.PersistenceId;
                }
                else
                {
                    string menuName = GetMenuName().Replace(" ", "");
                    string labelName = attr.Label.Replace(" ", "");
                    persistenceKey = $"{menuName}_{labelName}";
                }

                if (field.FieldType == typeof(float))
                {
                    float defaultValue = (float)field.GetValue(this);
                    float loadedValue = defaultValue;
                    
                    if (persistenceKey != null && PlayerPrefs.HasKey(persistenceKey))
                    {
                        loadedValue = PlayerPrefs.GetFloat(persistenceKey, defaultValue);
                        field.SetValue(this, loadedValue);
                    }
                    
                    slider.value = loadedValue;
                }
                else if (field.FieldType == typeof(int))
                {
                    int defaultValue = (int)field.GetValue(this);
                    int loadedValue = defaultValue;
                    
                    if (persistenceKey != null && PlayerPrefs.HasKey(persistenceKey))
                    {
                        loadedValue = PlayerPrefs.GetInt(persistenceKey, defaultValue);
                        field.SetValue(this, loadedValue);
                    }
                    
                    slider.value = loadedValue;
                }

                slider.onValueChanged.AddListener((value) =>
                {
                    if (field.FieldType == typeof(float))
                    {
                        field.SetValue(this, value);
                        if (persistenceKey != null)
                        {
                            PlayerPrefs.SetFloat(persistenceKey, value);
                        }
                        InvokeCallback(attr.OnValueChanged, value);
                    }
                    else if (field.FieldType == typeof(int))
                    {
                        int intValue = Mathf.RoundToInt(value);
                        field.SetValue(this, intValue);
                        if (persistenceKey != null)
                        {
                            PlayerPrefs.SetInt(persistenceKey, intValue);
                        }
                        InvokeCallback(attr.OnValueChanged, intValue);
                    }
                });

                ApplyControlTint(slider.fillRect?.GetComponent<Image>());
                ApplyControlTint(slider.handleRect?.GetComponent<Image>());

                TextMeshProUGUI sliderValueText = null;
                TextMeshProUGUI[] textComponents = sliderObj.GetComponentsInChildren<TextMeshProUGUI>();
                if (textComponents.Length > 0)
                {
                    sliderValueText = textComponents[0];
                    ApplyTextSettings(sliderValueText);
                }

                bool useWholeNumbers = slider.wholeNumbers;

                if (attr.ShowValueLabel && valueLabelText != null)
                {
                    if (field.FieldType == typeof(float))
                    {
                        string format = useWholeNumbers ? "F0" : "F2";
                        valueLabelText.text = slider.value.ToString(format);
                        slider.onValueChanged.AddListener((value) => valueLabelText.text = value.ToString(format));
                    }
                    else if (field.FieldType == typeof(int))
                    {
                        valueLabelText.text = Mathf.RoundToInt(slider.value).ToString();
                        slider.onValueChanged.AddListener((value) => valueLabelText.text = Mathf.RoundToInt(value).ToString());
                    }
                }
                else if (sliderValueText != null)
                {
                    if (field.FieldType == typeof(float))
                    {
                        string format = useWholeNumbers ? "F0" : "F2";
                        sliderValueText.text = slider.value.ToString(format);
                        slider.onValueChanged.AddListener((value) => sliderValueText.text = value.ToString(format));
                    }
                    else if (field.FieldType == typeof(int))
                    {
                        sliderValueText.text = Mathf.RoundToInt(slider.value).ToString();
                        slider.onValueChanged.AddListener((value) => sliderValueText.text = Mathf.RoundToInt(value).ToString());
                    }
                }
            }

            controls[field.Name] = sliderObj;
            return wrapperObj;
        }

        protected GameObject CreateInputField(FieldInfo field, SimpleMenuInputFieldAttribute attr)
        {
            if (field.FieldType != typeof(string))
            {
                Debug.LogError($"Field {field.Name} must be of type string to use SimpleMenuInputField");
                return null;
            }

            if (Settings == null || Settings.inputFieldPrefab == null)
            {
                Debug.LogError("SimpleMenuSettings or inputFieldPrefab is null. Cannot create input field.");
                return null;
            }

            GameObject wrapperObj = new GameObject($"{attr.Label}_Wrapper");
            wrapperObj.transform.SetParent(contentContainer.transform, false);
            RectTransform wrapperRect = wrapperObj.AddComponent<RectTransform>();

            VerticalLayoutGroup layoutGroup = wrapperObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = 5f;

            if (Settings.labelPrefab != null)
            {
                GameObject labelObj = Instantiate(Settings.labelPrefab, wrapperObj.transform, false);
                labelObj.name = "Label";
                TextMeshProUGUI labelText = labelObj.GetComponentInChildren<TextMeshProUGUI>();
                if (labelText != null)
                {
                    labelText.text = attr.Label;
                    ApplyTextSettings(labelText);
                }
            }

            GameObject inputFieldObj = Instantiate(Settings.inputFieldPrefab, wrapperObj.transform, false);
            inputFieldObj.name = field.Name;

            TMP_InputField inputField = inputFieldObj.GetComponent<TMP_InputField>();
            if (inputField != null)
            {
                inputField.text = (string)field.GetValue(this) ?? "";
                
                if (!string.IsNullOrEmpty(attr.Placeholder))
                {
                    var placeholder = inputField.placeholder as TextMeshProUGUI;
                    if (placeholder != null)
                    {
                        placeholder.text = attr.Placeholder;
                    }
                }

                inputField.onValueChanged.AddListener((value) =>
                {
                    field.SetValue(this, value);
                    InvokeCallback(attr.OnValueChanged, value);
                });

                ApplyTextSettings(inputField.textComponent);
                ApplyControlTint(inputField.targetGraphic as Image);
            }

            controls[field.Name] = inputFieldObj;
            return wrapperObj;
        }

        //protected GameObject CreateMenuLink(FieldInfo field, SimpleMenuLinkAttribute attr)
        //{
        //    if (!typeof(SimpleMenuBase).IsAssignableFrom(field.FieldType))
        //    {
        //        Debug.LogError($"Field {field.Name} must be of type SimpleMenuBase or derived to use SimpleMenuLink");
        //        return null;
        //    }

        //    string label = attr.Label;
        //    if (string.IsNullOrEmpty(label))
        //    {
        //        SimpleMenuAttribute menuAttr = field.FieldType.GetCustomAttribute<SimpleMenuAttribute>();
        //        label = menuAttr?.Label ?? field.FieldType.Name;
        //    }

        //    return CreateButton(label, () => SwitchToMenu(field.FieldType));
        //}

        protected void InvokeCallback(string methodName, object value)
        {
            if (string.IsNullOrEmpty(methodName))
                return;

            MethodInfo method = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0)
                {
                    method.Invoke(this, null);
                }
                else if (parameters.Length == 1)
                {
                    method.Invoke(this, new object[] { value });
                }
            }
        }

        public T GetControl<T>(string fieldName) where T : Component
        {
            if (controls.TryGetValue(fieldName, out GameObject controlObj))
            {
                return controlObj.GetComponent<T>();
            }
            return null;
        }

        public GameObject GetControlGameObject(string fieldName)
        {
            if (controls.TryGetValue(fieldName, out GameObject controlObj))
            {
                return controlObj;
            }
            return null;
        }

        public void SwitchToMenu(Type menuType)
        {
            if (!typeof(SimpleMenuBase).IsAssignableFrom(menuType))
            {
                Debug.LogError($"Type {menuType.Name} does not derive from SimpleMenuBase");
                return;
            }

            string menuId = GetMenuId(menuType);
            SwitchToMenu(menuId);
        }

        public void SwitchToMenu(string menuId)
        {
            SimpleMenuBase mainMenu = GetMainMenu();

            if (mainMenu.subMenus.TryGetValue(menuId, out SimpleMenuBase existingMenu))
            {
                mainMenu.HideAllMenus();
                existingMenu.ShowMenu();
            }
            else
            {
                Type menuType = FindMenuTypeById(menuId);
                if (menuType != null)
                {
                    CreateSubMenu(menuType);
                }
                else
                {
                    Debug.LogError($"Menu with id '{menuId}' not found");
                }
            }
        }

        protected void CreateSubMenu(Type menuType)
        {
            SimpleMenuBase mainMenu = GetMainMenu();
            string menuId = GetMenuId(menuType);

            if (mainMenu.subMenus.ContainsKey(menuId))
            {
                SwitchToMenu(menuId);
                return;
            }

            isCreatingSubMenu = true;
            SimpleMenuBase subMenu = (SimpleMenuBase)gameObject.AddComponent(menuType);
            isCreatingSubMenu = false;

            subMenu.parentMenu = this;
            subMenu.isMainMenu = false;
            subMenu.isInitialized = true;

            subMenu.CreateMenuContainer();
            subMenu.menuContainer.transform.SetParent(transform, false);
            subMenu.BuildMenu();

            string backButtonLabel = "Back";
            subMenu.CreateButton(backButtonLabel, () => ReturnToParent(subMenu));

            mainMenu.subMenus[menuId] = subMenu;
            mainMenu.HideAllMenus();
            subMenu.ShowMenu();
        }

        protected void ReturnToParent(SimpleMenuBase subMenu)
        {
            SimpleMenuBase mainMenu = GetMainMenu();
            mainMenu.HideAllMenus();

            if (subMenu.parentMenu != null)
            {
                subMenu.parentMenu.ShowMenu();
            }
        }

        protected SimpleMenuBase GetMainMenu()
        {
            SimpleMenuBase current = this;
            while (current.parentMenu != null)
            {
                current = current.parentMenu;
            }
            return current;
        }

        protected void HideAllMenus()
        {
            if (menuContainer != null)
            {
                menuContainer.SetActive(false);
            }

            foreach (var subMenu in subMenus.Values)
            {
                if (subMenu.menuContainer != null)
                {
                    subMenu.menuContainer.SetActive(false);
                }
            }
        }

        protected void ShowMenu()
        {
            if (menuContainer != null)
            {
                menuContainer.SetActive(true);
            }
        }

        protected string GetMenuName()
        {
            SimpleMenuAttribute menuAttr = GetType().GetCustomAttribute<SimpleMenuAttribute>();
            if (menuAttr != null && !string.IsNullOrEmpty(menuAttr.Label))
            {
                return menuAttr.Label;
            }

            return GetType().Name;
        }

        protected string GetMenuId(Type menuType)
        {
            MenuIdAttribute menuIdAttr = menuType.GetCustomAttribute<MenuIdAttribute>();
            if (menuIdAttr != null)
            {
                return menuIdAttr.Id;
            }

            return menuType.Name;
        }

        protected string GetMenuLabelFromType(Type menuType)
        {
            SimpleMenuAttribute menuAttr = menuType.GetCustomAttribute<SimpleMenuAttribute>();
            if (menuAttr != null && !string.IsNullOrEmpty(menuAttr.Label))
            {
                return menuAttr.Label;
            }

            return menuType.Name;
        }

        private static bool ShouldScanAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name;
            
            if (name.StartsWith("UnityEngine")) return false;
            if (name.StartsWith("UnityEditor")) return false;
            if (name.StartsWith("Unity.")) return false;
            if (name.StartsWith("System")) return false;
            if (name.StartsWith("mscorlib")) return false;
            if (name.StartsWith("netstandard")) return false;
            if (name.StartsWith("Microsoft.")) return false;
            if (name.StartsWith("Mono.")) return false;
            if (name.StartsWith("nunit.")) return false;
            if (name.StartsWith("ExCSS")) return false;
            
            return true;
        }

        private static void InitializeMenuTypeCache()
        {
            if (s_MenuTypeCacheInitialized)
                return;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            s_MenuTypeCache = new Dictionary<string, Type>();
            
            int totalAssemblies = 0;
            int scannedAssemblies = 0;
            
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                totalAssemblies++;
                
                if (!ShouldScanAssembly(assembly))
                    continue;
                
                scannedAssemblies++;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"SimpleMenu: Failed to get types from assembly {assembly.GetName().Name}: {ex.Message}");
                    continue;
                }

                foreach (Type type in types)
                {
                    try
                    {
                        if (!typeof(SimpleMenuBase).IsAssignableFrom(type) || type.IsAbstract)
                            continue;

                        MenuIdAttribute menuIdAttr = type.GetCustomAttribute<MenuIdAttribute>();
                        if (menuIdAttr != null)
                        {
                            s_MenuTypeCache[menuIdAttr.Id] = type;
                        }
                        
                        s_MenuTypeCache[type.Name] = type;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"SimpleMenu: Error caching type {type.FullName}: {ex.Message}");
                    }
                }
            }

            stopwatch.Stop();
            s_MenuTypeCacheInitialized = true;
            Debug.Log($"SimpleMenu: Cached {s_MenuTypeCache.Count} menu type entries in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F4}s) - Scanned {scannedAssemblies}/{totalAssemblies} assemblies");
        }

        protected Type FindMenuTypeById(string menuId)
        {
            InitializeMenuTypeCache();

            if (s_MenuTypeCache.TryGetValue(menuId, out Type menuType))
            {
                return menuType;
            }

            Debug.LogError($"FindMenuTypeById: Could not find menu type '{menuId}' in cache");
            return null;
        }
    }
}
