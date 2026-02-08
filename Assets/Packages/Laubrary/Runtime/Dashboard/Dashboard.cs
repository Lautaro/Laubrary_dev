using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Dashboards
{
    public static partial class Dashboard
    {

        public static List<DashboardLogEntry> logs = new List<DashboardLogEntry>();

        public static void DashboardLog(this MonoBehaviour monoBehaviour, string id, string text, int textSize = 0, Color textColor = default)
        {

            Log(text, textSize, textColor, owner: monoBehaviour, id: id);
        }

        public static void DashboardLog(this MonoBehaviour monoBehaviour, string id, string text, int textSize = 0, DashboardColor textColor = default)
        {
            Log(text, textSize, DashboardColorMapper.MapEnumToColor(textColor), owner: monoBehaviour, id: id);
        }

        //public static void DashboardLog(this MonoBehaviour monoBehaviour, string text, int textSize = 12, Color textColor = default)
        //{
        //    Log(text, textSize, textColor, owner: monoBehaviour, id: "");
        //}

        public static void DashboardLog(this MonoBehaviour monoBehaviour, string text, int textSize = 12, DashboardColor textColor = default)
        {
            Log(text, textSize, DashboardColorMapper.MapEnumToColor(textColor), owner: monoBehaviour, id: "");
        }



        //public static void QuickLog(string id, string text, int textSize = 10, Color textColor = default)
        //{
        //    Log(text, textSize, textColor, id: id);
        //}
      
        /// <param name="id">Unique id. If no unique it will be overwritten</param>
        /// <param name="text">The text displayed</param>
        public static void QuickLog(string id, string text, int textSize = 10, DashboardColor textColor = default)
        {
            Log(text, textSize, DashboardColorMapper.MapEnumToColor(textColor), id: id);
        }
        public static void QuickLog( string text, int textSize = 10, DashboardColor textColor = default)
        {
            Log(text, textSize, DashboardColorMapper.MapEnumToColor(textColor), id: text);
        }
        public static void QuickLog(string id, string text, DashboardColor textColor = default)
        {
            Log(text, 10,DashboardColorMapper.MapEnumToColor(textColor), id: id);
        }

        public static void QuickLog(string text, DashboardColor textColor = default)
        {
            Log(text, 10, DashboardColorMapper.MapEnumToColor(textColor), id: text);
        }

        public static void QuickLog(string text)
        {
            Log(text, 10, DashboardColorMapper.MapEnumToColor(default), id: text);
        }



        static private void Log(string text, int textSize = 0, Color textColor = default, MonoBehaviour owner = null, string id = "")
        {
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                var logWithId = logs.FirstOrDefault(log => log.id == id && log.owner == owner);
                if (logWithId != null)
                {
                    logWithId.text = text;
                    logWithId.textSize = textSize;
                    logWithId.textColor = textColor;
                    if (textColor == default)
                    {
                        if (EditorGUIUtility.isProSkin)
                        {
                            // Dark theme (Pro Skin)
                            logWithId.textColor = Color.white; // Light color for dark backgrounds
                        }
                        else
                        {
                            // Light theme (Personal Skin)
                            logWithId.textColor = Color.black; // Dark color for light backgrounds
                        }
                    }

                    logWithId.owner = owner;
                    logWithId.id = id;
                }
                else
                {
                    var log = new DashboardLogEntry(id, text, textSize, textColor, owner);
                    logs.Add(log);
                }
            }
#endif
        }
    }
}