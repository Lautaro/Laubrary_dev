#!/bin/sh
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0334/probes"
cat > "$D/.zset.cs" <<EOT
UnityEditor.EditorPrefs.SetFloat("T334.w", $1f);
UnityEditor.EditorPrefs.SetFloat("T334.h", $2f);
return "size $1x$2";
EOT
timeout 90 sh "$D/zrun.sh" "$D/.zset.cs" >/dev/null
timeout 90 sh "$D/zrun.sh" "$D/s22-size.cs"
