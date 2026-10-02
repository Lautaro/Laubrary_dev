#!/bin/sh
# T-0318 — open any Laubrary window at a width, fullest state, then run the four audits.
# usage: sh xsweep.sh <WindowType> <MenuItem> <width> <bind> <tag>
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0318/probes"
T3="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
"$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T313.win\",\"$1\"); UnityEditor.EditorPrefs.SetString(\"T313.menu\",\"$2\"); UnityEditor.EditorPrefs.SetString(\"T313.width\",\"$3\"); UnityEditor.EditorPrefs.SetString(\"T313.bind\",\"$4\"); UnityEditor.EditorPrefs.SetString(\"T0312.tag\",\"$5\"); return \"ok\";" >/dev/null 2>&1
echo "########## $1 @ $3  bind=$4  tag=$5"
sh "$D/zrun.sh" "$T3/x-open.cs"
python -c "import time;time.sleep(1.4)"
sh "$D/zrun.sh" "$T3/x-audit.cs"
