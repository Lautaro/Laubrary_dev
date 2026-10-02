using System;
using UnityEngine;

namespace ZuiRuntime
{
    /// <summary>Presentation roles shared by runtime HUDs, menus, device legends and capture prompts.</summary>
    public enum ZuiTextRole
    {
        Title, Glyph, Caption, Device, Tooltip, Category, Body, Description, Overflow,
        Menu, Action, Button, Segment, SelectedSegment, Prompt, PromptHint
    }

    /// <summary>A text recipe in reference points; scaling and GUIStyle creation remain runtime toolkit work.</summary>
    [Serializable]
    public sealed class ZuiTextPresentation
    {
        [Tooltip("Text size at the runtime toolkit's reference resolution.")]
        [Min(1f)] public float points = 14f;
        [Tooltip("Optional font; leave empty to inherit the current GUI skin's font.")]
        public Font font;
        [Tooltip("Text tint used when Override color is enabled.")]
        public Color color = Color.white;
        [Tooltip("Apply this text tint instead of inheriting the GUI control's native state colors.")]
        public bool overrideColor = true;
        [Tooltip("Use the font's bold face.")]
        public bool bold;
        [Tooltip("Allow text to continue on another line.")]
        public bool wrap;
        [Tooltip("Placement of text within its allocated rectangle.")]
        public TextAnchor alignment = TextAnchor.UpperLeft;

        public ZuiTextPresentation() { }

        public ZuiTextPresentation(float points, Color color, TextAnchor alignment,
            bool bold = false, bool wrap = false, bool overrideColor = true)
        {
            this.points = points;
            this.color = color;
            this.alignment = alignment;
            this.bold = bold;
            this.wrap = wrap;
            this.overrideColor = overrideColor;
        }
    }

    /// <summary>
    /// Authored static layout of a controls overlay, in reference points unless marked as a viewport fraction.
    /// Screen fitting, text measurement, scrolling and data-dependent structure belong to the consumer.
    /// </summary>
    [Serializable]
    public sealed class ZuiOverlayLayout
    {
        [Tooltip("Largest panel size at the reference resolution.")]
        public Vector2 panelSize = new Vector2(1180f, 760f);
        [Tooltip("Largest fraction of the viewport occupied by the panel.")]
        public Vector2 viewportFraction = new Vector2(0.94f, 0.92f);
        [Tooltip("Inset between the panel edge and its content.")]
        [Min(0f)] public float panelPadding = 18f;
        [Tooltip("Height reserved for the title and navigation tabs.")]
        [Min(0f)] public float headerHeight = 46f;
        [Tooltip("Fraction of the header allocated to its title.")]
        [Range(0f, 1f)] public float titleWidthFraction = 0.46f;
        [Tooltip("Height permanently reserved for hover hints.")]
        [Min(0f)] public float tooltipHeight = 26f;
        [Tooltip("Gap below the header.")]
        [Min(0f)] public float bodyGap = 8f;
        [Tooltip("Combined spacing reserved between the header, body and hover hints.")]
        [Min(0f)] public float bodyReservedSpacing = 14f;
        [Tooltip("Height of header tab controls.")]
        [Min(0f)] public float tabHeight = 34f;
        [Tooltip("Width of each page tab.")]
        [Min(0f)] public float tabWidth = 112f;
        [Tooltip("Width of the close action.")]
        [Min(0f)] public float closeWidth = 72f;
        [Tooltip("Space between adjacent page or scheme controls.")]
        [Min(0f)] public float controlGap = 6f;
        [Tooltip("Fraction of the legend body used for action descriptions.")]
        [Range(0f, 1f)] public float detailsWidthFraction = 0.31f;
        [Tooltip("Minimum and maximum width of the action description column.")]
        public Vector2 detailsWidthRange = new Vector2(250f, 370f);
        [Tooltip("Gap between the device graphic and action descriptions.")]
        [Min(0f)] public float legendGap = 18f;
        [Tooltip("Vertical offset of the device graphic below its heading.")]
        [Min(0f)] public float graphicTop = 30f;
        [Tooltip("Height reserved for the device heading.")]
        [Min(0f)] public float deviceHeadingHeight = 26f;
        [Tooltip("Space between successive content rows.")]
        [Min(0f)] public float stackGap = 5f;
        [Tooltip("Height of the binding scheme switcher.")]
        [Min(0f)] public float switchHeight = 36f;
        [Tooltip("Gap between the scheme switcher and binding rows.")]
        [Min(0f)] public float listGap = 10f;
        [Tooltip("Space before the binding reset actions.")]
        [Min(0f)] public float resetGap = 8f;
        [Tooltip("Fraction of the overlay width occupied by the capture prompt.")]
        [Range(0f, 1f)] public float promptWidthFraction = 0.7f;
        [Tooltip("Height of the centered capture prompt.")]
        [Min(0f)] public float promptHeight = 140f;
        [Tooltip("Inset of prompt text from its edges.")]
        [Min(0f)] public float promptPadding = 18f;
        [Tooltip("Height reserved for the capture instruction.")]
        [Min(0f)] public float promptTextHeight = 58f;
        [Tooltip("Distance from the prompt bottom to the cancellation hint.")]
        [Min(0f)] public float promptHintBottom = 44f;
        [Tooltip("Height reserved for the cancellation hint.")]
        [Min(0f)] public float promptHintHeight = 26f;
    }

