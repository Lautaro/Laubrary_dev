#!/usr/bin/env bash
# runlegs.sh <outfile> <leg> [<leg> ...]  — recompile, wait, run the named legs, print RESULT lines.
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:\UNITY\Laubrary Dev - Shaper"
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/fix"
OUT="$1"; shift
LEGS="$*"

"$U" command --project-path "$P" eval_file "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\_recomp.cs" >/dev/null 2>&1

for i in $(seq 1 40); do
  r=$("$U" command --project-path "$P" eval_file "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\_wait.cs" 2>&1 | grep -o '"result":"[^"]*"')
  case "$r" in *"compiling=False"*) echo "COMPILE $r"; break;; esac
done
case "$r" in *"failed=True"*) echo "COMPILE FAILED - aborting"; exit 1;; esac

printf '%s\n' $LEGS > "$D/legs.txt"
rm -f "$D/$OUT"
cat > "$D/_runsel.cs" <<EOF
string outPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\\$OUT";
string legFile = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\legs.txt";
System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperLightAudit"); if (x != null) { t = x; break; } }
if (t == null) return "AUDIT TYPE MISSING";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(outPath))
  foreach (var l in System.IO.File.ReadAllLines(outPath)) if (l.StartsWith("@@LEG ")) done.Add(l.Substring(6).Trim());
var sw = System.Diagnostics.Stopwatch.StartNew(); int ran = 0;
foreach (var raw in System.IO.File.ReadAllLines(legFile)) {
  string nm = raw.Trim(); if (nm.Length == 0 || done.Contains(nm)) continue;
  if (sw.ElapsedMilliseconds > 540000) return "partial ran=" + ran + " - call again";
  var m = t.GetMethod(nm);
  string r;
  if (m == null) r = nm + "\n  RESULT: FAIL NO SUCH METHOD";
  else { try { r = (string)m.Invoke(null, null); } catch (System.Exception e) { r = nm + "\n  RESULT: FAIL EXCEPTION " + e.GetBaseException().Message; } }
  System.IO.File.AppendAllText(outPath, "@@LEG " + nm + "\n" + r + "\n\n");
  ran++;
}
return "complete ran=" + ran;
EOF

for i in $(seq 1 6); do
  res=$("$U" command --project-path "$P" eval_file "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\_runsel.cs" 2>&1 | grep -o '"result":"[^"]*"')
  echo "RUN $res"
  case "$res" in *complete*) break;; esac
done

echo "--- verdicts ---"
grep "^@@LEG\|  RESULT:" "$D/$OUT" | paste - -
echo "PASS=$(grep -c 'RESULT: PASS' "$D/$OUT")  RESULTFAIL=$(grep -c 'RESULT: FAIL' "$D/$OUT")  FAILTOK=$(grep -o 'FAIL' "$D/$OUT" | wc -l)"
