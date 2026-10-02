#!/bin/sh
# pr.sh <button text> [nth] — set the press target then press it (retrying once after a scroll)
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0334/probes"
cat > "$D/.pset.cs" <<EOT
UnityEditor.EditorPrefs.SetString("T334.pressText", "$1");
UnityEditor.EditorPrefs.SetInt("T334.pressNth", ${2:-0});
return "set";
EOT
timeout 90 sh "$D/zrun.sh" "$D/.pset.cs" > /dev/null
OUT=$(timeout 90 sh "$D/zrun.sh" "$D/s6-press.cs")
echo "$OUT"
case "$OUT" in *"rerun"*) timeout 90 sh "$D/zrun.sh" "$D/s6-press.cs";; esac
