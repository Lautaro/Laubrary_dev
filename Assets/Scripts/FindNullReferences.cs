#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class FindNullReferences : EditorWindow
{
    [MenuItem("Laubrary Dev/Find Assets With Null References")]
    static void FindAssets()
    {
        string[] allAssets = AssetDatabase.GetAllAssetPaths();
        int foundCount = 0;

        Debug.Log("=== Starting scan for null references ===");

        foreach (string assetPath in allAssets)
        {
            if (!assetPath.StartsWith("Assets/")) continue;
            if (assetPath.EndsWith(".cs")) continue;
            if (assetPath.EndsWith(".meta")) continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null)
            {
                Component[] components = prefab.GetComponentsInChildren<Component>(true);
                foreach (Component comp in components)
                {
                    if (comp == null)
                    {
                        Debug.LogError($"❌ Missing component on: {assetPath}", prefab);
                        foundCount++;
                        break;
                    }

                    SerializedObject so = new SerializedObject(comp);
                    SerializedProperty prop = so.GetIterator();

                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            if (prop.objectReferenceValue == null && prop.objectReferenceInstanceIDValue != 0)
                            {
                                Debug.LogError($"❌ Null reference in {assetPath} on {comp.GetType().Name}.{prop.name}", prefab);
                                foundCount++;
                            }
                        }
                    }
                }
            }
        }

        Debug.Log($"=== Scan complete. Found {foundCount} assets with null references ===");
    }
}
#endif
