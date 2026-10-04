#!/bin/sh
# Compile the GoreLab engine (Assets/.../GoreLab/Core) + the golden checker + this harness with Unity's bundled Roslyn, run on its .NET runtime.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$HERE/../../.."
U="/c/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data"
DOTNET="$U/NetCoreRuntime/dotnet.exe"
FW="$(ls -d "$U/NetCoreRuntime/shared/Microsoft.NETCore.App/"*/ | head -1)"
FWVER="$(basename "$FW")"
OUT="$HERE/bin"
mkdir -p "$OUT"
RSP="$OUT/build.rsp"
{
  echo "-nologo -nostdlib -optimize+ -langversion:9.0 -nowarn:1701,1702"
  echo "-out:\"$(cygpath -w "$OUT/GoreHarness.dll")\""
  for f in System.Runtime System.Collections System.Console System.Runtime.Extensions System.Private.CoreLib netstandard System.Linq; do
    echo "-r:\"$(cygpath -w "$FW$f.dll")\""
  done
  for f in "$PROJ"/Assets/Packages/Laubrary/Runtime/GoreLab/Core/*.cs "$PROJ/Assets/Tests/GoreLab/GoreGoldenChecker.cs" "$HERE/Program.cs"; do
    echo "\"$(cygpath -w "$f")\""
  done
} > "$RSP"
"$DOTNET" "$(cygpath -w "$U/DotNetSdkRoslyn/csc.dll")" "@$(cygpath -w "$RSP")"
cat > "$OUT/GoreHarness.runtimeconfig.json" <<EOF
{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "$FWVER" } } }
EOF
"$DOTNET" "$(cygpath -w "$OUT/GoreHarness.dll")" "$(cygpath -w "$PROJ/GORELAB_GOLDEN.json")"
