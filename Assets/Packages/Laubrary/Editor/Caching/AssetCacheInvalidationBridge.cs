using UnityEditor;
using UnityEngine;

namespace Laubrary.Caching.Editor
{
    /// <summary>
    /// Watches Unity's own <see cref="ObjectChangeEvents"/> stream — the same signal the Editor uses
    /// internally to know when an asset's serialized properties changed, regardless of WHICH tool made the
    /// edit (a custom ZUI window, the plain Inspector, an Undo/Redo, doesn't matter) — and forwards asset
    /// property changes into <see cref="AssetCacheInvalidation"/>. This is what makes cache invalidation
    /// GENERAL: no individual editor tool needs to remember to call Invalidate() at every edit site; any
    /// ScriptableObject asset edited through any means gets this for free the moment something subscribes to
    /// it on the Runtime side.
    /// </summary>
    [InitializeOnLoad]
    static class AssetCacheInvalidationBridge
    {
        static AssetCacheInvalidationBridge()
        {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var args);
                var asset = EditorUtility.InstanceIDToObject(args.instanceId);
                if (asset != null) AssetCacheInvalidation.Invalidate(asset);
            }
        }
    }
}
