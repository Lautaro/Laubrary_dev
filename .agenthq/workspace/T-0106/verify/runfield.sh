#!/bin/sh
# Run ShaperFieldAudit one V-test per eval_file call. Some legs take longer than the pipeline's fixed
# 30 s reply timeout, but the editor finishes them anyway and each leg appends its own report, so the
# gate is "the RESULT count reached N", not "the CLI returned".
cd "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0106/verify" || exit 1
OUT="D:\\UNITY\\Laubrary Dev\\.agenthq\\workspace\\T-0106\\verify\\FIELD-AUDIT.txt"
rm -f FIELD-AUDIT.txt
i=0
for m in V1_SweepIdentity V2_ShellIdentity V3_SoftCombineIdentities V4_DeclaredBounds \
         V5_RatioCrossCheck V6_Anisotropy V7_LandmarkTrap V8_TileIndependence \
         V9_ShelledInsideness V10_DegenerateInputs V11_EllipseAgainstOracle \
         V12_NGonOuterExtent V13_InstrumentSelfTest; do
  i=$((i+1))
  sh mk.sh "Laubrary.Shaper.Editor.ShaperFieldAudit" "$OUT" "$m" > "fv.cs"
  sh run.sh "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0106/verify/fv.cs" 120 > /dev/null 2>&1
  j=0
  while [ "$j" -lt 150 ]; do
    c=$(grep -c "RESULT" FIELD-AUDIT.txt 2>/dev/null || echo 0)
    [ "$c" -ge "$i" ] && break
    sleep 2
    j=$((j+1))
  done
  echo "$i $m -> RESULT lines now $(grep -c 'RESULT' FIELD-AUDIT.txt 2>/dev/null || echo 0)"
done
echo "PASS=$(grep -c 'RESULT: PASS' FIELD-AUDIT.txt)  FAIL=$(grep -c 'RESULT: FAIL' FIELD-AUDIT.txt)"
