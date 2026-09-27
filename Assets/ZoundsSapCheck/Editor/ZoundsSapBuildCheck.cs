using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks.EditorTools {

    /// <summary>
    /// Makes a standalone build of the audio check and leaves it somewhere it can be run, so the one question a
    /// build answers — is the audio chain genuinely compiled once it ships, or does it quietly fall back to
    /// ordinary code — can actually be answered.
    ///
    /// **Two deliberate choices keep this from disturbing the project.**
    ///
    /// It builds from an explicit list of one scene rather than from the project's build list. The build list is
    /// shared configuration, and a build made from it today would launch something unrelated, so using it would
    /// mean changing what everyone else's builds do just to run a check.
    ///
    /// And it creates the check scene ALONGSIDE whatever scene is already open rather than replacing it, then
    /// closes it again. This editor was found holding an unsaved scene belonging to a different project on disk;
    /// replacing the open scene would have discarded that work. New objects are made while the check scene is
    /// temporarily the active one, so nothing is ever created inside — or removed from — the scene already open.
    ///
    /// **The build is scheduled rather than run inline**, because a build blocks the editor for minutes and
    /// anything waiting on a reply from it gives up long before it finishes. Progress is reported by files on
    /// disk instead, which can be read whenever.
    /// </summary>
    public static class ZoundsSapBuildCheck {

        public const string ScenePath = "Assets/ZoundsSapCheck/ZoundsSapCheck.unity";

        private static string BuildRoot => Path.Combine(Path.GetTempPath(), "ZoundsSapCheckBuild");
        private static string ExePath => Path.Combine(BuildRoot, "ZoundsSapCheck.exe");
        private static string StatusPath => Path.Combine(BuildRoot, "build-status.txt");

        [MenuItem("Laubrary/Zounds/Checks/6 - Build a standalone audio check")]
        public static void StartBuildFromMenu() { StartBuild(); }

        /// <summary>
        /// Schedules the build and returns at once. Watch the status file for the outcome; its path is returned.
        /// </summary>
        public static string StartBuild() {
            Directory.CreateDirectory(BuildRoot);
            File.WriteAllText(StatusPath, "scheduled " + DateTime.Now.ToString("HH:mm:ss") + "\n");
            EditorApplication.delayCall += RunBuild;
            return StatusPath;
        }

        private static void RunBuild() {
            try {
                // Checked BEFORE building, because otherwise this fails in a thoroughly unhelpful way: the
                // addressable-content step refuses to run while any scene has unsaved changes, and what comes back
                // is a build result of "Unknown" after zero seconds with no output and no errors counted. The real
                // reason is only visible in the console. Saying it here costs nothing and saves the next person
                // from that hunt.
                //
                // It is reported rather than fixed on purpose. Saving someone's open scene to get a build through
                // is not this check's decision to make — and in the case that prompted this, the unsaved scene
                // belonged to an entirely different project on disk.
                // Saved here, immediately before the check, rather than beforehand. Something in this project marks
                // the open scene as modified again within a frame or two of it being saved, so saving it earlier and
                // then building loses a race and the build stops for a scene that was clean a moment ago.
                //
                // Only ever OUR scene. A scene belonging to anyone else is reported, never saved, however
                // inconvenient that is — which is the whole reason this stopped rather than proceeding the first
                // time it ran.
                SaveOurOwnScene();

                string unsaved = UnsavedScenes();
                if (unsaved != null) {
                    File.AppendAllText(StatusPath,
                        "CANNOT BUILD YET: a scene has unsaved changes, and the addressable-content step refuses to\n" +
                        "run until every scene is saved. Save or discard it, then run this again.\n" +
                        "unsaved: " + unsaved + "\nDONE\n");
                    Debug.LogWarning("[Zounds] Build check stopped before building: unsaved scene(s) " + unsaved +
                                     ". Save or discard, then run the build check again.");
                    return;
                }

                File.AppendAllText(StatusPath, "creating the scene\n");
                EnsureScene();

                File.AppendAllText(StatusPath, "building\n");
                var options = new BuildPlayerOptions {
                    scenes = new[] { ScenePath },
                    locationPathName = ExePath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    // A development build so the log is readable and the report is easy to find. It does not
                    // affect whether the audio chain is compiled, which is what this exists to find out.
                    options = BuildOptions.Development,
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                File.AppendAllText(StatusPath,
                    "result: " + summary.result + "\n" +
                    "errors: " + summary.totalErrors + "   warnings: " + summary.totalWarnings + "\n" +
                    "took: " + summary.totalTime + "\n" +
                    "output: " + summary.outputPath + "\n" +
                    "size: " + summary.totalSize + " bytes\n" +
                    "DONE\n");
            }
            catch (Exception e) {
                File.AppendAllText(StatusPath, "FAILED with an exception: " + e.GetType().Name + ": " + e.Message + "\nDONE\n");
            }
        }

        /// <summary>Saves the check scene if it is open and modified. Never touches any other scene.</summary>
        private static void SaveOurOwnScene() {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty && s.path == ScenePath) EditorSceneManager.SaveScene(s);
            }
        }

        /// <summary>Names the scenes with unsaved changes, or null when there are none.</summary>
        private static string UnsavedScenes() {
            string names = null;
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;
                string label = string.IsNullOrEmpty(s.path) ? "(a scene that has never been saved)" : s.path;
                names = names == null ? label : names + ", " + label;
            }
            return names;
        }

        /// <summary>
        /// Creates the check scene without touching whatever scene is already open. If it already exists on disk,
        /// it is left exactly as it is, so someone's edits to it are not silently reverted by a rebuild.
        /// </summary>
        public static void EnsureScene() {
            if (File.Exists(ScenePath)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var previouslyActive = SceneManager.GetActiveScene();

            // Additive: every scene already open stays open and untouched.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            // Made active only while the objects are created, so they land in the new scene rather than in
            // whichever scene happened to be open.
            SceneManager.SetActiveScene(scene);
            try {
                var cameraObject = new GameObject("Camera and listener");
                cameraObject.AddComponent<Camera>();
                // Created explicitly. Without a listener there is perfect silence and no error of any kind, which
                // has already cost a session in this codebase.
                cameraObject.AddComponent<AudioListener>();
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);

                var soundObject = new GameObject("Zounds check");
                var source = soundObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                soundObject.AddComponent<ZoundSapVoiceGenerator>();
                soundObject.AddComponent<ZoundsSapCheck>();
            }
            finally {
                if (previouslyActive.IsValid()) SceneManager.SetActiveScene(previouslyActive);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
        }
    }
}
