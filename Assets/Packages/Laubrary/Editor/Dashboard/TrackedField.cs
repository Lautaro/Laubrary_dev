using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace Laubrary.Dashboards
{
    internal class TrackedField
    {
        public string logTitle;
        public MonoBehaviour owner { get; private set; }
        public FieldInfo fieldInfo { get; private set; }
        public int textSize;
        public Color textColor;
        public object currentValue => fieldInfo.GetValue(owner);

        public TrackedField(MonoBehaviour component, FieldInfo fieldInfo, string logTitle = "", int textSize = 0, Color textColor = default)
        {
            owner = component;
            this.fieldInfo = fieldInfo;
            this.logTitle = logTitle;
            this.textSize = textSize;
            this.textColor = textColor;
            if (textColor == default)
            {
                if (EditorGUIUtility.isProSkin)
                {
                    // Dark theme (Pro Skin)
                    this.textColor = Color.white; // Light color for dark backgrounds
                }
                else
                {
                    // Light theme (Personal Skin)
                    this.textColor = Color.black; // Dark color for light backgrounds
                }
            }
        }
    }
}