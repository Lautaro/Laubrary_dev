using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Opens a project file (`.aseprite` / `.png`) in the Aseprite app for editing — the launch half of the
    /// "promote to editable" seam (Zoetrope owns/produces the file; Aseprite is just the editor). On save,
    /// Unity's importer/texture-importer reimports automatically. The path to the Aseprite executable is
    /// auto-detected from common install locations, or set via Tools ▸ Zoetrope ▸ Set Aseprite Path…;
    /// failing that it falls back to the OS file association.
    /// </summary>
    public static class AsepriteLauncher
    {
        const string PrefKey = "Zoetrope.AsepritePath";

        public static string ExePath
        {
            get => EditorPrefs.GetString(PrefKey, "");
            set => EditorPrefs.SetString(PrefKey, value ?? "");
        }

        static readonly string[] CommonPaths =
        {
            @"C:\Program Files\Aseprite\Aseprite.exe",
            @"C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe",
            @"C:\Program Files\Steam\steamapps\common\Aseprite\Aseprite.exe",
            @"C:\Program Files\LibreSprite\libresprite.exe",
            "/Applications/Aseprite.app/Contents/MacOS/aseprite",
        };

        public static string ResolveExe()
        {
            if (File.Exists(ExePath)) return ExePath;
            foreach (var p in CommonPaths) if (File.Exists(p)) return p;
            return null;
        }

        /// <summary>Open an asset path (or absolute path) in Aseprite. Returns false if nothing could launch it.</summary>
        public static bool Open(string assetPath)
        {
            string abs = Path.GetFullPath(assetPath);
            if (!File.Exists(abs)) { UnityEngine.Debug.LogWarning($"[Zoetrope] file not found: {abs}"); return false; }
            string exe = ResolveExe();
            try
            {
                if (exe != null) Process.Start(new ProcessStartInfo(exe, $"\"{abs}\"") { UseShellExecute = false });
                else Process.Start(new ProcessStartInfo(abs) { UseShellExecute = true });   // OS association
                return true;
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Zoetrope] could not open Aseprite ({e.Message}). Set the path via Tools ▸ Zoetrope ▸ Set Aseprite Path…");
                return false;
            }
        }

        [MenuItem("Tools/Laubrary/Zoetrope/Set Aseprite Path…")]
        static void SetPath()
        {
            string start = File.Exists(ExePath) ? Path.GetDirectoryName(ExePath) : "C:/Program Files";
            string picked = EditorUtility.OpenFilePanel("Locate the Aseprite executable", start, "exe");
            if (!string.IsNullOrEmpty(picked)) { ExePath = picked; UnityEngine.Debug.Log($"[Zoetrope] Aseprite path = {picked}"); }
        }

        [MenuItem("Assets/Open in Aseprite", true)]
        static bool OpenSelectedValidate()
        {
            var p = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(p) && (p.EndsWith(".aseprite") || p.EndsWith(".ase") || p.EndsWith(".png"));
        }

        [MenuItem("Assets/Open in Aseprite", false, 30)]
        static void OpenSelected() => Open(AssetDatabase.GetAssetPath(Selection.activeObject));
    }
}
