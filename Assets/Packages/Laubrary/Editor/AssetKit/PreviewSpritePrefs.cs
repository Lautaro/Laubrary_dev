using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// <summary>
    /// Editor-only persistence for a PREVIEW SUBJECT sprite — the "live preview subject (not baked)" pattern:
    /// a tool window lets the user aim an asset's effect at a REAL sprite while configuring it, but that sprite
    /// is no part of the recipe, so it must never be serialized into the asset (auditioning a sprite would dirty
    /// the asset, put a meaningless reference into version control, and risk becoming an accidental runtime
    /// dependency). It is remembered in EditorPrefs instead, keyed by the edited asset's GUID, with the value
    /// "spriteGuid:localId" (the localId picks the exact sub-sprite out of a sheet) — so each asset remembers
    /// its subject across window closes and domain reloads, per machine, never in the asset file.
    ///
    /// Shared by SpriteFxStackWindow's preview input sprite and ChunkWindow's preview subject — one canonical
    /// implementation instead of a copy per window (each window keeps its own key prefix, so existing
    /// remembered sprites survive the extraction).
    /// </summary>
    public static class PreviewSpritePrefs
    {
        /// The asset GUID used as the per-asset key, or "" for an unsaved asset (nothing durable to key on).
        public static string GuidOf(Object o)
        {
            if (o == null) return "";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long _) ? g : "";
        }

        /// Remember (or, with a null sprite, forget) the preview subject for the asset with this GUID.
        public static void Remember(string keyPrefix, string assetGuid, Sprite s)
        {
            if (string.IsNullOrEmpty(assetGuid)) return;   // unsaved asset: nothing durable to key on
            string key = keyPrefix + assetGuid;
            if (s == null) { EditorPrefs.DeleteKey(key); return; }
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out string g, out long id))
                EditorPrefs.SetString(key, g + ":" + id);
        }

        /// The remembered preview subject for the asset with this GUID, or null. Matches the exact sub-sprite
        /// by localId (a sheet holds many); falls back to the main/first sprite at the remembered path.
        public static Sprite Load(string keyPrefix, string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid)) return null;
            string val = EditorPrefs.GetString(keyPrefix + assetGuid, "");
            if (string.IsNullOrEmpty(val)) return null;
            int c = val.IndexOf(':');
            if (c <= 0) return null;
            string g = val.Substring(0, c);
            if (!long.TryParse(val.Substring(c + 1), out long id)) return null;
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(path)) return null;
            if (AssetDatabase.LoadMainAssetAtPath(path) is Sprite main &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(main, out _, out long mid) && mid == id)
                return main;
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (o is Sprite sp && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sp, out _, out long sid) && sid == id)
                    return sp;
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
