#!/bin/sh
W="D:/UNITY/Laubrary Dev/.agenthq/workspace"
R="$W/T-0307/probes/run.sh"
for f in "$@"; do
  echo "=========== $f"
  sh "$R" "$f" 2>&1 | tail -40
done
