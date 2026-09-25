using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    // Editor-session-only history of what the GAME triggered, backing both the Zounds Browser's
    // Recent mode and the Monitor tab. Only Play-mode triggers that did not come from editor code
    // are recorded: a preview from the Browser, an editor window or the Routing tool never appears
    // here, in or out of Play mode. Backed by SessionState so it survives a domain reload (e.g.
    // exiting Play Mode) but not an editor restart. Never touches ZoundsProject.json or its
    // serializer.
    [InitializeOnLoad]
    internal static class ZoundsRecentHistory {

        private const string SessionKey = "Zounds.Browser.RecentHistory";
        private const int MaxEntries = 500;

        public static event Action onRecentHistoryChanged;

        // Monitor-tab view state: not persisted, not part of the history data itself. While paused,
        // new triggers are not recorded.
        public static bool paused = false;

        [Serializable]
        internal class Entry {
            public string name;
            public double editorTime;
            public int frame;
            public bool wasPlaying;
            public string caller;
        }

        [Serializable]
        private class HistoryData {
            public List<Entry> entries = new List<Entry>();
        }

        private static List<Entry> s_cache;

        // Origin is captured when the token is CREATED (synchronous with the game's PlayZound call),
        // because play-start can fire later (delayed or deferred tokens) from the engine's update tick,
        // where the stack holds only engine frames. A token with no origin record is not a gameplay
        // trigger and is never recorded. Keyed weakly so a token that never starts does not pin anything.
        private class Origin {
            public string caller;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ZoundToken, Origin> s_originByToken =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ZoundToken, Origin>();

        // Persisting 500 entries as JSON on every trigger would be a per-trigger cost in combat;
        // instead the in-memory cache is authoritative and SessionState is written at most ~1/s,
        // plus before a domain reload and on Play transitions.
        private static bool s_saveDirty;
        private static double s_lastSaveTime;
        private const double SaveInterval = 1.0;

        static ZoundsRecentHistory() {
            ZoundEngine.onNewTokenCreated += OnNewTokenCreated;
            ZoundEngine.onZoundStartedPlaying += OnZoundStartedPlaying;
            EditorApplication.update += FlushIfDue;
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.playModeStateChanged += _ => Flush();
        }

        private static void OnNewTokenCreated(ZoundToken token) {
            if (paused || token == null) return;
            if (!Application.isPlaying) return;
            // Children are started by the engine on behalf of their parent; the parent is the trigger.
            if (token.isChildZound) return;

            bool captureCallers = ZoundsWindowProperties.Instance != null && ZoundsWindowProperties.Instance.captureCallers;
            if (!WalkOrigin(captureCallers, out string caller)) return;
            s_originByToken.AddOrUpdate(token, new Origin { caller = caller });
        }

        private static void FlushIfDue() {
            if (!s_saveDirty) return;
            if (EditorApplication.timeSinceStartup - s_lastSaveTime < SaveInterval) return;
            Flush();
        }

        private static void Flush() {
            if (!s_saveDirty || s_cache == null) return;
            s_saveDirty = false;
            s_lastSaveTime = EditorApplication.timeSinceStartup;
            SessionState.SetString(SessionKey, JsonUtility.ToJson(new HistoryData { entries = s_cache }));
        }

        /// <summary>Recently triggered Zound names, newest first, deduped case-insensitively (first occurrence wins).</summary>
        public static IReadOnlyList<string> GetRecentZoundNames() {
            var entries = LoadEntries();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>(entries.Count);
            foreach (var entry in entries) {
                if (string.IsNullOrEmpty(entry.name)) continue;
                if (seen.Add(entry.name)) names.Add(entry.name);
            }
            return names;
        }

        /// <summary>All recorded trigger entries, newest first, repeats kept.</summary>
        public static IReadOnlyList<Entry> GetEntries() {
            return LoadEntries();
        }

        public static void ClearRecent() {
            s_cache = new List<Entry>();
            s_saveDirty = false;
            SessionState.EraseString(SessionKey);
            onRecentHistoryChanged?.Invoke();
        }

        private static void OnZoundStartedPlaying(Zound zound, ZoundToken token) {
            if (paused) return;
            if (zound == null || string.IsNullOrEmpty(zound.name)) return;
            if (token == null || !s_originByToken.TryGetValue(token, out var origin)) return;
            s_originByToken.Remove(token);

            var entries = LoadEntries();
            entries.Insert(0, new Entry {
                name = zound.name,
                editorTime = EditorApplication.timeSinceStartup,
                frame = Time.frameCount,
                wasPlaying = true,
                caller = origin.caller ?? string.Empty
            });
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

            SaveEntries(entries);
            onRecentHistoryChanged?.Invoke();
        }

        // One walk of the call stack decides two things: whether the trigger came from editor code (any
        // frame in an editor assembly, which is UnityEditor itself or an assembly whose name carries
        // "Editor", such as ZoundsEditor, ZUI.Editor and Assembly-CSharp-Editor), and, when asked, the
        // first game frame outside Laubrary.Zounds/System/UnityEngine as the caller. Returns false for
        // an editor-originated trigger. Cheap: no file/line info requested.
        private static bool WalkOrigin(bool captureCaller, out string caller) {
            caller = string.Empty;
            try {
                var trace = new StackTrace(false);
                int frameCount = trace.FrameCount;
                for (int i = 0; i < frameCount; i++) {
                    var frame = trace.GetFrame(i);
                    var method = frame?.GetMethod();
                    var declaringType = method?.DeclaringType;
                    if (declaringType == null) continue;
                    // This class itself sits in the editor assembly; its own frames say nothing about the origin.
                    if (declaringType == typeof(ZoundsRecentHistory)) continue;

                    string ns = declaringType.Namespace ?? string.Empty;
                    if (ns == "UnityEditor" || ns.StartsWith("UnityEditor.")) return false;
                    string assemblyName = declaringType.Assembly.GetName().Name;
                    if (assemblyName.IndexOf("Editor", StringComparison.Ordinal) >= 0) return false;

                    if (!captureCaller || caller.Length > 0) continue;
                    if (ns == "Laubrary.Zounds") continue;
                    if (ns == "System" || ns.StartsWith("System.")) continue;
                    if (ns == "UnityEngine" || ns.StartsWith("UnityEngine.")) continue;
                    caller = declaringType.Name + "." + method.Name;
                }
            }
            catch {
                // Never let origin capture break playback.
            }
            return true;
        }

        private static List<Entry> LoadEntries() {
            if (s_cache != null) return s_cache;

            string json = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) {
                s_cache = new List<Entry>();
            }
            else {
                var data = JsonUtility.FromJson<HistoryData>(json);
                s_cache = data?.entries ?? new List<Entry>();
            }
            return s_cache;
        }

        private static void SaveEntries(List<Entry> entries) {
            s_cache = entries;
            s_saveDirty = true;
        }
    }

}
