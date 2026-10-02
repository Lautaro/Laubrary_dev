#!/bin/sh
# T-0312 Shaper matrix: layer 0 and layer 1 of the demo document x 1 / 2 / 4 ZuiColumnFlow columns,
# every toggle-bar section on.  usage: sh sweep-shaper.sh <outfile-suffix>
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0318/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
S="$1"
for L in 0 1; do
  for C in 1 2 4; do
    case $C in
      1) PANE=400;  WINW=1000 ;;
      2) PANE=800;  WINW=1300 ;;
      # The 4th column needs the flow itself >= 4x360 = 1440px; at window 1700 the right pane's 320px
      # minimum clamps the flow to 1359 and only 3 columns appear, so the 4-column state needs ~1950.
      4) PANE=1500; WINW=1950 ;;
    esac
    "$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T0312.doc\",\"Assets/Demos/ShaperDemo/ShaperDemoDoc.asset\"); UnityEditor.EditorPrefs.SetInt(\"T0312.layer\",$L); UnityEditor.EditorPrefs.SetString(\"T0312.pane\",\"$PANE\"); UnityEditor.EditorPrefs.SetString(\"T0312.winw\",\"$WINW\"); UnityEditor.EditorPrefs.SetString(\"T0312.unit\",\"shaper\"); UnityEditor.EditorPrefs.SetString(\"T0312.tag\",\"shaper$S-L$L-c$C\"); return \"ok\";" >/dev/null 2>&1
    echo "########## layer $L  pane $PANE  (target $C columns)"
    sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/s-shaper.cs"
    python -c "import time;time.sleep(1.2)"
    sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/a-audit.cs"
  done
done
