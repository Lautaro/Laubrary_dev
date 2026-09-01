import io
p="D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperLightAudit.cs"
s=io.open(p,encoding='utf-8').read()
new=io.open("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/fix/newlegs.cs",encoding='utf-8').read()
marker = "        // \u2500\u2500 LT-17 \u2500"
assert marker in s, "LT-17 marker missing"
assert "LT18_DegenerateDialsNeverGoNonFinite" not in s, "already spliced"
s = s.replace(marker, new + marker, 1)
old_run = "            sb.AppendLine(LT16_SharedFrame());\n            return sb.ToString();"
assert old_run in s
new_run = ("            sb.AppendLine(LT16_SharedFrame());\n"
"            // The fix pass's additions. LT-18..LT-23 close the four real defects and the three gaps an\n"
"            // independent verification found; each names the mutation that makes it fail, like every leg above.\n"
"            sb.AppendLine(LT18_DegenerateDialsNeverGoNonFinite());\n"
"            sb.AppendLine(LT19_EveryDialIsLiveOrDeclaredInert());\n"
"            sb.AppendLine(LT20_BorderIsLitByItsHost());\n"
"            sb.AppendLine(LT21_DocumentOwnsTheLights());\n"
"            sb.AppendLine(LT22_GlowPathExecutes());\n"
"            sb.AppendLine(LT23_RimIsInertOnBlackAmbientAndDeclared());\n"
"            return sb.ToString();")
s = s.replace(old_run, new_run, 1)
io.open(p,'w',encoding='utf-8').write(s)
print("spliced", len(new))
