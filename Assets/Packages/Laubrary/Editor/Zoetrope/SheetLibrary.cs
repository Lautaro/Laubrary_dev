using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The canonical home for source sprite sheets (<c>Assets/SpriteSheets</c>) plus the helpers the
    /// Animation Builder uses to pick one fast. Sheets are reused across many animations, so this exposes
    /// (a) the sheets ALREADY used to extract sprites — detected by the presence of a RegionSlicer sidecar
    /// (see <see cref="RegionSlicerPersistence"/>), most-recently-used first — and (b) a one-shot download
    /// of a new sheet straight from a URL into the folder. UI-free; the window owns presentation.
    /// </summary>
    public static class SheetLibrary
    {
        public const string Folder = "Assets/SpriteSheets";

        /// <summary>One row in the Recent picker: the sheet, its friendly display name, and whether it has
        /// already been used to extract sprites (carries a slicing sidecar).</summary>
        public struct SheetEntry
        {
            public Texture2D tex;
            public string displayName;
            public bool used;
        }

        /// <summary>
        /// Every sheet under <see cref="Folder"/> — both ones already sliced and ones merely downloaded/dropped
        /// in. Used sheets come first, most-recently-used first (sidecar last-write time, which every save
        /// rewrites); the rest follow alphabetically by display name.
        /// </summary>
        public static List<SheetEntry> EnumerateSheets()
        {
            var rows = new List<(SheetEntry entry, DateTime when)>();
            if (!AssetDatabase.IsValidFolder(Folder)) return new List<SheetEntry>();

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                bool used = RegionSlicerPersistence.Exists(path);
                rows.Add((
                    new SheetEntry { tex = tex, displayName = SheetRegistry.GetDisplayName(tex), used = used },
                    used ? SidecarWriteTimeUtc(path) : DateTime.MinValue));
            }

            return rows
                .OrderByDescending(r => r.entry.used)
                .ThenByDescending(r => r.when)
                .ThenBy(r => r.entry.displayName, StringComparer.OrdinalIgnoreCase)
                .Select(r => r.entry)
                .ToList();
        }

        /// <summary>Delete a sheet from the library: its image asset, its slicing sidecar, and its display-name
        /// entry. Already-baked zoe atlases are unaffected; only RE-editing an animation would need the
        /// sheet back. Returns false (with <paramref name="error"/>) if the asset could not be removed.</summary>
        public static bool DeleteSheet(Texture2D tex, out string error)
        {
            error = null;
            if (tex == null) { error = "No sheet."; return false; }
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) { error = "Sheet has no asset path."; return false; }

            SheetRegistry.Clear(tex);

            string sidecar = RegionSlicerPersistence.SidecarPathFor(path);
            if (!string.IsNullOrEmpty(sidecar) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sidecar) != null)
                AssetDatabase.DeleteAsset(sidecar);

            if (!AssetDatabase.DeleteAsset(path)) { error = "Unity refused to delete the asset."; return false; }
            AssetDatabase.Refresh();
            return true;
        }

        private static DateTime SidecarWriteTimeUtc(string texturePath)
        {
            try { return File.GetLastWriteTimeUtc(ToSystemPath(RegionSlicerPersistence.SidecarPathFor(texturePath))); }
            catch { return DateTime.MinValue; }
        }

        /// <summary>
        /// Download an image from <paramref name="url"/> into <see cref="Folder"/>, import it, register its
        /// friendly <paramref name="displayName"/> (kept separate from identity — see <see cref="SheetRegistry"/>),
        /// and return the resulting Texture2D. Returns null with <paramref name="error"/> set on failure. The
        /// file extension is taken from the URL when it names an image, otherwise sniffed from the downloaded
        /// bytes. The caller loads/crispens the importer (see the window's LoadSheet).
        /// </summary>
        public static Texture2D DownloadImage(string url, string displayName, out string error)
        {
            error = null;
            url = (url ?? "").Trim();
            if (string.IsNullOrEmpty(url)) { error = "Empty URL."; return null; }

            byte[] data;
            try
            {
                using (var client = new WebClient())
                {
                    // Some hosts 403 a request with no User-Agent; present a benign one.
                    client.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Zoetrope Unity Editor)");
                    data = client.DownloadData(url);
                }
            }
            catch (Exception e) { error = "Download failed: " + e.Message; return null; }
            if (data == null || data.Length == 0) { error = "Download was empty."; return null; }

            ZoeBuilder.EnsureFolder(Folder);
            string baseName = !string.IsNullOrWhiteSpace(displayName) ? ZoeBuilder.Sanitize(displayName) : DeriveBaseName(url);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{baseName}.{ResolveExtension(url, data)}");
            try
            {
                string sys = ToSystemPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(sys));
                File.WriteAllBytes(sys, data);
            }
            catch (Exception e) { error = "Could not write file: " + e.Message; return null; }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) { error = "The downloaded file did not import as a texture (not an image?)."; return null; }

            if (!string.IsNullOrWhiteSpace(displayName)) SheetRegistry.SetDisplayName(tex, displayName);
            return tex;
        }

        private static string DeriveBaseName(string url)
        {
            string name = null;
            try { name = Path.GetFileNameWithoutExtension(new Uri(url).LocalPath); } catch { /* fall through */ }
            if (string.IsNullOrWhiteSpace(name)) name = "downloaded_sheet";
            return ZoeBuilder.Sanitize(name);
        }

        private static string ResolveExtension(string url, byte[] data)
        {
            // Trust a known image extension named in the URL path first.
            string urlExt = null;
            try { urlExt = Path.GetExtension(new Uri(url).LocalPath).TrimStart('.').ToLowerInvariant(); } catch { /* ignore */ }
            switch (urlExt)
            {
                case "png": case "jpg": case "jpeg": case "gif": case "bmp": case "tga": return urlExt;
            }

            // Otherwise sniff the magic bytes.
            if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "png";
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "jpg";
            if (data.Length >= 3 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return "gif";
            if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D) return "bmp";
            return "png";
        }

        private static string ToSystemPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
