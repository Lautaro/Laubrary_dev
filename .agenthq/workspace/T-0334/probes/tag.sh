#!/bin/sh
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0334/probes"
cat > "$D/.tset.cs" <<EOT
UnityEditor.EditorPrefs.SetString("T334.tag", "$1");
return "tag=$1";
EOT
timeout 90 sh "$D/zrun.sh" "$D/.tset.cs"
