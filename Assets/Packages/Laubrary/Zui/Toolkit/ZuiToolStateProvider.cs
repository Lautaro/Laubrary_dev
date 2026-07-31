// Finds (or creates) the one committed ZuiToolStateStore and hands tools their entry.
//
// Saving is DEBOUNCED rather than immediate. A fold, a colour drag and a pad drag all mark the store
// dirty, and writing an asset per mouse-move would put an AssetDatabase import in the middle of a drag —
// visible as a stutter, and a diff per frame. Instead the store is marked dirty immediately (so the data
// is correct the instant it is read back) and FLUSHED to disk on an idle tick, plus on the two events
// that would otherwise lose it: an assembly reload and quitting the editor.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zui
{
    [InitializeOnLoad]
    public static class ZuiToolStateProvider
    {
        // In the HOST project, not in the package: Laubrary ships no authored assets, and a team's editor
        // arrangement belongs to the project that authored the content, not to the tool package.
        const string Folder = "Assets/Laubrary";
        const string Path = Folder + "/ZuiToolState.asset";

        const double FlushDelay = 0.5;

        static ZuiToolStateStore _store;
        static double _flushAt;
        static bool _dirty;

        static ZuiToolStateProvider()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
        }

        /// <summary>The store, loaded once per domain. Returns null only when the asset cannot be created
        /// (a read-only project), and every caller treats null as "no persistence" rather than failing —
        /// losing an arrangement must never cost the user their actual work.</summary>
        public static ZuiToolStateStore Store
        {
            get
            {
                if (_store != null) return _store;

                _store = AssetDatabase.LoadAssetAtPath<ZuiToolStateStore>(Path);
                if (_store != null) return _store;

                if (!AssetDatabase.IsValidFolder(Folder))
                {
                    if (!Directory.Exists(Folder)) AssetDatabase.CreateFolder("Assets", "Laubrary");
                    if (!AssetDatabase.IsValidFolder(Folder)) return null;
                }

                var fresh = ScriptableObject.CreateInstance<ZuiToolStateStore>();
                AssetDatabase.CreateAsset(fresh, Path);
                AssetDatabase.SaveAssets();
                _store = fresh;
                return _store;
            }
        }

        /// <summary>A tool's arrangement, by its own stable slug. Null when there is no store to write to.</summary>
        public static ZuiToolState For(string toolId)
        {
            var s = Store;
            return s != null ? s.For(toolId) : null;
        }

        /// <summary>Say the state changed. Cheap and safe to call on every drag frame — it only stamps a
        /// deadline; the write happens once the user stops moving.</summary>
        public static void MarkDirty()
        {
            if (_store == null) return;
            EditorUtility.SetDirty(_store);
            _dirty = true;
            _flushAt = EditorApplication.timeSinceStartup + FlushDelay;
        }

        static void Tick()
        {
            if (!_dirty || EditorApplication.timeSinceStartup < _flushAt) return;
            Flush();
        }

        static void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            if (_store == null) return;
            EditorUtility.SetDirty(_store);
            // Only this asset: a background save nobody asked for has no business flushing whatever else
            // the user happens to have dirty.
            AssetDatabase.SaveAssetIfDirty(_store);
        }
    }
}
