var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.Button fit=null;
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Fit") fit=b; }
if (fit==null) return "no Fit";
ZClick(fit);
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre7.png");
win.Repaint();
return "fit pressed";
