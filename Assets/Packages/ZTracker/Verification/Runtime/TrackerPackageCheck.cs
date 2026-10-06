#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;
using UnityEngine;

namespace Laubrary.ZTracker.Proof
{
    public static class TrackerPackageCheck
    {
        // Uses Unity's actual normal-player assembly graph without building or changing scenes.
        public static string Execute()
        {
            var players = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var names = new[] { "ZTracker", "com.Lautaro-Arino.Laubrary.ZTracker.Engine", "com.Lautaro-Arino.Laubrary.ZTracker" };
            var production = players.Where(a => names.Contains(a.name)).ToArray();
            if (production.Length != 3) throw new Exception("Expected data, engine and playback player assemblies; got " + production.Length);
            if (players.Any(a => a.name.Contains("ZTracker.P3Proof") || a.name == "ZTrackerP3Golden.Editor")) throw new Exception("Verification included in normal player graph");
            if (production.Any(a => a.sourceFiles.Any(p => p.Replace('\\', '/').Contains("/Verification/")))) throw new Exception("Verification source ships in a production assembly");
            foreach (var assembly in production)
                foreach (var path in assembly.sourceFiles)
                    if (File.ReadAllText(path).Contains("DllImport(")) throw new Exception("Native tracker import in " + path);
            string songSource = production.SelectMany(a => a.sourceFiles).Single(p => Path.GetFileName(p) == "ZTrackerSong.cs");
            string package = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(songSource), "..", ".."));
            if (Directory.GetFiles(package, "*.dll", SearchOption.AllDirectories).Length != 0) throw new Exception("Imported tracker DLL remains");
            if (players.Where(a => a.name.Contains("Laubrary") && !a.name.Contains("ZTracker")).Any(a => a.assemblyReferences.Any(r => production.Any(p => p.name == r.name)))) throw new Exception("Core Laubrary depends on optional tracker");
            return "PASS normal-player graph: 3 production assemblies; verification excluded; no tracker DLL/import; core Laubrary independent. Proof define inclusion requires separate AOT build.";
        }
    }
}
#endif