    /// <summary>
    /// Runtime-safe semantic skin. Assign a separate asset to each consumer, or place a shared asset at
    /// Resources/ZUIRuntimeSemanticSkin. Resolving or drawing a skin never changes another consumer's selection.
    /// Defaults preserve the controls overlay's existing presentation and do not adopt any editor-only skin.
    /// </summary>
    [CreateAssetMenu(menuName = "ZUI/Runtime Skin", fileName = "ZUIRuntimeSkin")]
    public sealed class ZuiRuntimeSkin : ScriptableObject
    {
        [Tooltip("Tint behind the overlay panel.")]
        public Color backdrop = new Color(0f, 0f, 0f, 0.72f);
        [Tooltip("Fill behind the device graphic and binding list.")]
        public Color panel = new Color(0.055f, 0.065f, 0.085f, 0.98f);
        [Tooltip("Fill of an unselected segment.")]
        public Color segment = new Color(1f, 1f, 1f, 0.065f);
        [Tooltip("Fill of an unselected segment under the pointer.")]
        public Color segmentHovered = new Color(1f, 1f, 1f, 0.13f);
        [Tooltip("Fill of the selected segment.")]
        public Color segmentSelected = new Color(0.25f, 0.56f, 0.78f, 0.92f);
        [Tooltip("Accent marking the selected segment.")]
        public Color segmentAccent = new Color(0.68f, 0.93f, 1f, 1f);
        [Tooltip("Thickness of the selected segment's accent, in reference points.")]
        [Min(0f)] public float segmentAccentHeight = 3f;
        [Tooltip("Fill behind the focused keyboard or gamepad menu item.")]
        public Color menuFocus = new Color(1f, 1f, 1f, 0.14f);
        [Tooltip("Extra height around menu text, in reference points.")]
        [Min(0f)] public float menuVerticalPadding = 8f;
        [Tooltip("Extra focus-highlight width on each side, in reference points.")]
        [Min(0f)] public float menuFocusOutset = 4f;
        [Tooltip("Tint dimming the overlay during binding capture.")]
        public Color promptBackdrop = new Color(0f, 0f, 0f, 0.82f);
        [Tooltip("Fill of the binding capture prompt.")]
        public Color promptPanel = new Color(0.12f, 0.15f, 0.20f, 1f);

        public ZuiOverlayLayout overlay = new ZuiOverlayLayout();
        public ZuiTextPresentation title = new ZuiTextPresentation(25f, new Color(0.65f, 0.90f, 1f), TextAnchor.MiddleLeft, true);
        public ZuiTextPresentation glyph = new ZuiTextPresentation(14f, Color.white, TextAnchor.MiddleCenter, true);
        public ZuiTextPresentation caption = new ZuiTextPresentation(10f, Color.white, TextAnchor.MiddleCenter, false, true);
        public ZuiTextPresentation device = new ZuiTextPresentation(14f, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft, true);
        public ZuiTextPresentation tooltip = new ZuiTextPresentation(12f, new Color(1f, 1f, 1f, 0.62f), TextAnchor.MiddleLeft);
        public ZuiTextPresentation category = new ZuiTextPresentation(13f, new Color(0.65f, 0.88f, 1f), TextAnchor.UpperLeft, true, true);
        public ZuiTextPresentation body = new ZuiTextPresentation(14f, Color.white, TextAnchor.UpperLeft, true, true);
        public ZuiTextPresentation description = new ZuiTextPresentation(12f, new Color(1f, 1f, 1f, 0.68f), TextAnchor.UpperLeft, false, true);
        public ZuiTextPresentation overflow = new ZuiTextPresentation(12f, Color.white, TextAnchor.UpperLeft, false, true);
        public ZuiTextPresentation menu = new ZuiTextPresentation(15f, Color.white, TextAnchor.MiddleCenter, overrideColor: false);
        public ZuiTextPresentation action = new ZuiTextPresentation(14f, Color.white, TextAnchor.MiddleCenter, overrideColor: false);
        public ZuiTextPresentation button = new ZuiTextPresentation(14f, Color.white, TextAnchor.MiddleCenter, overrideColor: false);
        public ZuiTextPresentation segmentText = new ZuiTextPresentation(14f, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleCenter);
        public ZuiTextPresentation selectedSegmentText = new ZuiTextPresentation(14f, Color.white, TextAnchor.MiddleCenter, true);
        public ZuiTextPresentation prompt = new ZuiTextPresentation(20f, Color.white, TextAnchor.MiddleCenter, true, true);
        public ZuiTextPresentation promptHint = new ZuiTextPresentation(13f, new Color(1f, 1f, 1f, 0.58f), TextAnchor.MiddleCenter);

        /// <summary>Returns the authored recipe for a semantic text role.</summary>
        public ZuiTextPresentation Text(ZuiTextRole role)
        {
            switch (role)
            {
                case ZuiTextRole.Title: return title;
                case ZuiTextRole.Glyph: return glyph;
                case ZuiTextRole.Caption: return caption;
                case ZuiTextRole.Device: return device;
                case ZuiTextRole.Tooltip: return tooltip;
                case ZuiTextRole.Category: return category;
                case ZuiTextRole.Body: return body;
                case ZuiTextRole.Description: return description;
                case ZuiTextRole.Overflow: return overflow;
                case ZuiTextRole.Menu: return menu;
                case ZuiTextRole.Action: return action;
                case ZuiTextRole.Button: return button;
                case ZuiTextRole.Segment: return segmentText;
                case ZuiTextRole.SelectedSegment: return selectedSegmentText;
                case ZuiTextRole.Prompt: return prompt;
                case ZuiTextRole.PromptHint: return promptHint;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }
    }
}
