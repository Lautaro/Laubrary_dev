#!/bin/sh
# usage: sh setpref.sh key value [type]   type: s(default)|i|f|b
cat > .pref-set.cs <<CS
UnityEditor.EditorPrefs.SetString("$1", "$2");
return "set $1=$2";
CS
sh zrun.sh .pref-set.cs
