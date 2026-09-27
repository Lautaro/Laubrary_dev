using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks.EditorTools {

    /// <summary>
    /// Builds the listening-and-stutter check scene on demand, from a menu item.
    ///
    /// **Why this is a menu item rather than something already done.** Creating a scene replaces whatever scene
    /// is currently open, and this editor was found with an unsaved scene open that belonged to a DIFFERENT
    /// project on disk. Creating the check scene automatically would have quietly discarded someone's unsaved
    /// work, or written into another project's file. Going through a menu item means the editor's own
    /// save-or-discard prompt appears first and the person decides, which is the only safe way to do this.
    ///
    /// Adding the scene to the build list is asked about separately, because the build list is a shared project
    /// setting and changing it silently would change what everyone else's builds launch.
    /// </summary>
    public static class ZoundsSapCheckSceneBuilder {

        private const string ScenePath = "Assets/ZoundsSapCheck/ZoundsSapCheck.unity";

        [MenuItem("Laubrary/Zounds/Create the native audio check scene")]
        public static void CreateScene() {
            // The editor's own prompt, deliberately. If the person cancels, nothing happens at all.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // A camera carrying the listener. Without a listener there is perfect silence and no error of any
            // kind, which has already cost a session in this codebase, so it is created explicitly rather than
            // left to a default scene template that may or may not include one.
            var cameraObject = new GameObject("Camera and listener");
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var soundObject = new GameObject("Zounds check");
            var source = soundObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            soundObject.AddComponent<ZoundSapVoiceGenerator>();
            soundObject.AddComponent<ZoundsSapCheck>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("[Zounds] Check scene created at " + ScenePath +
                      ". Press Play and listen; press 2 to force a collection while it plays.");

            OfferBuildListAndBufferChanges();
        }

        /// <summary>
        /// Offers the two shared project settings a meaningful BUILD test needs, one at a time, because each one
        /// changes behaviour for everyone and neither should happen without being asked for.
        /// </summary>
        private static void OfferBuildListAndBufferChanges() {
            bool alreadyFirst = EditorBuildSettings.scenes.Length > 0
                                && EditorBuildSettings.scenes[0].enabled
                                && EditorBuildSettings.scenes[0].path == ScenePath;

            if (!alreadyFirst && EditorUtility.DisplayDialog(
                    "Put the check scene first in the build list?",
                    "A build launches the first enabled scene in the list. Right now that is something else, so a " +
                    "build made today would not launch this check.\n\n" +
                    "This adds the check scene at the front and leaves every other entry alone. The build list is " +
                    "shared project configuration, so it is worth undoing afterwards.",
                    "Put it first", "Leave the build list alone")) {

                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                scenes.RemoveAll(s => s.path == ScenePath);
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[Zounds] Check scene placed first in the build list.");
            }

            AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
            if (bufferLength >= 1024) {
                EditorUtility.DisplayDialog(
                    "The audio buffer is large",
                    "The audio buffer is currently " + bufferLength + " frames, which is the most forgiving setting " +
                    "there is. A large buffer hides exactly the problem this check is looking for, because it gives " +
                    "the audio thread a long head start before a pause becomes audible.\n\n" +
                    "Set it smaller in Project Settings under Audio (Best Latency, or a DSP buffer of 256) before " +
                    "trusting a clean result. It is left alone here because it affects the whole project.",
                    "Understood");
            }
        }
    }
}
