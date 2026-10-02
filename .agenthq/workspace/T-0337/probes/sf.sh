#!/bin/sh
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0337/probes"
printf 'UnityEditor.EditorPrefs.SetString("T337.form", "%s"); return "form=" + UnityEditor.EditorPrefs.GetString("T337.form","");\n' "$1" > "$D/.setform.cs"
"C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe" --json command eval_file --project-path "D:/UNITY/Laubrary Dev - Shaper" "$D/.setform.cs" 2>&1 | PYTHONIOENCODING=utf-8 python -c "
import sys,json
d=json.load(sys.stdin)
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
print(dig(d) or json.dumps(d)[:800])
"
