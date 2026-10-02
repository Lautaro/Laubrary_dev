#!/bin/sh
# T-0312 Pyre matrix: the scratch spec's Disc layer (0) and Torch layer (1) at two dial-pane widths.
# usage: sh sweep-pyre.sh <outfile-suffix>
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
S="$1"
for L in 0 1; do
  for PANE in 400 900; do
    "$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetInt(\"T0312.layer\",$L); UnityEditor.EditorPrefs.SetString(\"T0312.pane\",\"$PANE\"); UnityEditor.EditorPrefs.SetString(\"T0312.winw\",\"1700\"); UnityEditor.EditorPrefs.SetString(\"T0312.unit\",\"pyre\"); UnityEditor.EditorPrefs.SetString(\"T0312.tag\",\"pyre$S-L$L-p$PANE\"); return \"ok\";" >/dev/null 2>&1
    echo "########## pyre layer $L  pane $PANE"
    sh "$D/zrun.sh" "$D/s-pyre.cs"
    python -c "import time;time.sleep(1.2)"
    sh "$D/zrun.sh" "$D/a-audit.cs"
  done
done
