#!/bin/sh
cat > .pp-set.cs <<EOT
UnityEditor.EditorPrefs.SetString("T323.pressWin","$1");
UnityEditor.EditorPrefs.SetString("T323.pressText","$2");
UnityEditor.EditorPrefs.SetString("T323.pressAsset","$3");
return "ok";
EOT
sh zrun.sh .pp-set.cs > /dev/null
cat g0-lib.cs f7-poppress.cs > .pp-run.cs
sh zrun.sh .pp-run.cs
