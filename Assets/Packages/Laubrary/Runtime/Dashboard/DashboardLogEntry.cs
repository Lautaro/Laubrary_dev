using UnityEditor;
using UnityEngine;

namespace Laubrary.Dashboards
{
    public static partial class Dashboard
    {
        public class DashboardLogEntry
        {
            public string id;
            public string text;
            public int textSize;
            public Color textColor;
            public MonoBehaviour owner;

            public DashboardLogEntry(string id, string text, int textSize = 0, Color color = default, MonoBehaviour owner = null)
            {
                this.id = id;
                this.textSize = textSize;
                this.textColor = color;
                this.owner = owner;
#if UNITY_EDITOR
                if (color == default)
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
#endif
            }
        }
    }
}