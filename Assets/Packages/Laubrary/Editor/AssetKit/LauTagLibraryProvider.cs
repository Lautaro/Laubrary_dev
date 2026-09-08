using UnityEditor;
using UnityEngine;

namespace Laubrary.AssetKit.Editor
{
    /// One project-wide LauTagLibrary, auto-created on first use — same "singleton asset, found or made
    /// lazily" shape as Zounds' own ZoundsEditorPresets, but under a normal findable project path
    /// (Assets/LauTag/) rather than a Resources/ runtime-loadable one, since this is editor-only.
    public static class LauTagLibraryProvider
    {
        const string Path = "Assets/LauTag/LauTagLibrary.asset";

        static LauTagLibrary _cached;

        public static LauTagLibrary Get()
        {
            if (_cached != null) return _cached;

            _cached = AssetDatabase.LoadAssetAtPath<LauTagLibrary>(Path);
            if (_cached != null) return _cached;

            var existing = AssetDatabase.FindAssets("t:LauTagLibrary");
            if (existing.Length > 0)
            {
                _cached = AssetDatabase.LoadAssetAtPath<LauTagLibrary>(AssetDatabase.GUIDToAssetPath(existing[0]));
                if (_cached != null) return _cached;
            }

            AssetFolders.EnsureFolder("Assets/LauTag");
            _cached = ScriptableObject.CreateInstance<LauTagLibrary>();
            AssetDatabase.CreateAsset(_cached, Path);
            // T-0282 — flush THIS asset (the one it just created), never the project. See AssetLibrary.Create (T-0276).
            AssetDatabase.SaveAssetIfDirty(_cached);
            return _cached;
        }
    }
}
