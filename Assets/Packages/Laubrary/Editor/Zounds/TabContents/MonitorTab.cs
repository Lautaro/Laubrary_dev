using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    // Live view of what the GAME has triggered, newest first. One list: a Zound moves to the top the
    // moment it starts playing, and while it plays its row also shows state, progress and a Stop
    // button. Editor previews (Browser, editor windows, the Routing tool) never appear here — see
    // ZoundsRecentHistory. Each item is a compact info line plus the SAME Browser row
    // (BrowserTab.DrawListRow) drawn for a real Zound list, so mute/solo, the name button, the name
    // field, VPC sliders, route/duplicate/delete and tags are the identical controls the Browser uses,
    // not a re-implementation.
    public class MonitorTab : TabContent {

        public override string name { get; set; } = "Monitor";
        public override string tooltip { get; set; } =
            "What the game triggered in Play mode, newest first. Playing Zounds show their progress and a Stop button.";

        private const float RECENT_FRAME_W = 56f;
        private const float RECENT_CALLER_MAX_W = 320f;

        // The live part of the info line is always laid out at these widths, playing or not, so a
        // sound starting or stopping never shifts the rest of the row.
        private const float PLAYING_STATE_W = 60f;
        private const float PLAYING_COUNT_W = 32f;
        private const float PLAYING_BAR_W = 140f;
        private const float PLAYING_STOP_W = 50f;

        private Vector2 recentScroll;
        private string filterText = string.Empty;

        private double lastRepaintRequest;
        private const double RepaintInterval = 0.1; // ~10 Hz throttle

        private readonly Dictionary<Zound, ZoundToken> liveTokenByZound = new Dictionary<Zound, ZoundToken>();
        private readonly Dictionary<Zound, int> liveInstanceCounts = new Dictionary<Zound, int>();
        private readonly HashSet<string> recentSeenKeys = new HashSet<string>();
        private readonly List<Zound> layoutCandidatesBuffer = new List<Zound>();

        public MonitorTab() {
            ZoundsRecentHistory.onRecentHistoryChanged += OnRecentHistoryChanged;
        }

        // Called from ZoundsWindow.OnDisable alongside BrowserTab's own Dispose — see the comment
        // on BrowserTab.Dispose for why this can't rely on a finalizer instead.
        public void Dispose() {
            ZoundsRecentHistory.onRecentHistoryChanged -= OnRecentHistoryChanged;
        }

        private void OnRecentHistoryChanged() {
            ZoundsWindow.RepaintWindow();
        }

        public override void Update() {
            bool anythingPlaying = CountLiveTokens() > 0;
            if (anythingPlaying || Application.isPlaying) {
                double now = EditorApplication.timeSinceStartup;
                if (now - lastRepaintRequest >= RepaintInterval) {
                    lastRepaintRequest = now;
                    ZoundsWindow.Instance?.Repaint();
                }
            }
        }

        internal static int CountLiveTokens() {
            int count = 0;
            foreach (var kvp in ZoundEngine.CullingGroups) {
                foreach (var token in kvp.Value) {
                    if (!token.isChildZound && IsLiveState(token.state)) count++;
                }
            }
            return count;
        }

        private static bool IsLiveState(ZoundToken.State state) {
            return state == ZoundToken.State.Playing
                || state == ZoundToken.State.Paused
                || state == ZoundToken.State.FadeToKill;
        }

        // One representative (oldest) live token per Zound, plus how many instances are live.
        private void CollectLiveTokens() => CollectLiveTokens(liveTokenByZound, liveInstanceCounts);

        /// <summary>Fills <paramref name="liveTokenByZound"/> with one representative (oldest) live token per Zound and
        /// <paramref name="liveInstanceCounts"/> with how many are live. Shared with the UI Toolkit twin (T-0470).</summary>
        internal static void CollectLiveTokens(Dictionary<Zound, ZoundToken> liveTokenByZound, Dictionary<Zound, int> liveInstanceCounts) {
            liveTokenByZound.Clear();
            liveInstanceCounts.Clear();
            foreach (var kvp in ZoundEngine.CullingGroups) {
                var zound = kvp.Key;
                if (zound == null) continue;
                foreach (var token in kvp.Value) {
                    if (token.isChildZound) continue;
                    if (!IsLiveState(token.state)) continue;
                    if (liveInstanceCounts.TryGetValue(zound, out int n)) { liveInstanceCounts[zound] = n + 1; continue; }
                    liveInstanceCounts[zound] = 1;
                    liveTokenByZound[zound] = token;
                }
            }
        }

        public override void OnGUI(SerializedObject serializedObject, Rect contentRect) {
            using (ZUI.Box()) {
                DrawToolbar(contentRect);
                ZUI.RowSpace();
                DrawRecentSection(contentRect);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TOOLBAR — one fixed-height, non-wrapping row. Status text goes last.
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawToolbar(Rect contentRect) {
            const float tbH = 30f;

            GUILayout.BeginHorizontal();
            GUILayout.Space(5f);

            bool paused = ZoundsRecentHistory.paused;
            var pauseContent = new GUIContent(
                paused ? "Resume" : "Pause",
                paused ? "Recording is paused — new game triggers are not added. Click to resume." : "Pause recording new game triggers (playback itself is unaffected).");
            if (ZUI.Button(pauseContent, ZUI.Style.Flat, ZUICornerMask.All, GUILayout.Height(tbH), GUILayout.Width(70f))) {
                ZoundsRecentHistory.paused = !paused;
            }

            ZUI.HorizontalSpace("H Btns Medium");

            if (ZUI.Button(new GUIContent("Clear", "Clear the list. Does not stop anything currently playing."), ZUI.Style.Flat, ZUICornerMask.All, GUILayout.Height(tbH))) {
                ZoundsRecentHistory.ClearRecent();
            }

            ZUI.HorizontalSpace("H Btns Medium");

            var props = ZoundsWindowProperties.Instance;
            bool captureCallers = props.captureCallers;
            bool newCaptureCallers = ZUI.Toggle(captureCallers,
                new GUIContent("Callers", "Record which game code triggered each Zound (a short stack walk per trigger). Turn off if triggers are very frequent and the cost matters."),
                ZUI.Style.RichToggle, GUILayout.Height(tbH), GUILayout.Width(70f));
            if (newCaptureCallers != captureCallers) {
                Undo.RecordObject(props, "toggle Zounds Monitor caller capture");
                props.captureCallers = newCaptureCallers;
                EditorUtility.SetDirty(props);
            }

            ZUI.HorizontalSpace("H Btns Medium");

            GUI.SetNextControlName("MonitorFilterField");
            filterText = EditorGUILayout.TextField(filterText, GUILayout.Width(160f), GUILayout.Height(tbH));
            if (Event.current.type == EventType.Repaint) {
                var lastRect = GUILayoutUtility.GetLastRect();
                EditorGUI.LabelField(lastRect, new GUIContent("", "Filter the list by zound name."));
                if (string.IsNullOrEmpty(filterText) && GUI.GetNameOfFocusedControl() != "MonitorFilterField") {
                    var ghostStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.gray } };
                    ghostStyle.padding.left = 4;
                    GUI.Label(lastRect, "Filter by name...", ghostStyle);
                }
            }

            GUILayout.FlexibleSpace();

            int playingCount = CountLiveTokens();
            int recordedCount = ZoundsRecentHistory.GetEntries().Count;
            var statusStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            GUILayout.Label(new GUIContent($"{playingCount} playing · {recordedCount} recorded"), statusStyle, GUILayout.Width(170f), GUILayout.Height(tbH));

            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RECENT — the one list
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawRecentSection(Rect contentRect) {
            var allEntries = ZoundsRecentHistory.GetEntries();
            var filtered = FilteredEntries(allEntries, filterText, recentSeenKeys);

            GUILayout.BeginHorizontal();
            ZUI.Label("Recent", GUILayout.ExpandWidth(false));
            GUILayout.Label($"({filtered.Count})", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            if (allEntries.Count == 0) {
                ZUI.InfoBox("Nothing recorded yet. Trigger a Zound from the game in Play mode; every game trigger appears here newest first.");
                return;
            }
            if (filtered.Count == 0) {
                ZUI.InfoBox("No entries match the filter.");
                return;
            }

            CollectLiveTokens();

            var browserTab = BrowserTab.Instance;
            BrowserTab.ZoundListRowLayout layout = default;
            if (browserTab != null) {
                layoutCandidatesBuffer.Clear();
                var seen = new HashSet<Zound>();
                foreach (var entry in filtered) {
                    if (!string.IsNullOrEmpty(entry.name) && ZoundDictionary.TryGetZoundByName(entry.name, out var z) && seen.Add(z)) {
                        layoutCandidatesBuffer.Add(z);
                    }
                }
                layout = browserTab.PrepareListRowLayout(layoutCandidatesBuffer);
            }

            using (ZUI.ScrollView(ref recentScroll, GUILayout.ExpandHeight(true))) {
                for (int i = 0; i < filtered.Count; i++) {
                    var entry = filtered[i];
                    Zound resolvedZound = null;
                    bool resolved = !string.IsNullOrEmpty(entry.name) && ZoundDictionary.TryGetZoundByName(entry.name, out resolvedZound);
                    ZoundToken liveToken = null;
                    if (resolved) liveTokenByZound.TryGetValue(resolvedZound, out liveToken);

                    using (ZUI.Box()) {
                        DrawInfoLine(entry, liveToken);
                        if (resolved && browserTab != null) {
                            browserTab.DrawListRow(resolvedZound, ref layout);
                        }
                        else {
                            DrawMissingName(entry.name);
                        }
                    }
                    if (i < filtered.Count - 1) ZUI.RowSpace(0.5f);
                }
            }

            browserTab?.ApplyDeferredRowMutations();
        }

        // Frame and caller on the left; the live part on the right, drawn at fixed widths whether
        // or not the Zound is playing, so rows never reflow when a sound starts or ends.
        private void DrawInfoLine(ZoundsRecentHistory.Entry entry, ZoundToken liveToken) {
            GUILayout.BeginHorizontal(GUILayout.Height(18f));

            GUILayout.Label("#" + entry.frame, EditorStyles.miniLabel, GUILayout.Width(RECENT_FRAME_W));

            string callerText = string.IsNullOrEmpty(entry.caller) ? "—" : entry.caller;
            GUILayout.Label(new GUIContent(callerText, callerText), EditorStyles.miniLabel, GUILayout.MaxWidth(RECENT_CALLER_MAX_W));

            GUILayout.FlexibleSpace();

            if (liveToken != null) {
                GUILayout.Label(liveToken.state.ToString(), EditorStyles.miniLabel, GUILayout.Width(PLAYING_STATE_W));

                string countText = liveInstanceCounts.TryGetValue(liveToken.zound, out int instances) && instances > 1 ? "×" + instances : string.Empty;
                GUILayout.Label(new GUIContent(countText, countText.Length > 0 ? instances + " instances of this Zound are playing." : string.Empty), EditorStyles.miniLabel, GUILayout.Width(PLAYING_COUNT_W));

                float duration = liveToken.duration;
                float time = liveToken.time;
                bool loops = float.IsInfinity(duration);   // a Looper (T-0473)
                float progress01 = !loops && duration > 0f ? Mathf.Clamp01(time / duration) : 0f;
                var barRect = GUILayoutUtility.GetRect(PLAYING_BAR_W, 16f, GUILayout.Width(PLAYING_BAR_W));
                EditorGUI.ProgressBar(barRect, progress01, loops ? $"{time:0.0}s · looping" : $"{time:0.0} / {duration:0.0}s");

                if (ZUI.Button(new GUIContent("Stop", "Stop this playing Zound now."), ZUI.Style.Flat, ZUICornerMask.All, GUILayout.Width(PLAYING_STOP_W), GUILayout.Height(18f))) {
                    var projectSettings = ZoundsProject.Instance.projectSettings;
                    liveToken.Kill(projectSettings.cullFadeDuration);
                }
            }
            else {
                // Same footprint as the live controls, empty.
                GUILayoutUtility.GetRect(PLAYING_STATE_W + PLAYING_COUNT_W + PLAYING_BAR_W + PLAYING_STOP_W, 18f,
                    GUILayout.Width(PLAYING_STATE_W + PLAYING_COUNT_W + PLAYING_BAR_W + PLAYING_STOP_W));
            }

            GUILayout.EndHorizontal();
        }

        private void DrawMissingName(string name) {
            var style = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.4f, 0.4f, 1f) }, fontStyle = FontStyle.Italic };
            string text = (string.IsNullOrEmpty(name) ? "(unknown)" : name) + " — no longer in the library";
            GUILayout.Label(text, style);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>The list the Monitor shows: each Zound once, at the position of its newest trigger (entries are newest
        /// first), narrowed by the name filter. Shared with the UI Toolkit twin (T-0470).</summary>
        internal static List<ZoundsRecentHistory.Entry> FilteredEntries(IReadOnlyList<ZoundsRecentHistory.Entry> allEntries, string filterText, HashSet<string> seenKeys) {
            var filtered = new List<ZoundsRecentHistory.Entry>(allEntries.Count);
            seenKeys.Clear();
            foreach (var entry in allEntries) {
                if (!MatchesFilter(entry.name, filterText)) continue;
                if (!seenKeys.Add(ZoundDictionary.ZoundNameToKey(entry.name ?? string.Empty))) continue;
                filtered.Add(entry);
            }
            return filtered;
        }

        /// <summary>The name filter: case-insensitive "contains"; an empty filter matches everything.</summary>
        internal static bool MatchesFilter(string name, string filterText) {
            if (string.IsNullOrEmpty(filterText)) return true;
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf(filterText, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

}
