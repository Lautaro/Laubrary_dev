#!/bin/sh
# T-0313 — one cold walk of Shaper with temporal samples: close, open from the menu, sample at t0/t1/t4,
# bind the demo document, sample again at t0/t1/t4.  usage: sh cold-walk.sh <walk-number>
D="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0318/probes"
U="C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe"
P="D:/UNITY/Laubrary Dev - Shaper"
set_step() { "$U" command eval --project-path "$P" "UnityEditor.EditorPrefs.SetString(\"T313.step\",\"$1\"); UnityEditor.EditorPrefs.SetString(\"T313.t\",\"$2\"); return \"ok\";" >/dev/null 2>&1; }
echo "===== COLD WALK $1 ====="
set_step close 0;  sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes/c-cold.cs"
set_step open 0;   sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes/c-cold.cs"
python -c "import time;time.sleep(1)"
set_step sample 1; sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes/c-cold.cs"
python -c "import time;time.sleep(3)"
set_step sample 4; sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes/c-cold.cs"
set_step bind 0;   sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes/c-cold.cs"
python -c "import time;time.sleep(1)"
set_step sample 1; sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes/c-cold.cs"
python -c "import time;time.sleep(3)"
set_step sample 4; sh "$D/zrun.sh" "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0313/probes/c-cold.cs"
