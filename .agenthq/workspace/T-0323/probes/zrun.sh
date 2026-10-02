#!/bin/sh
# T-0323 probe runner.  usage:  sh zrun.sh <probe.cs>
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0323/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
T="$D/.combined.cs"
cat "$D/zlib.cs" "$D/click.cs" "$D/zbind.cs" "$1" > "$T"
"$U" --json command eval_file --project-path "$P" "$T" 2>&1 | python -c "
import sys,json
raw=sys.stdin.read()
try: d=json.loads(raw)
except Exception: print(raw[:6000]); sys.exit()
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
if not dig(d): print(json.dumps(d)[:6000])
"
