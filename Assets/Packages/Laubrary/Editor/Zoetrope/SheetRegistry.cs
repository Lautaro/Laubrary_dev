using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// A small, friendly DISPLAY NAME for each source sheet, kept separate from identity. A sheet is always
    /// identified by its texture asset GUID (that's what recipes reference); this registry only maps
    /// GUID → a human-facing label, so the label can be edited freely without ever changing identity or
    /// breaking any reference. Persisted as one JSON file under <c>Assets/Zoetrope</c>.
    /// </summary>
    internal static class SheetRegistry
    {
        private const string Path = "Assets/Zoetrope/SheetNames.json";
        private static Dictionary<string, string> _map;

        private static void EnsureLoaded()
        {
            if (_map != null) return;
            _map = new Dictionary<string, string>();
            string sys = ToSystemPath(Path);
            if (!File.Exists(sys)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(sys));
                if (loaded != null) _map = loaded;
            }
            catch { /* corrupt/old file → start fresh, harmless */ }
        }

        /// <summary>The display name for a texture, falling back to the asset file name if none is set.</summary>
        public static string GetDisplayName(Texture2D tex)
        {
            if (tex == null) return "";
            string guid = GuidOf(tex);
            EnsureLoaded();
            return _map.TryGetValue(guid, out var name) && !string.IsNullOrWhiteSpace(name) ? name : tex.name;
        }

        /// <summary>Set (or clear) a texture's display name. Clearing falls back to the asset file name.</summary>
        public static void SetDisplayName(Texture2D tex, string displayName)
        {
            if (tex == null) return;
            string guid = GuidOf(tex);
            if (string.IsNullOrEmpty(guid)) return;
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim() == tex.name)
                _map.Remove(guid);
            else
                _map[guid] = displayName.Trim();
            Persist();
        }

        /// <summary>Forget a texture's display name (used when deleting the sheet).</summary>
        public static void Clear(Texture2D tex)
        {
            if (tex == null) return;
            string guid = GuidOf(tex);
            if (string.IsNullOrEmpty(guid)) return;
            EnsureLoaded();
            if (_map.Remove(guid)) Persist();
        }

        private static void Persist()
        {
            try
            {
                string sys = ToSystemPath(Path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sys));
                File.WriteAllText(sys, JsonConvert.SerializeObject(_map, Formatting.Indented));
                AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            }
            catch { /* best-effort; a missing label is non-fatal */ }
        }

        private static string GuidOf(Texture2D tex)
            => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tex));

        private static string ToSystemPath(string assetPath)
            => System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, assetPath));
    }
}
