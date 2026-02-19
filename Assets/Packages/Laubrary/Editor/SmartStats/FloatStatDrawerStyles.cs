using UnityEngine;

namespace Lautaro.Stats.Editor
{
    /// <summary>
    /// Centralized style configuration for FloatStat property drawer.
    /// Modify these values to customize the appearance of stats in the Inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "FloatStatDrawerStyles", menuName = "SmartStats/Drawer Styles")]
    public class FloatStatDrawerStyles : ScriptableObject
    {
        [Header("Stat Name & Final Value")]
        [Tooltip("Color for the stat name and final value")]
        public Color statNameColor = new Color(0.8f, 0.95f, 1f);
        
        [Tooltip("Font size for stat name and final value")]
        public int statNameFontSize = 14;
        
        [Tooltip("Bold style for stat name")]
        public bool statNameBold = true;
        
        [Tooltip("Italic style for stat name")]
        public bool statNameItalic = false;

        [Header("Modifier Calculations")]
        [Tooltip("Color for modifier calculation lines (duller)")]
        public Color modifierCalculationColor = new Color(0.6f, 0.6f, 0.6f);
        
        [Tooltip("Font size for modifier calculations (smaller)")]
        public int modifierCalculationFontSize = 11;
        
        [Tooltip("Bold style for modifier calculations")]
        public bool modifierCalculationBold = false;
        
        [Tooltip("Italic style for modifier calculations")]
        public bool modifierCalculationItalic = false;

        [Header("Modifier Value Display")]
        [Tooltip("Color for the numeric modifier value")]
        public Color modifierValueColor = new Color(1f, 0.85f, 0.4f);
        
        [Tooltip("Bold style for numeric modifier values")]
        public bool modifierValueBold = true;

        [Header("Base Value Display")]
        [Tooltip("Text shown before base value in calculations")]
        public string baseValuePrefix = "[";
        
        [Tooltip("Text shown after base value in calculations")]
        public string baseValueSuffix = "] Base Value";

        private static FloatStatDrawerStyles _default;
        
        public static FloatStatDrawerStyles Default
        {
            get
            {
                if (_default == null)
                {
                    _default = CreateInstance<FloatStatDrawerStyles>();
                }
                return _default;
            }
        }
    }
}
