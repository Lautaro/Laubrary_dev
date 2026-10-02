#!/bin/sh
# T-0313 probe runner — identical to T-0312's, except that it concatenates T-0312's zlib.cs (the ONE
# shared hierarchy-walking library; this task writes no second walker) with a probe from EITHER folder.
# usage:  sh zrun.sh <probe.cs>
L="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/zlib.cs"
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
T="$D/.combined.cs"
cat "$L" "$1" > "$T"
"$U" --json command eval_file --project-path "$P" "$T" 2>&1 | python -c "
import sys,json
raw=sys.stdin.read()
try: d=json.loads(raw)
except Exception: print(raw[:4000]); sys.exit()
if not d.get('success'):
    for e in d.get('errors') or []: print('ERR', e.get('message'))
    sys.exit()
def dig(o):
    if isinstance(o,dict):
        if isinstance(o.get('result'),str): print(o['result']); return True
        for v in o.values():
            if dig(v): return True
    if isinstance(o,list):
        for v in o:
            if dig(v): return True
    return False
if not dig(d): print(json.dumps(d)[:4000])
"
