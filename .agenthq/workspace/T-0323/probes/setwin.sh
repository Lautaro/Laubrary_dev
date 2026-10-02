#!/bin/sh
cat > .setwin.cs <<EOT
UnityEditor.EditorPrefs.SetString("T323.auditWin","$1");
return "set $1";
EOT
sh zrun.sh .setwin.cs > /dev/null
sh zrun.sh c2-audit.cs
