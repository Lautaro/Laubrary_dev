// generic pref setter: T336.set = "key=value;key=value"
string spec = UnityEditor.EditorPrefs.GetString("T336.spec", "");
var sb = new System.Text.StringBuilder();
foreach (var part in spec.Split(';')) {
  if (part.Length == 0) continue;
  int i = part.IndexOf('=');
  if (i < 0) continue;
  UnityEditor.EditorPrefs.SetString(part.Substring(0, i), part.Substring(i + 1));
  sb.Append(part).Append(" | ");
}
return sb.ToString();
