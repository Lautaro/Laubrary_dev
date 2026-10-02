#!/bin/sh
# usage: sh multipress.sh <Window> <assetPath> "btn1|nth" "btn2|nth" ...
W="$1"; A="$2"; shift 2
for spec in "$@"; do
  T=$(echo "$spec" | cut -d'|' -f1)
  N=$(echo "$spec" | cut -d'|' -f2)
  [ -z "$N" ] && N=0
  echo "### $W :: '$T' #$N"
  sh press.sh "$W" "$T" "$N" "$A" | grep -E "BEFORE|AFTER|SETTLED|NO BUTTON|POPOVER|onscreen"
done
