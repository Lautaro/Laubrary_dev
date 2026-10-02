#!/bin/sh
# usage: sh setcrop.sh <src> <dst> <x,y,w,h> <scale>
cat > .crop-set.cs <<CS
UnityEditor.EditorPrefs.SetString("T321.src", "$1");
UnityEditor.EditorPrefs.SetString("T321.dst", "$2");
UnityEditor.EditorPrefs.SetString("T321.rect", "$3");
UnityEditor.EditorPrefs.SetFloat("T321.scale", $4f);
return "ok";
CS
sh zrun.sh .crop-set.cs >/dev/null
sh zrun.sh crop.cs
