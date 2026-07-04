using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Dev-host convenience: builds a runnable Larder demo scene — a dark store interior with rows of procedurally
/// generated Wares you can click to smash. Follows the ChoreoDemoBuilder pattern (empty scene → camera → root →
/// save). Not shipped with the package.
public static class LarderDemoBuilder
{
    const string Dir = "Assets/Demos/LarderDemo";

    [MenuItem("Laubrary/Larder/Build Demo Scene")]
    public static void BuildDemo()
    {
        EnsureDir();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera: orthographic, framed on the shelves, grimy dark store background.
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 3.2f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        camGo.AddComponent<AudioListener>();

        // Root with the shelf generator + the click shooter.
        var root = new GameObject("Larder Demo");
        root.AddComponent<LarderDemoShelf>();
        var shooter = root.AddComponent<LarderDemoClickShooter>();
        shooter.cam = cam;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Dir + "/LarderDemo.unity");
        Debug.Log($"[Larder] Built demo scene at {Dir}/LarderDemo.unity — press Play, then click the Wares to smash them.");
    }

    static void EnsureDir()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Demos")) AssetDatabase.CreateFolder("Assets", "Demos");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Demos", "LarderDemo");
    }
}
