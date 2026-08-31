import io
p = r"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperBorderAudit.cs"
s = io.open(p, encoding="utf-8").read()
anchor = "            return ShaperNode.Primitive(d, kind.ToString());\n        }"
assert s.count(anchor) == 1, s.count(anchor)
helpers = io.open(r"D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/scripts/helpers.cs.txt", encoding="utf-8").read()
s = s.replace(anchor, anchor + "\n" + helpers.strip("\n"), 1)
io.open(p, "w", encoding="utf-8").write(s)
print("inserted")
