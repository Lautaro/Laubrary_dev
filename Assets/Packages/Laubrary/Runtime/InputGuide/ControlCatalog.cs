using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.InputGuide
{
    public enum InputSchemeKind
    {
        Unknown,
        KeyboardMouse,
        Gamepad
    }

    [Serializable]
    public sealed class ControlActionDescription
    {
        [Tooltip("Action whose live bindings this description explains.")]
        public InputActionReference action;

        [Tooltip("Short player-facing action name shown beside the bound control. The Input Action name is used when empty.")]
        public string playerName;

        [Tooltip("What this action does in player language. Keep implementation details out of this text.")]
        [TextArea(1, 3)] public string description;

        [Tooltip("Short heading used to group related controls in lists, such as Flight, Tools, or Menus.")]
        public string category;

        [Tooltip("Show this action in the generated binding legend and rebinding list.")]
        public bool showInGuide = true;

        public Guid ActionId => action != null && action.action != null ? action.action.id : Guid.Empty;

        public string DisplayName => !string.IsNullOrWhiteSpace(playerName)
            ? playerName
            : action != null && action.action != null ? action.action.name : "Unassigned action";
    }

    /// <summary>
    /// Adds player-facing meaning to a normal Input System action asset. Bindings remain owned by the
    /// Input System; this asset only says how those actions should be explained and grouped.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/Input Guide Catalog", fileName = "Control Catalog")]
    public sealed class ControlCatalog : ScriptableObject
    {
        [Tooltip("Input System actions used by the game. InputGuide clones this asset at runtime so rebinding never mutates the authored asset.")]
        [SerializeField] InputActionAsset actions;

        [Tooltip("Binding group used by gamepad bindings in the Input Action asset.")]
        [SerializeField] string gamepadBindingGroup = "Gamepad";

        [Tooltip("Binding group used by keyboard and mouse bindings in the Input Action asset.")]
        [SerializeField] string keyboardMouseBindingGroup = "Keyboard&Mouse";

        [Tooltip("PlayerPrefs key for binding overrides. Leave empty to derive one from this asset's name.")]
        [SerializeField] string persistenceKey;

        [Tooltip("Player-facing descriptions for actions that should appear in the control guide.")]
        [SerializeField] List<ControlActionDescription> actionDescriptions = new List<ControlActionDescription>();

        public InputActionAsset Actions => actions;
        public string GamepadBindingGroup => gamepadBindingGroup;
        public string KeyboardMouseBindingGroup => keyboardMouseBindingGroup;
        public IReadOnlyList<ControlActionDescription> ActionDescriptions => actionDescriptions;
        public string PersistenceKey => !string.IsNullOrWhiteSpace(persistenceKey)
            ? persistenceKey
            : "Laubrary.InputGuide." + name + ".Bindings";

        public string BindingGroup(InputSchemeKind scheme)
        {
            return scheme == InputSchemeKind.Gamepad ? gamepadBindingGroup : keyboardMouseBindingGroup;
        }

        public ControlActionDescription FindDescription(Guid actionId)
        {
            for (int i = 0; i < actionDescriptions.Count; i++)
            {
                var item = actionDescriptions[i];
                if (item != null && item.ActionId == actionId) return item;
            }
            return null;
        }
    }
}
