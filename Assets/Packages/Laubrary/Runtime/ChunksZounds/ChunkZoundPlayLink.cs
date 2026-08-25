using UnityEngine;
using Laubrary.Chunks;
using Laubrary.Zounds;

namespace Laubrary.ChunksZounds
{
    /// Fills in Chunks' Zound PLAY hook with Zounds' own engine, so a timeline's Zound Event marker actually
    /// makes a sound.
    ///
    /// Lives in a BRIDGE module for the usual reason: Chunks' core must not depend on Zounds, so a project
    /// with no audio tool still compiles and simply plays nothing. Same shape as PlayZoundEffect
    /// (Zoetrope+Zounds) and the LaunimatorZounds frame-event bridge — Chunks only knows it has a hook.
    ///
    /// Installed from TWO entry points on purpose. The runtime one is what a build uses; the editor one is
    /// what makes an editor-side preview audible, because [RuntimeInitializeOnLoadMethod] does not run until
    /// Play is entered and a timeline auditioned from the Chunks window would otherwise be silent.
    public static class ChunkZoundPlayLink
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            ChunkZoundHook.Play = name =>
            {
                if (!string.IsNullOrEmpty(name)) ZoundEngine.PlayZound(name);
            };
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InstallInEditor() => Install();
#endif
    }
}
