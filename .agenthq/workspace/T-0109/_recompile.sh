#!/bin/bash
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
W="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0109"
"$U" command --project-path "$P" eval_file --file "$W/_refresh109.cs" >/dev/null 2>&1
for i in $(seq 1 60); do
  out=$("$U" command --project-path "$P" eval_file --file "$W/_state.cs" 2>&1 | grep -o 'compiling=[A-Za-z]*, failed=[A-Za-z]*')
  if [ -n "$out" ]; then
    case "$out" in *"compiling=False"*) echo "$out"; exit 0;; esac
  fi
done
echo "TIMEOUT waiting for compile"
exit 1
