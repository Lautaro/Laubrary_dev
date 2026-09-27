using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Laubrary.Zounds.Checks.EditorTools {

    /// <summary>
    /// Creates the listening check scene if it does not exist yet, then opens it, and offers the two shared
    /// project settings a meaningful BUILD test needs.
    ///
    /// The scene itself is created by the build check's own non-destructive routine, so there is exactly one piece
    /// of code that knows what the scene contains. Creating it never disturbs whatever scene is already open;
    /// OPENING it does, so the editor's own save-or-discard prompt is invited first and the person decides. That
    /// matters here specifically: this editor was found holding an unsaved scene belonging to a different project
    /// on disk, and replacing it silently would have discarded that work.
    /// </summary>
    public static class ZoundsSapCheckSceneBuilder {

        [MenuItem("Laubrary/Zounds/Create the native audio check scene")]
        public static void CreateAndOpenScene() {
            ZoundsSapBuildCheck.EnsureScene();

            // The editor's own prompt, deliberately. If the person cancels, the scene still exists on disk and
            // nothing has been opened or lost.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                Debug.Log("[Zounds] Check scene is at " + ZoundsSapBuildCheck.ScenePath +
                          ". It was not opened, because the currently open scene has unsaved changes.");
                return;
            }

            EditorSceneManager.OpenScene(ZoundsSapBuildCheck.ScenePath, OpenSceneMode.Single);
            Debug.Log("[Zounds] Press Play and listen. Press 2 to force a collection while it plays; the tone " +
                      "should continue unbroken. Press 3 to sweep a parameter live.");

            OfferBuildListAndBufferChanges();
        }

        /// <summary>
        /// Offers the two shared project settings a meaningful build test needs, because each changes behaviour for
        /// everyone and neither should happen without being asked for.
        /// </summary>
        private static void OfferBuildListAndBufferChanges() {
            bool alreadyFirst = EditorBuildSettings.scenes.Length > 0
                                && EditorBuildSettings.scenes[0].enabled
                                && EditorBuildSettings.scenes[0].path == ZoundsSapBuildCheck.ScenePath;

            if (!alreadyFirst && EditorUtility.DisplayDialog(
                    "Put the check scene first in the build list?",
                    "This is only needed if you build through the normal Build menu. The check's own build command " +
                    "under Laubrary > Zounds > Checks does NOT need it — it builds this one scene directly and " +
                    "leaves the build list alone.\n\n" +
                    "A build made from the list today would launch something unrelated, because the first enabled " +
                    "entry is another scene.",
                    "Put it first", "Leave the build list alone")) {

                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                scenes.RemoveAll(s => s.path == ZoundsSapBuildCheck.ScenePath);
                scenes.Insert(0, new EditorBuildSettingsScene(ZoundsSapBuildCheck.ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[Zounds] Check scene placed first in the build list.");
            }

            AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
            if (bufferLength >= 1024) {
                EditorUtility.DisplayDialog(
                    "The audio buffer is large",
                    "The audio buffer is currently " + bufferLength + " frames, which is the most forgiving setting " +
                    "there is. A large buffer hides exactly the problem this check looks for, because it gives the " +
                    "audio thread a long head start before a pause becomes audible.\n\n" +
                    "Set it smaller in Project Settings under Audio (Best Latency, or a DSP buffer of 256) before " +
                    "trusting a clean listening result. It is left alone here because it affects the whole project.",
                    "Understood");
            }
        }
    }
}
