// T-0111 scratch import utility. Not a [MenuItem] by design (CLAUDE.md: never add an unrequested
// menu item) -- invoked once via Coplay's execute_script / the Unity CLI. Converts the manifest.tsv +
// _raw/*.bytes staged by the Python conversion pass (D:\CODEZ\Kiln -> raw float32 .bytes, byte-identical
// to the source .npy data) into ShaperHeightFieldPreset assets with an embedded RFloat Texture2D.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Laubrary.Shaper;

public static class HeightFieldImportTool
{
    const string Root = "Assets/Demos/ShaperDemo/TapestryHeightFields";
    const string RawDir = Root + "/_raw";
    const string OutDir = Root + "/Presets";
    const string ManifestPath = Root + "/manifest.tsv";

    struct Row { public string id, sourceId, sha256; public int resolution; public float min, max; }

    public static string Import()
    {
        AssetDatabase.Refresh();

        var rows = ReadManifest();
        Debug.Log("[HeightFieldImportTool] manifest rows: " + rows.Count);

        if (!AssetDatabase.IsValidFolder(OutDir))
            AssetDatabase.CreateFolder(Root, "Presets");

        int ok = 0, fail = 0, checksumFail = 0;
        var failures = new List<string>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            string bytesPath = RawDir + "/" + row.id + ".bytes";
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(bytesPath);
            if (ta == null) { fail++; failures.Add(row.id + ": .bytes TextAsset not found at " + bytesPath); continue; }

            byte[] raw = ta.bytes;
            int expectedBytes = row.resolution * row.resolution * 4;
            if (raw.Length != expectedBytes)
            {
                fail++; failures.Add(row.id + $": byte length {raw.Length} != expected {expectedBytes}");
                continue;
            }

            // Verify against the manifest's checksum BEFORE writing anything, proving the .bytes staging
            // itself round-tripped through Unity's TextAsset import unmodified.
            using (var sha = SHA256.Create())
            {
                string hex = BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "").ToLowerInvariant().Substring(0, 16);
                if (hex != row.sha256) { checksumFail++; failures.Add(row.id + $": sha mismatch {hex} != {row.sha256}"); continue; }
            }

            var floats = new float[row.resolution * row.resolution];
            Buffer.BlockCopy(raw, 0, floats, 0, raw.Length);

            var tex = new Texture2D(row.resolution, row.resolution, TextureFormat.RFloat, false, true); // linear=true, no mips
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            tex.SetPixelData(floats, 0);
            tex.Apply(false, false); // keep readable
            tex.name = row.id + "_Field";

            var preset = ScriptableObject.CreateInstance<ShaperHeightFieldPreset>();
            preset.sourceId = row.sourceId;
            preset.resolution = row.resolution;
            preset.measuredMin = row.min;
            preset.measuredMax = row.max;

            string assetPath = OutDir + "/" + row.id + ".asset";
            AssetDatabase.CreateAsset(preset, assetPath);
            AssetDatabase.AddObjectToAsset(tex, preset);
            preset.field = tex;
            EditorUtility.SetDirty(preset);

            ok++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"ok={ok} fail={fail} checksumFail={checksumFail} total={rows.Count}";
        Debug.Log("[HeightFieldImportTool] " + summary);
        if (failures.Count > 0)
            Debug.LogWarning("[HeightFieldImportTool] failures:\n" + string.Join("\n", failures.ToArray()));
        return summary;
    }

    /// <summary>Spot-checks N random imported presets: reload the texture from disk, re-read its pixel
    /// data, and compare against the manifest's min/max and a fresh SHA256 of the reconstructed bytes.
    /// This is the "byte-exact reconstruction" proof, run AFTER SaveAssets/Refresh so it reads back
    /// exactly what is now on disk, not the in-memory object still held by the import pass above.</summary>
    public static string VerifySample()
    {
        var rows = ReadManifest();
        var rnd = new System.Random(12345);
        int n = Math.Min(8, rows.Count);
        var chosen = new List<Row>();
        var used = new HashSet<int>();
        while (chosen.Count < n)
        {
            int idx = rnd.Next(rows.Count);
            if (used.Add(idx)) chosen.Add(rows[idx]);
        }

        int pass = 0, failCount = 0;
        var lines = new List<string>();
        foreach (var row in chosen)
        {
            string assetPath = OutDir + "/" + row.id + ".asset";
            var preset = AssetDatabase.LoadAssetAtPath<ShaperHeightFieldPreset>(assetPath);
            if (preset == null || preset.field == null)
            {
                failCount++; lines.Add(row.id + ": preset or field missing"); continue;
            }
            var floats = preset.field.GetPixelData<float>(0);
            var bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats.ToArray(), 0, bytes, 0, bytes.Length);
            using (var sha = SHA256.Create())
            {
                string hex = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant().Substring(0, 16);
                bool okHash = hex == row.sha256;
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var v in floats) { if (v < mn) mn = v; if (v > mx) mx = v; }
                bool okRange = Mathf.Approximately(mn, row.min) && Mathf.Approximately(mx, row.max);
                if (okHash && okRange) pass++; else failCount++;
                lines.Add($"{row.id}: sha {(okHash ? "MATCH" : "MISMATCH " + hex + " vs " + row.sha256)}, " +
                          $"range measured=[{mn},{mx}] manifest=[{row.min},{row.max}] {(okRange ? "OK" : "MISMATCH")}");
            }
        }
        string summary = $"VerifySample pass={pass} fail={failCount} of {chosen.Count}\n" + string.Join("\n", lines.ToArray());
        Debug.Log("[HeightFieldImportTool] " + summary);
        return summary;
    }

    /// <summary>Deletes the _raw staging .bytes files once the Presets/*.asset library has the data
    /// embedded, so the ~64MB is not duplicated on disk.</summary>
    public static string CleanupStaging()
    {
        if (AssetDatabase.IsValidFolder(RawDir))
        {
            AssetDatabase.DeleteAsset(RawDir);
            AssetDatabase.Refresh();
            return "deleted " + RawDir;
        }
        return "nothing to delete";
    }

    static List<Row> ReadManifest()
    {
        string full = Path.Combine(Directory.GetCurrentDirectory(), ManifestPath);
        var lines = File.ReadAllLines(full);
        var rows = new List<Row>();
        for (int i = 1; i < lines.Length; i++) // skip header
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var parts = lines[i].Split('\t');
            if (parts.Length < 6) continue;
            rows.Add(new Row
            {
                id = parts[0],
                sourceId = parts[1],
                resolution = int.Parse(parts[2]),
                min = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                max = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                sha256 = parts[5],
            });
        }
        return rows;
    }
}
