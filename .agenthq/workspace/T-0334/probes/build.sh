#!/bin/sh
# build.sh <probe.cs> — concatenate the shared libs + probe into combined.cs for mcp eval_file
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0334/probes"
cat "$D/zlib.cs" "$D/click.cs" "$D/zbind.cs" "$D/g0-lib.cs" "$D/zpress.cs" "$1" > "$D/combined.cs"
echo "$D/combined.cs"
