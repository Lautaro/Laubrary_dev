using System.IO;
using UnityEditor;
using Laubrary.Chunks.Editor;

namespace Laubrary.Zoetrope.Editor
{
    /// Registers "Edit" for a ZoeAnimationChunkAdapter asset picked into a Chunks ChunkSpec — jumps straight into
    /// the Animation Builder on the wrapped animation. The adapter only stores a ZoeVersion (a runtime-safe
    /// reference with no back-link to its owning Zoe), so this resolves the owning Zoe by walking up from the
    /// version asset's folder (Zoes/&lt;Name&gt;_&lt;id&gt;/draft|v{n}/, per ZoeRepo's layout) — editor-only lookup,
    /// kept out of the runtime adapter.
    [InitializeOnLoad]
    static class ZoeAnimationChunkAdapterEditorLink
    {
        static ZoeAnimationChunkAdapterEditorLink()
        {
            ChunkAnimationEditors.Register<ZoeAnimationChunkAdapter>(a =>
            {
                var zoe = FindOwningZoe(a.version);
                if (zoe != null) AnimationBuilderWindow.OpenForEdit(zoe, a.animationName);
            });
        }

        static Zoe FindOwningZoe(ZoeVersion version)
        {
            if (version == null) return null;
            string versionAssetPath = AssetDatabase.GetAssetPath(version);
            if (string.IsNullOrEmpty(versionAssetPath)) return null;

            string versionFolder = Path.GetDirectoryName(versionAssetPath)?.Replace('\\', '/');
            string zoeFolder = string.IsNullOrEmpty(versionFolder) ? null : Path.GetDirectoryName(versionFolder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(zoeFolder)) return null;

            foreach (var guid in AssetDatabase.FindAssets("t:Zoe", new[] { zoeFolder }))
            {
                var zoe = AssetDatabase.LoadAssetAtPath<Zoe>(AssetDatabase.GUIDToAssetPath(guid));
                if (zoe != null) return zoe;
            }
            return null;
        }
    }
}
