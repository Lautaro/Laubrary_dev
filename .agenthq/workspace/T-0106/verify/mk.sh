#!/bin/sh
# mk.sh <typename> <outfile> <method...>
T="$1"; OUT="$2"; shift 2
{
echo 'System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }'
echo "var t = Find(\"$T\");"
echo 'var sb=new System.Text.StringBuilder();'
for m in "$@"; do
  echo "sb.AppendLine((string)t.GetMethod(\"$m\").Invoke(null,null));"
done
echo "System.IO.File.AppendAllText(@\"$OUT\", sb.ToString());"
echo 'return "wrote " + sb.Length + " chars";'
} 
