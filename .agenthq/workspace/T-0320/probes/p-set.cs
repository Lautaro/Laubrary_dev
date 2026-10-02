foreach (var kv in UnityEditor.EditorPrefs.GetString("T320.args","").Split(';')) {
  if (kv.Length == 0) continue; var i = kv.IndexOf('='); if (i<0) continue;
  UnityEditor.EditorPrefs.SetString(kv.Substring(0,i), kv.Substring(i+1));
}
return "ok";
