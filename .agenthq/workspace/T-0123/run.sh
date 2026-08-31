#!/bin/sh
"C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe" command eval_file --project-path "D:/UNITY/Laubrary Dev" --file "$1" 2>&1 | python -c "
import sys,json
raw=sys.stdin.read()
i=raw.find('{\"output\"')
if i<0:
    print(raw); sys.exit()
j=raw.rfind('}',0,raw.find('\t{\"file\"')) if raw.find('\t{\"file\"')>0 else raw.rfind('}')
d=json.loads(raw[i:j+1])
r=d.get('result')
if r is None: r='RESULT-NULL err='+str(d.get('error'))+' details='+str(d.get('errorDetails'))+' out='+str(d.get('output'))
print(r)
"
