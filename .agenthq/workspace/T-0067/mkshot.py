import sys
tpl=open(r"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0081\shot_capture.cs",encoding='utf-8').read()
tag,out=sys.argv[1],sys.argv[2]
open(r"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0067\shot-%s.cs"%tag,"w",encoding='utf-8').write(tpl.replace("%TAG%",tag).replace("%OUT%",out))
print("ok",tag,out)
