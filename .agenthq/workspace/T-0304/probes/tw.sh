#!/bin/sh
# usage: tw.sh <tweak> [rebuild]
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0304/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
"$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T0304.tweak\",\"$1\"); UnityEditor.EditorPrefs.SetString(\"T0304.unit\",\"4\"); UnityEditor.EditorPrefs.SetString(\"T0304.node\",\"\"); UnityEditor.EditorPrefs.SetString(\"T0304.cmask\",\"\"); UnityEditor.EditorPrefs.SetString(\"T0304.mask\",\"\"); UnityEditor.EditorPrefs.SetString(\"T0304.reset\",\"0\"); return \"ok\";" >/dev/null 2>&1
if [ "$2" = "rebuild" ]; then sh "$D/run.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0303/probes/lay-rebuild.cs" >/dev/null; fi
sh "$D/run.sh" "$D/p8-tweak.cs" | tail -4
python -c "import time;time.sleep(4)"
sh "$D/run.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0303/probes/lay-count.cs"
