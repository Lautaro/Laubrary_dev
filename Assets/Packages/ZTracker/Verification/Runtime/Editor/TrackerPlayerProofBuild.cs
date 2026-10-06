using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Laubrary.ZTracker.Proof
{
    public static class TrackerPlayerProofBuild
    {
        public const string FixedOutput = "D:/UNITY/_builds/ztracker-proof/ZTrackerProof.exe";
        static void RequireIsolated()
        {
            if (string.Equals(Path.GetFullPath(Application.dataPath),Path.GetFullPath("D:/UNITY/Laubrary Dev/Assets"),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Proof commands require a disposable verification project; never build in the owner's editor.");
        }
        public static string Start(string output = FixedOutput)
        {
            RequireIsolated();
            EditorApplication.delayCall += () => BuildNow(output);
            return "scheduled isolated non-Development P7 proof build";
        }
        public static void BuildFromCommandLine() => BuildNow();
        public static string BuildNow(string output = FixedOutput)
        {
            RequireIsolated();
            if (!string.Equals(Path.GetFullPath(output),Path.GetFullPath(FixedOutput),StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Reuse the fixed P7 proof output path");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string oldName = PlayerSettings.productName;
            try
            {
                PlayerSettings.productName = "ZTrackerProof";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { "Assets/Packages/ZTracker/Verification/Runtime/TrackerProof.unity" },
                    locationPathName = output, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None, extraScriptingDefines = new[] { "ZTRACKER_PROOF" }
                });
                var errors = new System.Collections.Generic.List<string>();
                foreach (var step in report.steps) foreach (var message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Exception) errors.Add(step.name + ": " + message.content);
                File.WriteAllLines(Path.Combine(Path.GetDirectoryName(output),"build-errors.txt"),errors);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build-result.json"),JsonUtility.ToJson(new BuildEvidence {
                    result=report.summary.result.ToString(), errors=report.summary.totalErrors, seconds=report.summary.totalTime.TotalSeconds,
                    unity=Application.unityVersion, options=report.summary.options.ToString(), development=false, output=output,
                    proofAssembly="UNITY_EDITOR || ZTRACKER_PROOF", sourceWitness=Engine.TrackerPreparedSong.ImplementationWitness
                },true));
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("P7 build failed; see build-errors.txt");
                return report.summary.result.ToString();
            }
            finally { PlayerSettings.productName = oldName; }
        }
        [Serializable] sealed class BuildEvidence { public string result,unity,options,output,proofAssembly,sourceWitness;public int errors;public double seconds;public bool development; }
    }
}
