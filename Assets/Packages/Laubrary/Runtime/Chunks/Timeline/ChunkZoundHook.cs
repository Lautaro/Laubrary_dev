// ChunkZoundHook — the seam that lets a Chunks timeline play a Zound without Chunks referencing Zounds
// (AgentHQ T-0038).
//
// Same optional-bridge shape as Zoetrope's ZoundPickerHook / PyreEditorLink / ChunkSpecEditorLink: the
// consumer declares the hook, a bridge module fills it in, and a project that has no audio tool simply never
// registers one. The default of null is a feature — it means "no sound", not "broken": a Zound Event marker
// in a project without Zounds silently plays nothing rather than throwing on every burst.
//
// This is the RUNTIME half. Its editor twin (picking a Zound rather than playing one) is
// Editor/Chunks/ChunkZoundPickerHook.cs, filled by Editor/ChunksZounds.
using System;

namespace Laubrary.Chunks
{
    /// How a Chunks timeline plays a Zound. Filled in by the Runtime/ChunksZounds bridge module.
    public static class ChunkZoundHook
    {
        /// Play a Zound by name. Set by the ChunksZounds bridge to ZoundEngine.PlayZound; null in a project
        /// with no audio tool, in which case Zound markers are silent.
        ///
        /// A NAME rather than an asset reference because that is how Zounds itself addresses one, and it is
        /// what PlayZoundEffect already stores — one Zound reference should not mean two different things in
        /// two Laubrary tools.
        public static Action<string> Play;

        /// Whether an audio tool has registered itself. The editor uses it to say so honestly rather than
        /// authoring a marker that can never make a sound.
        public static bool Available => Play != null;
    }
}
