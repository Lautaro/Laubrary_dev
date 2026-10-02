using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Notifyer
{
    /// <summary>Live, session-only event log. View settings deliberately remain window-instance state.</summary>
    public class NotifyerLog : ZuiWindow
    {
        private class LogEntry { public DateTime Time; public Type EventType; public NotifyerEventBase EventInstance; public string StringEventId; public string LogMessage; }
        private class TypeInfo { public bool Muted; public bool Pinned; public Color Color; }
        private static readonly List<LogEntry> logEntries = new();
        private static readonly Dictionary<Type, TypeInfo> typeInfos = new();
        private static readonly Dictionary<Type, string> typeNames = new();

        // These were always non-serialized and therefore reset on a domain reload.
        private bool showOnlyPinned, showSettings, narrowMode, leftPanelExpanded = true;
        private bool showTimestamp = true, showEventData = true, showLogMessage = true;
        private float leftPanelWidth = 200f, logEntryScale = 1f, logEntryHeight = 22f;
        private const float MinPanelWidth = 100f, MaxPanelWidth = 400f, MinEntryScale = 1f, MaxEntryScale = 2f, MinEntryHeight = 18f, MaxEntryHeight = 60f;
        private const float TimestampWidth = 90f, TypeWidth = 120f, DataWidth = 200f, MessageWidth = 200f;

        [MenuItem("Laubrary/Notifyer Log")]
        public static void ShowWindow() => GetWindow<NotifyerLog>("Notifyer Log");

        private void OnEnable()
        {
            Notifyer.OnAnyEvent += OnEventNotified;
            Notifyer.OnAnyStringEvent += OnStringEventNotified;
        }

        protected override void OnDisable()
        {
            Notifyer.OnAnyEvent -= OnEventNotified;
            Notifyer.OnAnyStringEvent -= OnStringEventNotified;
            base.OnDisable();
        }

        private void OnEventNotified(NotifyerEventBase evt, string logMessage = "")
        {
            var type = evt.GetType();
            if (!typeInfos.ContainsKey(type))
            {
                typeInfos[type] = new TypeInfo { Color = GetUniqueColor(type) };
                typeNames[type] = type.Name;
            }
            logEntries.Add(new LogEntry { Time = DateTime.Now, EventType = type, EventInstance = evt, LogMessage = logMessage });
            Rebuild();
        }

        private void OnStringEventNotified(string id, string logMessage = "")
        {
            logEntries.Add(new LogEntry { Time = DateTime.Now, StringEventId = id, LogMessage = logMessage });
            Rebuild();
        }

        private static Color GetUniqueColor(Type type)
        {
            int hash = type.FullName.GetHashCode();
            UnityEngine.Random.State oldState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(hash);
            Color color = Color.HSVToRGB(UnityEngine.Random.value, 0.7f, EditorGUIUtility.isProSkin ? 0.85f : 0.6f);
            UnityEngine.Random.state = oldState;
            return color;
        }

        protected override void BuildUI(VisualElement root)
        {
            Z.AttachTool(root, "notifyer-log");
            root.AddToClassList("lau-event-log");
            root.Add(BuildToolbar());
            var content = new VisualElement(); content.AddToClassList("lau-event-log__content"); root.Add(content);
            if (leftPanelExpanded) content.Add(BuildTypePanel());
            content.Add(BuildLogPanel());
        }

        private VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement(); toolbar.AddToClassList("lau-event-log__toolbar");
            toolbar.Add(Z.Button(leftPanelExpanded ? "◀" : "▶", leftPanelExpanded ? "Hide the event-type panel." : "Show the event-type panel.", () => { leftPanelExpanded = !leftPanelExpanded; Rebuild(); }));
            toolbar.Add(Z.Toggle("Show only pinned", showOnlyPinned ? "Show every unmuted event type." : "Show only event types pinned in the left panel.", showOnlyPinned, value => { showOnlyPinned = value; Rebuild(); }));
            toolbar.Add(Z.Toggle("Narrow Mode", narrowMode ? "Show each log event on one row." : "Show each log event across two rows.", narrowMode, value => { narrowMode = value; Rebuild(); }));
            toolbar.Add(Z.Toggle("Settings", showSettings ? "Hide display settings." : "Show display settings.", showSettings, value => { showSettings = value; Rebuild(); }));
            if (!showSettings) return toolbar;

            var settings = new VisualElement(); settings.AddToClassList("lau-event-log__settings");
            settings.Add(Z.Toggle("Timestamp", "Show the time each event was received.", showTimestamp, value => { showTimestamp = value; Rebuild(); }));
            settings.Add(Z.Toggle("Event Data", "Show the event object or string-event identifier.", showEventData, value => { showEventData = value; Rebuild(); }));
            settings.Add(Z.Toggle("Log Message", "Show an optional message sent with each event.", showLogMessage, value => { showLogMessage = value; Rebuild(); }));
            settings.Add(Z.Field("Left Panel Width", "Change the width of the event-type panel.", Z.Slider(leftPanelWidth, MinPanelWidth, MaxPanelWidth, "Change the width of the event-type panel.", value => { leftPanelWidth = value; Rebuild(); }, 150f)));
            settings.Add(Z.Field("Log Entry Size", "Scale the height, text and columns of log entries.", Z.Slider(logEntryScale, MinEntryScale, MaxEntryScale, "Scale the height, text and columns of log entries.", value => { logEntryScale = value; logEntryHeight = Mathf.Lerp(MinEntryHeight, MaxEntryHeight, (value - MinEntryScale) / (MaxEntryScale - MinEntryScale)); Rebuild(); }, 150f)));
            toolbar.Add(settings);
            return toolbar;
        }

        private VisualElement BuildTypePanel()
        {
            var panel = new VisualElement(); panel.AddToClassList("lau-event-log__types"); panel.style.width = leftPanelWidth;
            panel.Add(Z.Text("Event Types", ZuiText.Section, "Left-click a type to mute or unmute it. Right-click to pin or unpin it."));
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("lau-event-log__scroll"); panel.Add(scroll);
            foreach (var pair in typeInfos) scroll.Add(BuildTypeRow(pair.Key, pair.Value));
            return panel;
        }

        private VisualElement BuildTypeRow(Type type, TypeInfo info)
        {
            string tip = info.Muted ? "Muted. Left-click to show this event type; right-click to pin it." : "Visible. Left-click to mute this event type; right-click to pin it.";
            var row = new VisualElement { tooltip = tip }; row.AddToClassList("lau-event-log__type-row"); row.style.height = logEntryHeight;
            if (info.Pinned) { row.AddToClassList("lau-event-log__type-row--pinned"); SetBorderColor(row, info.Color); }
            var swatch = new VisualElement(); swatch.AddToClassList("zui-row__swatch"); swatch.style.backgroundColor = info.Color; row.Add(swatch);
            var label = Z.Text(typeNames[type], info.Muted ? ZuiText.Subtle : ZuiText.Body, tip); label.AddToClassList("zui-row__title"); label.style.fontSize = FontSize; row.Add(label);
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) info.Muted = !info.Muted;
                else if (evt.button == 1) info.Pinned = !info.Pinned;
                else return;
                evt.StopPropagation(); Rebuild();
            });
            return row;
        }

        private VisualElement BuildLogPanel()
        {
            var panel = new VisualElement(); panel.AddToClassList("lau-event-log__log");
            panel.Add(Z.Text("Event Log", ZuiText.Section, "Events received through Notifyer during this editor session."));
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("lau-event-log__scroll"); panel.Add(scroll);
            foreach (var entry in logEntries) if (ShouldShow(entry)) scroll.Add(BuildLogEntry(entry));
            return panel;
        }

        private bool ShouldShow(LogEntry entry) => entry.EventType == null ? !showOnlyPinned : !typeInfos[entry.EventType].Muted && (!showOnlyPinned || typeInfos[entry.EventType].Pinned);

        private VisualElement BuildLogEntry(LogEntry entry)
        {
            var row = new VisualElement { tooltip = "A Notifyer event received during this editor session." }; row.AddToClassList("lau-event-log__entry");
            if (narrowMode) row.AddToClassList("lau-event-log__entry--narrow");
            if (entry.EventType != null && typeInfos[entry.EventType].Pinned) { row.AddToClassList("lau-event-log__entry--pinned"); SetBorderColor(row, typeInfos[entry.EventType].Color); }
            row.style.height = narrowMode ? logEntryHeight * 2f : logEntryHeight;
            if (narrowMode)
            {
                var header = NewLine(); AddTimestamp(header, entry); AddType(header, entry); row.Add(header);
                var detail = NewLine(); AddDetails(detail, entry); row.Add(detail);
            }
            else { AddTimestamp(row, entry); AddType(row, entry); AddDetails(row, entry); }
            return row;
        }

        private VisualElement NewLine() { var line = new VisualElement(); line.AddToClassList("lau-event-log__entry-line"); return line; }
        private void AddTimestamp(VisualElement row, LogEntry entry) { if (showTimestamp) row.Add(LogLabel(entry.Time.ToString("HH:mm:ss.fff"), "lau-event-log__timestamp", TimestampWidth)); }
        private void AddType(VisualElement row, LogEntry entry)
        {
            var label = LogLabel(entry.EventType == null ? "[String Event]" : typeNames[entry.EventType], "lau-event-log__type", TypeWidth);
            label.style.color = entry.EventType == null ? Color.cyan : typeInfos[entry.EventType].Color; row.Add(label);
        }
        private void AddDetails(VisualElement row, LogEntry entry)
        {
            if (showEventData) row.Add(LogLabel(entry.EventType != null ? entry.EventInstance?.ToString() ?? "" : entry.StringEventId, "lau-event-log__data", DataWidth));
            if (showLogMessage && !string.IsNullOrEmpty(entry.LogMessage)) row.Add(LogLabel(entry.LogMessage, "lau-event-log__message", MessageWidth));
        }
        private Label LogLabel(string text, string className, float baseWidth)
        {
            var label = Z.Text(text, ZuiText.Body, text); label.AddToClassList(className); label.style.width = baseWidth * logEntryScale; label.style.fontSize = FontSize; return label;
        }
        private int FontSize => Mathf.RoundToInt(logEntryHeight * 0.45f);
        private static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderLeftColor = color; element.style.borderRightColor = color; element.style.borderTopColor = color; element.style.borderBottomColor = color;
        }
    }
}
