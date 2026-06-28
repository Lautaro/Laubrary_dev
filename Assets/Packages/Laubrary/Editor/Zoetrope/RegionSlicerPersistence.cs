using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// JSON-sidecar persistence for the <see cref="AnimationBuilderWindow"/>. The window's whole working
    /// state (committed regions + their per-cell rects &amp; pivots, the Animate &amp; Align sequence,
    /// and every grid/alpha/pivot setting) is mirrored into a flat, serializable DTO and written next
    /// to the SOURCE TEXTURE as <c>&lt;textureAssetPath&gt;.regionslicer.json</c>. Reload the sheet and
    /// the work comes back.
    ///
    /// Newtonsoft (not JsonUtility) so nested <c>List&lt;Rect&gt;</c>/<c>List&lt;Vector2&gt;</c> round-trip
    /// cleanly — JsonUtility can't serialize lists of structs at the top level and mangles nesting. The
    /// DTO uses plain float quartets/pairs rather than Unity's Rect/Vector2 so the JSON is compact,
    /// stable, and engine-version-proof.
    ///
    /// The window owns the mapping to/from its private runtime fields (see AnimationBuilderWindow's
    /// BuildState/ApplyState); this class is the pure on-disk format + read/write + index-drift guard.
    /// </summary>
    internal static class RegionSlicerPersistence
    {
        // Bump if the on-disk shape changes incompatibly; LoadFrom tolerates older minor shapes.
        public const int CurrentVersion = 1;
        public const string SidecarSuffix = ".regionslicer.json";

        // ── DTOs (flat, Newtonsoft-friendly) ─────────────────────────────────
        [Serializable]
        public class RectDto
        {
            public float x, y, w, h;
            public RectDto() { }
            public RectDto(Rect r) { x = r.x; y = r.y; w = r.width; h = r.height; }
            public Rect ToRect() => new Rect(x, y, w, h);
        }

        [Serializable]
        public class Vec2Dto
        {
            public float x, y;
            public Vec2Dto() { }
            public Vec2Dto(Vector2 v) { x = v.x; y = v.y; }
            public Vector2 ToVec2() => new Vector2(x, y);
        }

        [Serializable]
        public class RegionDto
        {
            public string label;
            public List<RectDto> cells = new List<RectDto>();
            public List<Vec2Dto> pivots = new List<Vec2Dto>();   // index-aligned with cells
            public RectDto bounds;                                // dimmed-overlay box (optional)
        }

        [Serializable]
        public class CellRefDto
        {
            public int region;
            public int cell;
        }

        [Serializable]
        public class StateDto
        {
            public int version = CurrentVersion;
            public string textureGuid;          // sanity check on restore (informational)
            public string texturePath;          // informational
            public int texW, texH;              // informational; helps spot a re-imported/resized sheet

            // Committed regions (includes the hand-picked region — it's just another region).
            public List<RegionDto> regions = new List<RegionDto>();

            // Animate & Align sequence + playback settings.
            public List<CellRefDto> sequence = new List<CellRefDto>();
            public float animFps = 8f;
            public bool onionSkin;

            // Settings.
            public bool alphaTrim;
            public int alphaThreshold = 8;
            public float ppu = 16f;

            // Background colour key (sheets with a solid-colour background instead of alpha).
            public bool bgKeyEnabled;
            public int bgKeyR, bgKeyG, bgKeyB;
            public int bgKeyTolerance = 12;

            // Registration: fixed-frame box vs auto-size, and the shared registration point.
            public bool fixedFrame;
            public int frameWidth = 32, frameHeight = 32;
            public float framePivotX = 0.5f, framePivotY = 0f;
            public int pivotMode;               // (int)GridSlicer.PivotMode
            public Vec2Dto customPivot;
            public int gridMode;                // (int)RegionSlicer.GridMode
            public int cols = 3, rows = 10;
            public int cellW = 16, cellH = 16;
            public int spacing, padding;
        }

        // ── Path helpers ─────────────────────────────────────────────────────
        /// <summary>Sidecar asset path for a texture asset path, or null if the input is empty.</summary>
        public static string SidecarPathFor(string textureAssetPath)
        {
            if (string.IsNullOrEmpty(textureAssetPath)) return null;
            return textureAssetPath + SidecarSuffix;
        }

        /// <summary>True if a sidecar exists on disk for the given texture asset path.</summary>
        public static bool Exists(string textureAssetPath)
        {
            string p = SidecarPathFor(textureAssetPath);
            if (p == null) return false;
            return File.Exists(ToSystemPath(p));
        }

        private static string ToSystemPath(string assetPath)
        {
            // assetPath is "Assets/..."; project root is one level above "Assets".
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        // ── Read / write ─────────────────────────────────────────────────────
        /// <summary>Serialize <paramref name="state"/> to the texture's sidecar. Returns the asset path
        /// written. Throws on I/O failure (caller surfaces the message).</summary>
        public static string Save(string textureAssetPath, StateDto state)
        {
            string assetPath = SidecarPathFor(textureAssetPath);
            if (assetPath == null) throw new InvalidOperationException("No texture path — load a sheet first.");
            state.version = CurrentVersion;

            string json = JsonConvert.SerializeObject(state, Formatting.Indented);
            string sysPath = ToSystemPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(sysPath));
            File.WriteAllText(sysPath, json);

            // Make the sidecar visible in the Project window (harmless if it re-imports as a TextAsset).
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return assetPath;
        }

        /// <summary>Delete the sidecar for a texture (file + imported asset). Returns true if one existed.
        /// No-op for an empty path or a missing sidecar.</summary>
        public static bool Delete(string textureAssetPath)
        {
            string assetPath = SidecarPathFor(textureAssetPath);
            if (assetPath == null) return false;
            string sysPath = ToSystemPath(assetPath);
            bool existed = File.Exists(sysPath);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
            else if (existed) { File.Delete(sysPath); AssetDatabase.Refresh(); }
            return existed;
        }

        /// <summary>Load &amp; deserialize the sidecar for a texture, or null if none exists / parse fails
        /// (the failure message is returned via <paramref name="error"/>).</summary>
        public static StateDto Load(string textureAssetPath, out string error)
        {
            error = null;
            string assetPath = SidecarPathFor(textureAssetPath);
            if (assetPath == null) { error = "No texture path."; return null; }
            string sysPath = ToSystemPath(assetPath);
            if (!File.Exists(sysPath)) { error = "No sidecar found."; return null; }

            try
            {
                string json = File.ReadAllText(sysPath);
                var state = JsonConvert.DeserializeObject<StateDto>(json);
                if (state == null) { error = "Sidecar parsed to null."; return null; }
                Normalize(state);
                return state;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>Guard against malformed/old sidecars: ensure lists exist and per-region pivot lists
        /// are the same length as their cell lists; drop sequence refs that point outside existing
        /// regions/cells (index-drift guard so a stale sequence can't crash the play loop).</summary>
        public static void Normalize(StateDto s)
        {
            if (s.regions == null) s.regions = new List<RegionDto>();
            foreach (var reg in s.regions)
            {
                if (reg.cells == null) reg.cells = new List<RectDto>();
                if (reg.pivots == null) reg.pivots = new List<Vec2Dto>();
                // Keep pivots parallel to cells (mirrors Region.SyncPivots).
                var fill = new Vec2Dto(new Vector2(0.5f, 0f));
                while (reg.pivots.Count < reg.cells.Count) reg.pivots.Add(fill);
                while (reg.pivots.Count > reg.cells.Count) reg.pivots.RemoveAt(reg.pivots.Count - 1);
            }

            if (s.sequence == null) s.sequence = new List<CellRefDto>();
            s.sequence.RemoveAll(cr =>
                cr == null
                || cr.region < 0 || cr.region >= s.regions.Count
                || cr.cell < 0 || cr.cell >= s.regions[cr.region].cells.Count);
        }
    }
}
