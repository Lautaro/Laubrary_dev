#!/bin/sh
# T-0312 probe runner.  usage:  sh zrun.sh <probe.cs>
# Concatenates zlib.cs (the shared hierarchy-walking library) with <probe.cs> and evals the pair in the
# Shaper-worktree editor.  A probe may use every Z* delegate zlib.cs defines and must end with a return.
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0318/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
T="$D/.combined.cs"
cat "$D/zlib.cs" "$1" > "$T"
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
