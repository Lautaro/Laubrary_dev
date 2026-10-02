int t0 = UnityEditor.EditorPrefs.GetInt("T334.tex0", -1);
int t1 = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Texture2D>().Length;
int tiny = 0;
foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Texture2D>())
  if (t != null && t.width == 2 && t.height == 2 && string.IsNullOrEmpty(t.name)) tiny++;
return "texturesBefore=" + t0 + " texturesNow=" + t1 + " delta=" + (t1 - t0) + " unnamed2x2=" + tiny;
