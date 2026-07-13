using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace Laubrary.Chunks.Editor
{
    /// Lets another tool's Editor code register an "open this animation source in its own authoring tool" action
    /// for a concrete IChunkAnimation asset type, without Chunks.Editor needing to reference that tool. Mirrors
    /// Pyre's PyrePreviewSubjectProvider (a resolver a bridge module fills in so the core tool stays decoupled),
    /// keyed by type here since more than one animation source kind can register at once.
    public static class ChunkAnimationEditors
    {
        static readonly Dictionary<Type, Action<Object>> openers = new Dictionary<Type, Action<Object>>();

        /// Called by a tool's own Editor assembly (e.g. via [InitializeOnLoad]) to wire up its "Edit" action.
        public static void Register<T>(Action<T> opener) where T : Object
            => openers[typeof(T)] = obj => opener((T)obj);

        public static bool CanOpen(Object animationSource)
            => animationSource != null && openers.ContainsKey(animationSource.GetType());

        public static void Open(Object animationSource)
        {
            if (animationSource != null && openers.TryGetValue(animationSource.GetType(), out var fn)) fn(animationSource);
        }
    }
}
