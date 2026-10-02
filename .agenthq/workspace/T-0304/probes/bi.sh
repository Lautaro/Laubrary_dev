#!/bin/sh
# usage: bi.sh <unit> <node-path> <cmask>
# Rebuilds the Pyre window (restoring true state), then hides the named children and counts idle errors.
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0304/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
"$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T0304.unit\",\"$1\"); UnityEditor.EditorPrefs.SetString(\"T0304.node\",\"$2\"); UnityEditor.EditorPrefs.SetString(\"T0304.cmask\",\"$3\"); UnityEditor.EditorPrefs.SetString(\"T0304.reset\",\"0\"); UnityEditor.EditorPrefs.SetString(\"T0304.mask\",\"\"); return \"ok\";" >/dev/null 2>&1
sh "$D/run.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0303/probes/lay-rebuild.cs" >/dev/null
sh "$D/run.sh" "$D/p6-bisect.cs"
python -c "import time;time.sleep(4)"
sh "$D/run.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0303/probes/lay-count.cs"
