#!/bin/sh
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0334/probes"
cat > "$D/.seth.cs" <<EOT
UnityEditor.EditorPrefs.SetInt("T334.h", $1);
UnityEditor.EditorPrefs.SetInt("T334.w", $2);
return "prefs $2 x $1";
EOT
sh "$D/zrun.sh" "$D/.seth.cs" > /dev/null
sh "$D/zrun.sh" "$D/f5-setsize.cs" > /dev/null
sh "$D/zrun.sh" "$D/f6-meas.cs"
