using System.IO;
using UnityEditor;
using Laubrary.Chunks.Editor;

namespace Laubrary.Launimator.Editor
{
    /// Registers "Edit" for a ReelAnimationChunkAdapter asset picked into a Chunks ChunkSpec — jumps straight into
    /// the Animation Builder on the wrapped animation. The adapter only stores a ReelVersion (a runtime-safe
    /// reference with no back-link to its owning Reel), so this resolves the owning Reel by walking up from the
    /// version asset's folder (Reels/&lt;Name&gt;_&lt;id&gt;/draft|v{n}/, per ReelRepo's layout) — editor-only lookup,
    /// kept out of the runtime adapter.
    [InitializeOnLoad]
    static class ReelAnimationChunkAdapterEditorLink
    {
        static ReelAnimationChunkAdapterEditorLink()
        {
            ChunkAnimationEditors.Register<ReelAnimationChunkAdapter>(a =>
            {
                var reel = FindOwningReel(a.version);
                if (reel != null) AnimationBuilderWindow.OpenForEdit(reel, a.animationName);
            });
        }

        static Reel FindOwningReel(ReelVersion version)
        {
            if (version == null) return null;
            string versionAssetPath = AssetDatabase.GetAssetPath(version);
            if (string.IsNullOrEmpty(versionAssetPath)) return null;

            string versionFolder = Path.GetDirectoryName(versionAssetPath)?.Replace('\\', '/');
            string reelFolder = string.IsNullOrEmpty(versionFolder) ? null : Path.GetDirectoryName(versionFolder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(reelFolder)) return null;

            foreach (var guid in AssetDatabase.FindAssets("t:Reel", new[] { reelFolder }))
            {
                var reel = AssetDatabase.LoadAssetAtPath<Reel>(AssetDatabase.GUIDToAssetPath(guid));
                if (reel != null) return reel;
            }
            return null;
        }
    }
}
