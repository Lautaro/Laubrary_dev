#!/bin/sh
# cap.sh <WindowTypeName> <out.png>  — capture one editor window by its logical position, via PrintWindow.
# A background editor's floating tool windows carry an EMPTY OS title, so they cannot be found by name;
# their physical origin is  2.25*logical - (5, 45)  in this editor (pixelsPerPoint 2.25, measured).
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0336"
POS=$(PYTHONIOENCODING=utf-8 python -c "
import subprocess,json,sys
code='var sb=new System.Text.StringBuilder(); foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w.GetType().Name==\"$1\") sb.Append(w.position.x).Append(\" \").Append(w.position.y); return sb.ToString();'
open(r'$D/probes/.pos.cs','w',encoding='utf-8').write(code)
o=subprocess.run(['C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe','--json','command','eval_file','--project-path','D:/UNITY/Laubrary Dev - Shaper',r'$D/probes/.pos.cs'],capture_output=True,text=True).stdout
d=json.loads(o)
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
r=dig(d)
x,y=r.split()
print(int(round(2.25*float(x)))-5, int(round(2.25*float(y)))-45)
")
L=$(echo $POS | cut -d' ' -f1); T=$(echo $POS | cut -d' ' -f2)
powershell -NoProfile -ExecutionPolicy Bypass -File "$D/probes/capprint.ps1" -Title "$1" -Out "$D/$2" -ProcId 184580 -Left $L -Top $T 2>&1 | grep -E "PrintWindow|NOT FOUND"
