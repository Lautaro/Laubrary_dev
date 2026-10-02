var w = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.Button play=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && b.text.Contains("Play")) play=b; }
if (play==null) return "no play";
ZClick(play);
UnityEditor.EditorPrefs.SetFloat("T337.t0", (float)UnityEditor.EditorApplication.timeSinceStartup);
return "pressed " + play.text + " t0=" + UnityEditor.EditorApplication.timeSinceStartup.ToString("F2");
