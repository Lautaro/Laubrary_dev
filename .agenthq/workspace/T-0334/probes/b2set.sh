#!/bin/sh
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0334/probes"
cat > "$D/.setfocus.cs" <<EOT
UnityEditor.EditorPrefs.SetString("T334.focus", "$1");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "$1") { w.Focus(); w.Repaint(); return "focused $1 pos=" + w.position; }
return "NOT FOUND $1";
EOT
"C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe" --json command eval_file --project-path "D:/UNITY/Laubrary Dev - Shaper" "$D/.setfocus.cs" 2>&1 | python -c "
import sys,json
d=json.loads(sys.stdin.read())
def dig(o):
    if isinstance(o,dict):
        if isinstance(o.get('result'),str): return o['result']
        for v in o.values():
            r=dig(v)
            if r is not None: return r
    if isinstance(o,list):
        for v in o:
            r=dig(v)
            if r is not None: return r
    return None
print(dig(d) or json.dumps(d)[:1500])
"
