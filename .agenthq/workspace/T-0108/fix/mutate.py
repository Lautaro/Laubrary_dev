"""Apply / revert the fix pass's mutations. Every mutation is an exact-string replace, and a
revert is proved by md5 against the pre-mutation copies in fix/orig/."""
import io, sys, hashlib, os

SRC = r"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary"
ORIG = r"D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/fix/orig"

FILES = {
    "res": "Runtime/Shaper/ShaperFillResolver.cs",
    "cmp": "Runtime/Shaper/ShaperLightCompiler.cs",
    "sol": "Runtime/Shaper/ShaperSolids.cs",
    "law": "Runtime/Shaper/ShaperLightLaw.cs",
}

M = {
  # ---- batch 1 -------------------------------------------------------------
  "M1": ("cmp",
    'if (enabled > ShaperLightRigCompiled.MaxLights)\n            {\n                prog.hasTooManyLights = true;\n                prog.tooManyLightsCount = enabled - ShaperLightRigCompiled.MaxLights;',
    'if (authored > ShaperLightRigCompiled.MaxLights)\n            {\n                prog.hasTooManyLights = true;\n                prog.tooManyLightsCount = authored - ShaperLightRigCompiled.MaxLights;'),

  "M2": ("cmp",
    'c.rimPower = Dial(ShaperValue.Sample(resp.rimPower, p, seed + 303u, 2.2f),\n                              MinPower, MaxPower, 2.2f);',
    'c.rimPower = ShaperValue.Sample(resp.rimPower, p, seed + 303u, 2.2f);'),

  "M4": ("cmp",
    'bool blackAmbient = prog.rig.ambR <= 0f && prog.rig.ambG <= 0f && prog.rig.ambB <= 0f;',
    'bool blackAmbient = false;'),

  "M5": ("sol",
    'if (form == ShaperSolidForm.Gem)\n                        return "Aspect does nothing on a Gem.',
    'if (false)\n                        return "Aspect does nothing on a Gem.'),

  "M6": ("cmp",
    'ShaperLayer layer = document.layers[layerIndex];',
    'ShaperLayer layer = document.layers[0];'),

  # ---- batch 2 -------------------------------------------------------------
  "M3": ("cmp",
    'float reach = range > MinRange ? range : MinRange;\n                    c.invRangeSq = 1f / (reach * reach);',
    'c.invRangeSq = range > 1e-6f ? 1f / (range * range) : 0f;'),

  "M7": ("res",
    'int bnslab = o * buf.sampleCapacity;',
    'int bnslab = b * buf.sampleCapacity;'),

  "M7b": ("res",
    'LightSample(scene, buf, o, bnslab, i, a3, grid, x0, y0, width,\n                                        true, false, out br, out bg, out bb);',
    'LightSample(scene, buf, b, bnslab, i, a3, grid, x0, y0, width,\n                                        true, false, out br, out bg, out bb);'),

  "M8": ("res",
    'cr += scene.glow[t3 + 0];\n                cg += scene.glow[t3 + 1];\n                cb += scene.glow[t3 + 2];',
    'cr += 0f; cg += 0f; cb += 0f;'),

  # ---- batch 3 -------------------------------------------------------------
  "M9": ("res",
    'buf.subtree[s4 + 0] += buf.albedo[a3 + 0] * ce;\n                        buf.subtree[s4 + 1] += buf.albedo[a3 + 1] * ce;\n                        buf.subtree[s4 + 2] += buf.albedo[a3 + 2] * ce;',
    'buf.subtree[s4 + 0] += buf.albedo[a3 + 0] * ce * 0.9f;\n                        buf.subtree[s4 + 1] += buf.albedo[a3 + 1] * ce * 0.9f;\n                        buf.subtree[s4 + 2] += buf.albedo[a3 + 2] * ce * 0.9f;'),

  "M10": ("sol",
    'float R = op.r;\n            float d = Mathf.Sqrt(lx * lx + ly * ly);',
    'float R = op.r;\n            float d = Mathf.Sqrt(lx * lx + ly * ly * (1f + 0.5f * Mathf.Abs(Mathf.Sin(op.tilt))));'),

  "M11": ("res",
    'int t = nslab + i;',
    'int t = nslab + i;\n            float[] mutScratch = new float[3]; mutScratch[0] = 1f; t = t * (int)mutScratch[0];'),
}

BATCH = {
  "1": ["M1", "M2", "M4", "M5", "M6"],
  "2": ["M3", "M7", "M7b", "M8"],
  "3": ["M9", "M10", "M11"],
}


def apply(names):
    for n in names:
        key, old, new = M[n]
        p = os.path.join(SRC, FILES[key])
        s = io.open(p, encoding="utf-8").read()
        if s.count(old) != 1:
            raise SystemExit("%s: anchor found %d times (need 1)" % (n, s.count(old)))
        io.open(p, "w", encoding="utf-8").write(s.replace(old, new, 1))
        print("applied", n, "->", FILES[key])


def revert():
    for key, rel in FILES.items():
        src = os.path.join(ORIG, os.path.basename(rel) + ".ORIG")
        dst = os.path.join(SRC, rel)
        data = io.open(src, "rb").read()
        io.open(dst, "wb").write(data)
    ok = True
    for key, rel in FILES.items():
        a = hashlib.md5(io.open(os.path.join(SRC, rel), "rb").read()).hexdigest()
        b = hashlib.md5(io.open(os.path.join(ORIG, os.path.basename(rel) + ".ORIG"), "rb").read()).hexdigest()
        print("revert", os.path.basename(rel), a, "OK" if a == b else "MISMATCH")
        ok &= a == b
    if not ok:
        raise SystemExit("REVERT MISMATCH")


if __name__ == "__main__":
    if sys.argv[1] == "revert":
        revert()
    else:
        apply(BATCH[sys.argv[1]])
