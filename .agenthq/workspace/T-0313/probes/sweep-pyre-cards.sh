#!/bin/sh
# T-0313 — Pyre's every card at 1 / 2 / 4 ZuiColumnFlow columns.
# usage: sh sweep-pyre-cards.sh "<space separated form names or -:Shape>"
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
COLS="${2:-1 2 4}"
for F in $1; do
  case "$F" in
    -:*) FORM="-"; SHAPE="${F#-:}" ;;
    *)   FORM="$F"; SHAPE="Disc" ;;
  esac
  for C in $COLS; do
    case $C in
      1) PANE=360;  WINW=1000 ;;
      2) PANE=760;  WINW=1400 ;;
      4) PANE=1458; WINW=1900 ;;
    esac
    "$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T313.form\",\"$FORM\"); UnityEditor.EditorPrefs.SetString(\"T313.shape\",\"$SHAPE\"); UnityEditor.EditorPrefs.SetString(\"T313.pane\",\"$PANE\"); UnityEditor.EditorPrefs.SetString(\"T313.winw\",\"$WINW\"); UnityEditor.EditorPrefs.SetString(\"T0312.unit\",\"pyre\"); UnityEditor.EditorPrefs.SetString(\"T0312.tag\",\"pyre13-$FORM$SHAPE-c$C\"); return \"ok\";" >/dev/null 2>&1
    echo "########## $FORM $SHAPE  pane $PANE  (target $C columns)"
    sh "$D/zrun.sh" "$D/p-set.cs"
    python -c "import time;time.sleep(1.2)"
    sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/probes/a-audit.cs"
  done
done
