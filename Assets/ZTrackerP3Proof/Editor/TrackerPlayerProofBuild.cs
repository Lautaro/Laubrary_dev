using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Laubrary.ZTracker.Proof
{
    public static class TrackerPlayerProofBuild
    {
        public static string Start(string output)
        {
            EditorApplication.delayCall+=()=>BuildNow(output);
            return "scheduled isolated tracker player build";
        }
        public static string BuildNow(string output)
        {
                string path="Assets/ZTrackerP3Proof/TrackerProof.unity";
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene,path);EditorSceneManager.CloseScene(scene,true);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{path},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
                var errors = new System.Collections.Generic.List<string>();
                foreach (var step in report.steps)
                    foreach (var message in step.messages)
                        if (message.type == UnityEngine.LogType.Error || message.type == UnityEngine.LogType.Exception)
                            errors.Add(step.name + ": " + message.content);
                File.WriteAllLines(Path.Combine(Path.GetDirectoryName(output), "build-errors.txt"), errors);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build-result.json"),"{\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+",\"seconds\":"+report.summary.totalTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            return report.summary.result.ToString();
        }
    }
}
