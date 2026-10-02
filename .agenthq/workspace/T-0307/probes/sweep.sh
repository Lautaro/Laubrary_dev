#!/bin/sh
# usage: sweep.sh <width-setter.cs> <from> <to> <step> <idle-seconds> <outfile>
W="D:/UNITY/Laubrary Dev/.agenthq/workspace"
D="$W/T-0307/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
w=$2
: > "$6"
while [ "$w" -le "$3" ]; do
  "$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T0304.w\",\"$w\"); return \"ok\";" >/dev/null 2>&1
  R=`sh "$D/run.sh" "$1"`
  python -c "import time;time.sleep($5)"
  C=`sh "$D/run.sh" "$W/T-0303/probes/lay-count.cs"`
  echo "$w | $R | $C" >> "$6"
  w=`expr $w + $4`
done
echo DONE >> "$6"
