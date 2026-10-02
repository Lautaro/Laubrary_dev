#!/bin/sh
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0334/probes"
cat > "$D/.stp.cs" <<EOT
UnityEditor.EditorPrefs.SetString("T334.step", "$1");
return "step=$1";
EOT
timeout 90 sh "$D/zrun.sh" "$D/.stp.cs" >/dev/null
OUT=$(timeout 100 sh "$D/zrun.sh" "$D/s40-step.cs"); echo "$OUT"
case "$OUT" in *"rerun"*) timeout 100 sh "$D/zrun.sh" "$D/s40-step.cs";; esac
