import subprocess, sys, json, os, io
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
f = sys.argv[1]
p = os.path.abspath(f).replace(os.sep, "/")
r = subprocess.run(["C:/Users/Lauta/AppData/Local/Unity/bin/unity.exe", "command",
                    "--project-path", "D:/UNITY/Laubrary Dev - Shaper",
                    "--timeout", "120", "eval_file", "--file", p],
                   capture_output=True, text=True, encoding="utf-8", errors="replace")
out = r.stdout + r.stderr
lines = [l for l in out.splitlines() if l.startswith("eval_file")]
if not lines:
    print(out)
    sys.exit(1)
d = json.loads(lines[0].split("\t")[2])
if d.get("output"):
    print("OUTPUT:", d["output"])
if d.get("result"):
    print(d["result"])
if d.get("error"):
    print("ERROR:", d["error"])
if d.get("errorDetails"):
    print("DETAILS:", d["errorDetails"])
if d.get("diagnostics"):
    print("DIAG:", d["diagnostics"])
