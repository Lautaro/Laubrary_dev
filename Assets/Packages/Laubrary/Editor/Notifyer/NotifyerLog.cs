using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Laubrary.Notifyer
{
public class NotifyerLog : EditorWindow
    {
        private class LogEntry
        {
            public DateTime Time;
            public Type EventType; // null for string event
            public NotifyerEventBase EventInstance;
            public string StringEventId;
            public string LogMessage;
        }

        private class TypeInfo
        {
            public bool Muted;
            public bool Pinned;
            public Color Color;
        }

        private static readonly List<LogEntry> logEntries = new();
        private static readonly Dictionary<Type, TypeInfo> typeInfos = new();
        private static readonly Dictionary<Type, string> typeNames = new();

        private Vector2 logScroll;
        private Vector2 typeScroll;

        // Settings
        private bool showOnlyPinned = false;
        private bool showTimestamp = true;
        private bool showEventData = true;
        private bool showLogMessage = true;
        private bool showSettings = false;
        private float leftPanelWidth = 200f;
        private float logEntryScale = 1f;
        private float logEntryHeight = 22f;
        private const float minPanelWidth = 100f;
        private const float maxPanelWidth = 400f;
        private const float minLogEntryScale = 1f;
        private const float maxLogEntryScale = 2f;
        private const float minLogEntryHeight = 18f;
        private const float maxLogEntryHeight = 60f;

        // Base column widths
        private const float baseTimestampWidth = 90f;
        private const float baseTypeWidth = 120f;
        private const float baseDataWidth = 200f;
        private const float baseMsgWidth = 200f;

        // New: Narrow mode and left panel expand/collapse
        private bool narrowMode = false;
        private bool leftPanelExpanded = true;

        [MenuItem("Laubrary/Notifyer Log")]
        public static void ShowWindow()
        {
            GetWindow<NotifyerLog>("Notifyer Log");
        }

        private void OnEnable()
        {
            Notifyer.OnAnyEvent += OnEventNotified;
            Notifyer.OnAnyStringEvent += OnStringEventNotified;
        }

        private void OnDisable()
        {
            Notifyer.OnAnyEvent -= OnEventNotified;
            Notifyer.OnAnyStringEvent -= OnStringEventNotified;
        }

        private void OnEventNotified(NotifyerEventBase evt, string logMessage = "")
        {
            Type type = evt.GetType();
            if (!typeInfos.ContainsKey(type))
            {
                typeInfos[type] = new TypeInfo
                {
                    Muted = false,
                    Pinned = false,
                    Color = GetUniqueColor(type)
                };
                typeNames[type] = type.Name;
            }
            logEntries.Add(new LogEntry
            {
                Time = DateTime.Now,
                EventType = type,
                EventInstance = evt,
                LogMessage = logMessage
            });
            Repaint();
        }

        private void OnStringEventNotified(string id, string logMessage = "")
        {
            logEntries.Add(new LogEntry
            {
                Time = DateTime.Now,
                EventType = null,
                StringEventId = id,
                LogMessage = logMessage
            });
            Repaint();
        }

        private static Color GetUniqueColor(Type type)
        {
            int hash = type.FullName.GetHashCode();
            UnityEngine.Random.State oldState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(hash);
            float h = UnityEngine.Random.value;
            float s = 0.7f;
            float v = EditorGUIUtility.isProSkin ? 0.85f : 0.6f; // Brighter for dark skin, still visible for light skin
            Color color = Color.HSVToRGB(h, s, v);
            UnityEngine.Random.state = oldState;
            return color;
        }

        private float GetSettingsBarHeight()
        {
            float height = EditorGUIUtility.singleLineHeight + 4; // Main bar
            if (showSettings)
                height += EditorGUIUtility.singleLineHeight + 4; // Settings row
            return height;
        }

        private void OnGUI()
        {
            DrawSettingsBar();

            float settingsBarHeight = GetSettingsBarHeight();

            // Draw left panel background and content (absolute position)
            if (leftPanelExpanded)
            {
                DrawTypePanelWithBackground(settingsBarHeight);
            }

            // Now draw the log panel, offset horizontally by leftPanelWidth if left panel is expanded
            GUILayout.BeginArea(new Rect(leftPanelExpanded ? leftPanelWidth : 0, settingsBarHeight, position.width - (leftPanelExpanded ? leftPanelWidth : 0), position.height - settingsBarHeight));
            DrawLogPanel();
            GUILayout.EndArea();
        }

        private void DrawSettingsBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 1. Left Panel Toggle
            if (GUILayout.Button(leftPanelExpanded ? "\u25C0" : "\u25B6", EditorStyles.toolbarButton, GUILayout.Width(22)))
            {
                leftPanelExpanded = !leftPanelExpanded;
            }

            // 2. Show Only Pinned
            showOnlyPinned = GUILayout.Toggle(showOnlyPinned, "Show only pinned", EditorStyles.toolbarButton, GUILayout.Width(120));

            // 3. Narrow Mode
            narrowMode = GUILayout.Toggle(narrowMode, "Narrow Mode", EditorStyles.toolbarButton, GUILayout.Width(100));

            // 4. Settings Toggle
            showSettings = GUILayout.Toggle(showSettings, "Settings", EditorStyles.toolbarButton, GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();

            // Show the rest of the settings on a new row if toggled
            if (showSettings)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

                showTimestamp = GUILayout.Toggle(showTimestamp, "Timestamp", EditorStyles.toolbarButton, GUILayout.Width(80));
                showEventData = GUILayout.Toggle(showEventData, "Event Data", EditorStyles.toolbarButton, GUILayout.Width(90));
                showLogMessage = GUILayout.Toggle(showLogMessage, "Log Message", EditorStyles.toolbarButton, GUILayout.Width(100));
                GUILayout.Space(10);

                GUILayout.Label("Left Panel Width", GUILayout.Width(110));
                leftPanelWidth = EditorGUILayout.Slider(leftPanelWidth, minPanelWidth, maxPanelWidth, GUILayout.Width(150));
                GUILayout.Space(10);

                GUILayout.Label("Log Entry Size", GUILayout.Width(110));
                logEntryScale = EditorGUILayout.Slider(logEntryScale, minLogEntryScale, maxLogEntryScale, GUILayout.Width(150));
                logEntryHeight = Mathf.Lerp(minLogEntryHeight, maxLogEntryHeight, (logEntryScale - minLogEntryScale) / (maxLogEntryScale - minLogEntryScale));

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTypePanelWithBackground(float yOffset)
        {
            float panelHeight = Mathf.Max(position.height - yOffset, 100);
            Rect panelRect = new Rect(0, yOffset, leftPanelWidth, panelHeight);

            Color bg = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.90f, 0.90f, 0.90f);
            EditorGUI.DrawRect(panelRect, bg);

            GUILayout.BeginArea(panelRect);
            DrawTypePanel();
            GUILayout.EndArea();
        }

        private void DrawTypePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(leftPanelWidth));
            // Slightly larger font for header, always visible
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                wordWrap = false,
                clipping = TextClipping.Clip,
                margin = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(2, 2, 2, 2)
            };
            EditorGUILayout.LabelField("Event Types", headerStyle, GUILayout.Width(leftPanelWidth));

            typeScroll = EditorGUILayout.BeginScrollView(typeScroll);

            foreach (var kvp in typeInfos)
            {
                var type = kvp.Key;
                var info = kvp.Value;
                var rect = EditorGUILayout.GetControlRect(false, logEntryHeight, GUILayout.Width(leftPanelWidth));

                // Draw color box
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2, 8, logEntryHeight - 4), info.Color);

                // Draw border if pinned
                if (info.Pinned)
                {
                    Handles.color = info.Color;
                    Handles.DrawSolidRectangleWithOutline(
                        new Rect(rect.x, rect.y, rect.width, rect.height),
                        Color.clear, info.Color);
                }

                // Draw type name, truncate if too long
                string displayName = typeNames[type];
                var labelRect = new Rect(rect.x + 12, rect.y, rect.width - 12, rect.height);
                var style = new GUIStyle(info.Muted ? EditorStyles.miniLabel : EditorStyles.label)
                {
                    fontSize = Mathf.RoundToInt(logEntryHeight * 0.45f),
                    clipping = TextClipping.Clip,
                    wordWrap = false
                };
                EditorGUI.LabelField(labelRect, displayName, style);

                // Click to mute/unmute or pin/unpin
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 0)
                    {
                        info.Muted = !info.Muted;
                        Event.current.Use();
                    }
                    else if (Event.current.button == 1)
                    {
                        info.Pinned = !info.Pinned;
                        Event.current.Use();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawLogPanel()
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField("Event Log", EditorStyles.boldLabel);

            logScroll = EditorGUILayout.BeginScrollView(logScroll);

            foreach (var entry in logEntries)
            {
                // Filter by pinned/muted
                if (entry.EventType != null)
                {
                    var info = typeInfos[entry.EventType];
                    if (info.Muted) continue;
                    if (showOnlyPinned && !info.Pinned) continue;
                }
                else
                {
                    // String event: always show unless only pinned
                    if (showOnlyPinned) continue;
                }

                float fontSize = Mathf.RoundToInt(logEntryHeight * 0.45f);

                if (!narrowMode)
                {
                    // Start horizontal for the log entry
                    EditorGUILayout.BeginHorizontal(GUILayout.Height(logEntryHeight));

                    // Timestamp
                    if (showTimestamp)
                    {
                        var style = new GUIStyle(EditorStyles.label) { fontSize = (int)fontSize };
                        EditorGUILayout.LabelField(entry.Time.ToString("HH:mm:ss.fff"), style, GUILayout.Width(baseTimestampWidth * logEntryScale));
                    }

                    if (entry.EventType != null)
                    {
                        // Draw color
                        var color = typeInfos[entry.EventType].Color;
                        var prevColor = GUI.color;
                        GUI.color = color;
                        var style = new GUIStyle(EditorStyles.label) { fontSize = (int)fontSize };
                        EditorGUILayout.LabelField(typeNames[entry.EventType], style, GUILayout.Width(baseTypeWidth * logEntryScale));
                        GUI.color = prevColor;

                        // Data
                        if (showEventData)
                        {
                            var dataStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.EventInstance?.ToString() ?? "", dataStyle, GUILayout.Width(baseDataWidth * logEntryScale));
                        }

                        // LOG MESSAGE
                        if (showLogMessage && !string.IsNullOrEmpty(entry.LogMessage))
                        {
                            var msgStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.LogMessage, msgStyle, GUILayout.Width(baseMsgWidth * logEntryScale));
                        }
                    }
                    else
                    {
                        // String event: special formatting
                        var prevColor = GUI.color;
                        GUI.color = Color.cyan;
                        var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = (int)fontSize };
                        EditorGUILayout.LabelField("[String Event]", style, GUILayout.Width(baseTypeWidth * logEntryScale));
                        GUI.color = prevColor;

                        // STRING ID
                        if (showEventData)
                        {
                            var idStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.StringEventId, idStyle, GUILayout.Width(baseDataWidth * logEntryScale));
                        }

                        // LOG MESSAGE
                        if (showLogMessage && !string.IsNullOrEmpty(entry.LogMessage))
                        {
                            var msgStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.LogMessage, msgStyle, GUILayout.Width(baseMsgWidth * logEntryScale));
                        }
                    }

                    EditorGUILayout.EndHorizontal();

                    // Draw border if pinned (after row, using last rect)
                    if (entry.EventType != null && typeInfos[entry.EventType].Pinned)
                    {
                        var lastRect = GUILayoutUtility.GetLastRect();
                        Handles.color = typeInfos[entry.EventType].Color;
                        Handles.DrawSolidRectangleWithOutline(lastRect, Color.clear, typeInfos[entry.EventType].Color);
                    }
                }
                else
                {
                    // Narrow mode: two rows per entry, using only EditorGUILayout
                    EditorGUILayout.BeginVertical(GUILayout.Height(logEntryHeight * 2f));

                    EditorGUILayout.BeginHorizontal();
                    if (showTimestamp)
                    {
                        var style = new GUIStyle(EditorStyles.label) { fontSize = (int)fontSize };
                        EditorGUILayout.LabelField(entry.Time.ToString("HH:mm:ss.fff"), style, GUILayout.Width(baseTimestampWidth * logEntryScale));
                    }
                    if (entry.EventType != null)
                    {
                        var color = typeInfos[entry.EventType].Color;
                        var prevColor = GUI.color;
                        GUI.color = color;
                        var style = new GUIStyle(EditorStyles.label) { fontSize = (int)fontSize };
                        EditorGUILayout.LabelField(typeNames[entry.EventType], style, GUILayout.Width(baseTypeWidth * logEntryScale));
                        GUI.color = prevColor;
                    }
                    else
                    {
                        var prevColor = GUI.color;
                        GUI.color = Color.cyan;
                        var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = (int)fontSize };
                        EditorGUILayout.LabelField("[String Event]", style, GUILayout.Width(baseTypeWidth * logEntryScale));
                        GUI.color = prevColor;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    if (entry.EventType != null)
                    {
                        if (showEventData)
                        {
                            var dataStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.EventInstance?.ToString() ?? "", dataStyle, GUILayout.Width(baseDataWidth * logEntryScale));
                        }
                        if (showLogMessage && !string.IsNullOrEmpty(entry.LogMessage))
                        {
                            var msgStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.LogMessage, msgStyle, GUILayout.Width(baseMsgWidth * logEntryScale));
                        }
                    }
                    else
                    {
                        if (showEventData)
                        {
                            var idStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.StringEventId, idStyle, GUILayout.Width(baseDataWidth * logEntryScale));
                        }
                        if (showLogMessage && !string.IsNullOrEmpty(entry.LogMessage))
                        {
                            var msgStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = (int)fontSize };
                            EditorGUILayout.LabelField(entry.LogMessage, msgStyle, GUILayout.Width(baseMsgWidth * logEntryScale));
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();

                    // Draw border if pinned (after row, using last rect)
                    if (entry.EventType != null && typeInfos[entry.EventType].Pinned)
                    {
                        var lastRect = GUILayoutUtility.GetLastRect();
                        Handles.color = typeInfos[entry.EventType].Color;
                        Handles.DrawSolidRectangleWithOutline(lastRect, Color.clear, typeInfos[entry.EventType].Color);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
    }
}