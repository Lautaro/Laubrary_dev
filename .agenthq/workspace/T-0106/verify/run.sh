#!/bin/sh
F="$1"; T="${2:-120}"
"C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe" --json command eval_file --project-path "D:/UNITY/Laubrary Dev - Shaper" --timeout "$T" "$F" 2>&1 \
 | python -c "
import sys,json
raw=sys.stdin.read()
try: d=json.loads(raw)
except Exception:
    print(raw); sys.exit(0)
def walk(o):
    if isinstance(o,dict):
        if 'result' in o and isinstance(o['result'],str): return o['result']
        for v in o.values():
            r=walk(v)
            if r: return r
    if isinstance(o,list):
        for v in o:
            r=walk(v)
            if r: return r
    return None
r=walk(d)
print(r if r else json.dumps(d)[:6000])
"
