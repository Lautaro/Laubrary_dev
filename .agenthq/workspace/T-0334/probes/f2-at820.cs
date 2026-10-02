foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
  if (w == null) continue;
  var tn = w.GetType().Name;
  if (tn.StartsWith("Unity") || tn == "InspectorWindow" || tn == "ConsoleWindow" || tn == "ProjectBrowser"
      || tn == "SceneHierarchyWindow" || tn == "SceneView" || tn == "GameView" || tn == "MainToolbarWindow") continue;
  bool isAsset = false;
  for (var b = w.GetType().BaseType; b != null; b = b.BaseType)
    if (b.IsGenericType && b.GetGenericTypeDefinition().Name.StartsWith("ZuiAssetWindow")) { isAsset = true; break; }
  if (!isAsset) continue;
  w.position = new UnityEngine.Rect(30, 30, 820, 520);
}
return "all asset windows set to 820x520";
