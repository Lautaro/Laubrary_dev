#!/bin/sh
# usage: sh docrop.sh <src> <dst> <x,y,w,h> [scale]
SC=${4:-1}
cat > .crop-run.cs <<EOT
UnityEditor.EditorPrefs.SetString("T321.src","$1");
UnityEditor.EditorPrefs.SetString("T321.dst","$2");
UnityEditor.EditorPrefs.SetString("T321.rect","$3");
UnityEditor.EditorPrefs.SetFloat("T321.scale",${SC}f);
EOT
cat crop.cs >> .crop-run.cs
sh zrun.sh .crop-run.cs
