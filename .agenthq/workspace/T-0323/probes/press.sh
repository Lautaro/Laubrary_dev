#!/bin/sh
# usage: sh press.sh <Window> <ButtonText> <nth> <assetPath>
cat > .press-set.cs <<EOT
UnityEditor.EditorPrefs.SetString("T323.pressWin","$1");
UnityEditor.EditorPrefs.SetString("T323.pressText","$2");
UnityEditor.EditorPrefs.SetInt("T323.pressNth",${3:-0});
UnityEditor.EditorPrefs.SetString("T323.pressAsset","$4");
return "ok";
EOT
sh zrun.sh .press-set.cs > /dev/null
cat g0-lib.cs g1-press.cs > .press-run.cs
sh zrun.sh .press-run.cs
cat g0-lib.cs g2-after.cs > .press-after.cs
sh zrun.sh .press-after.cs
