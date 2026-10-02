#!/bin/sh
# usage: sh docap.sh <WindowTypeName> <outname> [w] [h] [scale]
W=${3:-820}; H=${4:-880}; SC=${5:-0.5}
S="D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0323/shots"
cat > .cap-run.cs <<EOT
var w = ZWin("$1"); if (w==null) return "no window $1";
w.position = new Rect(20,20,$W,$H);
w.titleContent = new GUIContent("$1");
UnityEditor.EditorPrefs.SetString("T320.capWin","$1");
UnityEditor.EditorPrefs.SetString("T320.capOut","$S/$2.png");
w.Focus(); w.Repaint();
return "ready " + w.position;
EOT
sh zrun.sh .cap-run.cs
sh zrun.sh cap2.cs
sh docrop.sh "$S/$2.png" "$S/$2-v.png" "0,0,$(( W*9/4 )),$(( H*9/4 ))" $SC
