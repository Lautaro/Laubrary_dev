U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"; P="D:/UNITY/Laubrary Dev - Shaper"; V="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify"
for i in $(seq 1 40); do
  r=$("$U" --no-banner command eval_file --project-path "$P" --file "$V/_run_FIELD.cs" 2>&1 | grep -o '"result":"[^"]*"')
  echo "FIELD $i: $r"
  case "$r" in *"ran 0"*) echo "DONE"; break;; esac
done
echo "FAIL tokens: $(grep -c FAIL "$V/FIELD-AUDIT.txt")"
