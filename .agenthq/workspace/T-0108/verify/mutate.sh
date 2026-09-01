#!/usr/bin/env bash
# Apply a mutation, recompile, run one audit leg, print its RESULT, then revert.
SH_DIR="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108"
UNITY="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
PROJ="D:\UNITY\Laubrary Dev - Shaper"
wait_compile() {
  "$UNITY" command --project-path "$PROJ" eval_file "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\_mark.cs" >/dev/null 2>&1
  for i in $(seq 1 30); do
    out=$("$UNITY" command --project-path "$PROJ" eval_file "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\_tail.cs" 2>&1)
    if echo "$out" | grep -q 'compiling=False'; then
      echo "$out" | grep -o 'failed=[A-Za-z]*' | head -1
      echo "$out" | grep -o 'error CS[0-9]*[^\]*' | head -3
      return
    fi
  done
  echo "TIMEOUT"
}
run_leg() {
  echo "$1" > "$SH_DIR/verify/leg.txt"
  rm -f "$SH_DIR/MUT.txt"
  cp /dev/null "$SH_DIR/LIGHT-AUDIT.tmp" 2>/dev/null
  MUTOUT="$SH_DIR/MUT.txt"
  sed "s#LIGHT-AUDIT.txt#MUT.txt#" "$SH_DIR/verify/_run.cs" > "$SH_DIR/verify/_runmut.cs"
  "$UNITY" command --project-path "$PROJ" eval_file "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\_runmut.cs" >/dev/null 2>&1
  grep -E "RESULT:|FAIL" "$MUTOUT" | head -8
}
