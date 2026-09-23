using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Laubrary.VisionMask.Editor
{
    /// Builds the VisionMask demo scene in code (authoring rule 8): a camera and one object carrying the
    /// demo component, which grows the room, walls, flashlight and monsters from generated textures at Play.
    /// The demo component lives in the host project's demo assembly, so it is found by name.
    public static class VisionMaskDemoBuilder
    {
        public const string ScenePath = "Assets/Demos/VisionMaskDemo/VisionMaskDemo.unity";
        const string DemoType = "Laubrary.Demos.VisionMaskDemo";

        [MenuItem("Laubrary/VisionMask/Build Demo Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene(NewSceneMode.Single);
        }

        /// Builds and saves the scene. `Additive` leaves whatever is open untouched and closes the new scene
        /// again once saved (for building it from a session without disturbing the user's open scene).
        public static string BuildScene(NewSceneMode mode)
        {
            var type = FindType(DemoType);
            if (type == null)
            {
                Debug.LogError($"VisionMask demo: type '{DemoType}' not found — this project has no VisionMask demo script.");
                return null;
            }

            var previousActive = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            EditorSceneManager.SetActiveScene(scene);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            new GameObject("VisionMask Demo").AddComponent(type);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (mode == NewSceneMode.Additive)
            {
                if (previousActive.IsValid()) EditorSceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log("VisionMask demo scene built at " + ScenePath + " — open it and press Play.");
            return ScenePath;
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
