using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

namespace Laubrary.SimpleUI
{
    /// <summary>
    /// A combined display/input control that switches between a read-only text view
    /// and an editable input field. Call BeginEdit() or EndEdit() to switch modes,
    /// or wire any event (e.g. a Button click or PointerClick) to toggle.
    /// </summary>
    [AddComponentMenu("Laubrary/SimpleUI/Simple UI Editable Field")]
    public class SimpleUIEditableField : MonoBehaviour, IPointerClickHandler
    {
        [Header("References")]
        [Tooltip("Displays the current value in read mode.")]
        public TextMeshProUGUI displayText;

        [Tooltip("Accepts user input in edit mode.")]
        public TMP_InputField inputField;

        [Header("Settings")]
        [Tooltip("If true, clicking the display text begins editing automatically.")]
        public bool clickToEdit = true;

        [Tooltip("If true, pressing Enter or losing focus ends editing automatically.")]
        public bool submitOnEndEdit = true;

        [Header("Events")]
        public UnityEvent onBeginEdit = new UnityEvent();
        public UnityEvent<string> onValueSubmitted = new UnityEvent<string>();

        private bool _isEditing;

        /// <summary>The current string value regardless of edit mode.</summary>
        public string Value
        {
            get => displayText != null ? displayText.text : string.Empty;
            set => SetDisplayValue(value);
        }

        public bool IsEditing => _isEditing;

        private void Awake()
        {
            if (inputField != null && submitOnEndEdit)
            {
                inputField.onEndEdit.AddListener(OnInputEndEdit);
            }

            SetEditing(false);
        }

        private void OnDestroy()
        {
            if (inputField != null)
                inputField.onEndEdit.RemoveListener(OnInputEndEdit);
        }

        /// <summary>Toggles between display and edit mode.</summary>
        public void Toggle() => SetEditing(!_isEditing);

        /// <summary>Switches to edit mode and focuses the input field.</summary>
        public void BeginEdit()  => SetEditing(true);

        /// <summary>Submits the current input value and switches to display mode.</summary>
        public void EndEdit() => SetEditing(false);

        /// <summary>
        /// Sets the displayed value without triggering submit events.
        /// Called by the binding system to push data → UI.
        /// </summary>
        public void SetDisplayValue(string value)
        {
            if (displayText != null)
                displayText.text = value;

            if (inputField != null)
                inputField.SetTextWithoutNotify(value);
        }

        public void SetEditing(bool editing)
        {
            _isEditing = editing;

            if (displayText != null)
                displayText.gameObject.SetActive(!editing);

            if (inputField != null)
            {
                inputField.gameObject.SetActive(editing);

                if (editing)
                {
                    inputField.SetTextWithoutNotify(displayText != null ? displayText.text : string.Empty);
                    inputField.Select();
                    inputField.ActivateInputField();
                    onBeginEdit.Invoke();
                }
            }
        }

        // IPointerClickHandler — only fires when clickToEdit is enabled and in display mode
        public void OnPointerClick(PointerEventData eventData)
        {
            if (clickToEdit && !_isEditing)
                BeginEdit();
        }

        private void OnInputEndEdit(string value)
        {
            if (displayText != null)
                displayText.text = value;

            SetEditing(false);
            onValueSubmitted.Invoke(value);
        }
    }
}
