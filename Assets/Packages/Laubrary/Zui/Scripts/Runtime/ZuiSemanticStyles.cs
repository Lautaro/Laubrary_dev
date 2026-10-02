using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ZuiRuntime
{
    public static partial class Zui
    {
        static ZuiRuntimeSkin _resourceSkin;
        static ZuiRuntimeSkin _compatibilitySkin;
        static bool _triedSemanticSkin;
        static ConditionalWeakTable<ZuiRuntimeSkin, SemanticStyleCache> _semanticStyles
            = new ConditionalWeakTable<ZuiRuntimeSkin, SemanticStyleCache>();

        /// <summary>
        /// Optional shared player skin loaded from Resources/ZUIRuntimeSemanticSkin. A missing asset resolves
        /// to null; callers can distinguish this from the compatibility fallback to preserve legacy overrides.
        /// This does not read editor preferences or alter the editor's active skin.
        /// </summary>
        public static ZuiRuntimeSkin DefaultRuntimeSkin
        {
            get
            {
                if (!_triedSemanticSkin)
                {
                    _triedSemanticSkin = true;
                    _resourceSkin = Resources.Load<ZuiRuntimeSkin>("ZUIRuntimeSemanticSkin");
                }
                return _resourceSkin;
            }
        }

        /// <summary>
        /// Resolves a consumer's assigned skin, then the Resources skin, then an in-memory compatibility skin.
        /// The returned asset is presentation data; consumers must not mutate it to store their live view state.
        /// </summary>
        public static ZuiRuntimeSkin ResolveRuntimeSkin(ZuiRuntimeSkin assigned = null)
        {
            if (assigned != null) return assigned;
            if (DefaultRuntimeSkin != null) return DefaultRuntimeSkin;
            return CompatibilitySkin;
        }

        static ZuiRuntimeSkin CompatibilitySkin
        {
            get
            {
                if (_compatibilitySkin == null)
                {
                    _compatibilitySkin = ScriptableObject.CreateInstance<ZuiRuntimeSkin>();
                    _compatibilitySkin.name = "Runtime compatibility skin";
                    _compatibilitySkin.hideFlags = HideFlags.HideAndDontSave;
                }
                return _compatibilitySkin;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSemanticPresentation()
        {
            _resourceSkin = null;
            _triedSemanticSkin = false;
            _semanticStyles = new ConditionalWeakTable<ZuiRuntimeSkin, SemanticStyleCache>();
        }

        sealed class SemanticStyleCache
        {
            public readonly Dictionary<ZuiTextRole, SemanticStyleEntry> labels = new Dictionary<ZuiTextRole, SemanticStyleEntry>();
            public readonly Dictionary<ZuiTextRole, SemanticStyleEntry> buttons = new Dictionary<ZuiTextRole, SemanticStyleEntry>();
        }

        sealed class SemanticStyleEntry
        {
            public GUIStyle template, style;
            public GUISkin guiSkin;
            public Font font, templateFont;
            public int pixels;
            public Color color;
            public bool bold, wrap, overrideColor;
            public TextAnchor alignment;

            public bool Matches(GUIStyle template, ZuiTextPresentation recipe, int pixels)
                => ReferenceEquals(this.template, template) && guiSkin == GUI.skin && this.pixels == pixels
                   && font == recipe.font && templateFont == template.font && color.Equals(recipe.color) && bold == recipe.bold
                   && wrap == recipe.wrap && overrideColor == recipe.overrideColor && alignment == recipe.alignment;
        }

        /// <summary>
        /// Resolves a semantic label style. Cache ownership includes the selected asset and the current GUI skin;
        /// live recipe edits and resolution changes refresh it without reusing a different consumer's skin.
        /// </summary>
        public static GUIStyle TextStyle(ZuiRuntimeSkin skin, ZuiTextRole role)
            => SemanticStyle(skin, role, false);

        /// <summary>Resolves a semantic button style while preserving native GUI state colors when requested.</summary>
        public static GUIStyle ButtonStyle(ZuiRuntimeSkin skin, ZuiTextRole role = ZuiTextRole.Button)
            => SemanticStyle(skin, role, true);

        static GUIStyle SemanticStyle(ZuiRuntimeSkin skin, ZuiTextRole role, bool button)
        {
            skin = ResolveRuntimeSkin(skin);
            var recipe = skin.Text(role) ?? CompatibilitySkin.Text(role);
            int pixels = UIScale.Font(recipe.points);
            var cache = _semanticStyles.GetValue(skin, _ => new SemanticStyleCache());
            var entries = button ? cache.buttons : cache.labels;
            var template = button ? GUI.skin.button : GUI.skin.label;
            if (!entries.TryGetValue(role, out var entry) || !entry.Matches(template, recipe, pixels))
            {
                entry = new SemanticStyleEntry
                {
                    template = template, guiSkin = GUI.skin, font = recipe.font, templateFont = template.font, pixels = pixels,
                    color = recipe.color, bold = recipe.bold, wrap = recipe.wrap,
                    overrideColor = recipe.overrideColor, alignment = recipe.alignment,
                    style = new GUIStyle(template)
                    {
                        fontSize = pixels, fontStyle = recipe.bold ? FontStyle.Bold : FontStyle.Normal,
                        wordWrap = recipe.wrap, alignment = recipe.alignment
                    }
                };
                if (recipe.font != null) entry.style.font = recipe.font;
                if (!button) entry.style.richText = true;
                entries[role] = entry;
            }
            // Unity may reset cached style state colors when switching runtime GUI skins. Reassert on every
            // resolution so the device captions, title and focus text retain their authored contrast.
            if (recipe.overrideColor)
            {
                entry.style.normal.textColor = recipe.color;
                entry.style.hover.textColor = recipe.color;
                entry.style.active.textColor = recipe.color;
                entry.style.focused.textColor = recipe.color;
                entry.style.onNormal.textColor = recipe.color;
                entry.style.onHover.textColor = recipe.color;
                entry.style.onActive.textColor = recipe.color;
                entry.style.onFocused.textColor = recipe.color;
            }
            return entry.style;
        }

        /// <summary>Draws a semantic selected-choice control; the consumer owns the selection and its meaning.</summary>
        public static bool Segment(Rect rect, GUIContent content, bool selected, ZuiRuntimeSkin skin = null)
        {
            skin = ResolveRuntimeSkin(skin);
            bool hovered = rect.Contains(Event.current.mousePosition);
            FillRect(rect, selected ? skin.segmentSelected : hovered ? skin.segmentHovered : skin.segment);
            if (selected)
            {
                float height = UIScale.S(skin.segmentAccentHeight);
                FillRect(new Rect(rect.x, rect.yMax - height, rect.width, height), skin.segmentAccent);
            }
            GUI.Label(rect, content.text, TextStyle(skin, selected ? ZuiTextRole.SelectedSegment : ZuiTextRole.Segment));
            return GUI.Button(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
        }
    }
}
