using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Dev-host convenience: builds a runnable Pyre demo scene (dark camera + a spawner root). Not shipped with the
/// package. Guard rail: only creates its own folder/scene, never deletes other user assets.
public static class PyreDemoBuilder
{
    const string Dir = "Assets/Demos/PyreDemo";

    [MenuItem("Laubrary/Pyre/Build Demo Scene")]
    public static void BuildDemoScene()
    {
        EnsureDir();

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);

        var root = new GameObject("Pyre Demo");
        root.AddComponent<PyreDemoSpawner>();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Dir + "/PyreDemo.unity");
        Debug.Log($"[Pyre] Built demo scene at {Dir}/PyreDemo.unity — press Play, then click to spawn explosions.");
    }

    static void EnsureDir()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Demos")) AssetDatabase.CreateFolder("Assets", "Demos");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Demos", "PyreDemo");
    }
}
