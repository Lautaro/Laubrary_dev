using System.IO;
using UnityEditor;
using Laubrary.Chunks.Editor;

namespace Laubrary.Launimator.Editor
{
    /// Registers "Edit" for a LauminaryAnimationChunkAdapter asset picked into a Chunks ChunkSpec — jumps straight into
    /// the Laumination Builder on the wrapped animation. The adapter only stores a LauminaryVersion (a runtime-safe
    /// reference with no back-link to its owning Lauminary), so this resolves the owning Lauminary by walking up from the
    /// version asset's folder (Lauminaries/&lt;Name&gt;_&lt;id&gt;/draft|v{n}/, per LauminaryRepo's layout) — editor-only lookup,
    /// kept out of the runtime adapter.
    [InitializeOnLoad]
    static class LauminaryAnimationChunkAdapterEditorLink
    {
        static LauminaryAnimationChunkAdapterEditorLink()
        {
            ChunkAnimationEditors.Register<LauminaryAnimationChunkAdapter>(a =>
            {
                var lauminary = FindOwningLauminary(a.version);
                if (lauminary != null) LauminationBuilderWindow.OpenForEdit(lauminary, a.animationName);
            });
        }

        static Lauminary FindOwningLauminary(LauminaryVersion version)
        {
            if (version == null) return null;
            string versionAssetPath = AssetDatabase.GetAssetPath(version);
            if (string.IsNullOrEmpty(versionAssetPath)) return null;

            string versionFolder = Path.GetDirectoryName(versionAssetPath)?.Replace('\\', '/');
            string lauminaryFolder = string.IsNullOrEmpty(versionFolder) ? null : Path.GetDirectoryName(versionFolder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(lauminaryFolder)) return null;

            foreach (var guid in AssetDatabase.FindAssets("t:Lauminary", new[] { lauminaryFolder }))
            {
                var lauminary = AssetDatabase.LoadAssetAtPath<Lauminary>(AssetDatabase.GUIDToAssetPath(guid));
                if (lauminary != null) return lauminary;
            }
            return null;
        }
    }
}
