// ChunkCapabilityCopy — a deep copy of one capability, made by Unity's own serializer.
//
// A capability can hold managed references of its own (a Debris Scatter's modifier list), so a hand-written
// field copy would share them with the original and an edit to the copy would quietly edit both. Round-tripping
// through the serializer builds fresh instances for every managed reference and keeps asset references
// pointing at the same assets, which is exactly what "duplicate this card" means.
using UnityEditor;
using UnityEngine;

namespace Laubrary.Chunks.Editor
{
    internal sealed class ChunkCapabilityCopy : ScriptableObject
    {
        [SerializeReference] public ChunkCapability capability;

        /// A copy of <paramref name="source"/> with every managed reference its own. The id and colour slot
        /// are copied verbatim — the caller gives the copy its own, since only the recipe knows what is free.
        internal static ChunkCapability Of(ChunkCapability source)
        {
            if (source == null) return null;
            var from = CreateInstance<ChunkCapabilityCopy>();
            var to = CreateInstance<ChunkCapabilityCopy>();
            from.hideFlags = HideFlags.HideAndDontSave;
            to.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                from.capability = source;
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(from), to);
                return to.capability;
            }
            finally
            {
                from.capability = null;   // never let the throwaway hold the live one while it is destroyed
                DestroyImmediate(from);
                DestroyImmediate(to);
            }
        }
    }
}
