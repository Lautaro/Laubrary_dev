#!/bin/sh
# T-0304's layout-struggle sweep, re-run for T-0312's edits.
# usage: sh sweep-idle.sh <unit> <paneFrom> <paneTo> <step> <winw> <idleSeconds>
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0318/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
w=$2
while [ "$w" -le "$3" ]; do
  "$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T0312.unit\",\"$1\"); UnityEditor.EditorPrefs.SetString(\"T0312.pane\",\"$w\"); UnityEditor.EditorPrefs.SetString(\"T0312.winw\",\"$5\"); return \"ok\";" >/dev/null 2>&1
  R=`sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/w-idle.cs"`
  python -c "import time;time.sleep($6)"
  C=`sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/w-count.cs"`
  echo "$1 pane=$w | $C"
  w=`expr $w + $4`
done
