"""P7 immutable P0 accounting, source-hash fixture export and explicit comparison gates.

No native DLL is loaded. The old 90-recording gate stays intact. Additional cases
use independently reconstructed packed rows whose SHA256 must match P0.
"""
from __future__ import annotations
import argparse
from array import array
from collections import Counter
import copy
import hashlib
import json
import math
import shutil
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
NATIVE = ROOT / "Tools/ZTrackerNative"
sys.path.insert(0, str(NATIVE))
import p3_goldens as p3
from compare_golden import load_corpus
from golden_cases import cases as specifications, cell, EMPTY, OFF

ORIGINAL = p3.CORE_IDS + p3.CHARACTERIZATION_IDS + p3.P4_IDS + p3.P4_EXTRA_IDS
assert len(ORIGINAL) == len(set(ORIGINAL)) == 90
FROZEN_MANIFEST = "8ffb0a79abd1582543a0b08a2afe7432433551fc1f4ef52b0301102bf7afedac"
GAP = "sample_loop_3"
NUMERIC = {f"clock_120_b{b}" for b in (64,333,1024)} | {
    f"command_{c:02X}_control" for c in (1,2,4,8,10)}

def read(path):
    return json.loads(path.read_text(encoding="utf-8"))

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False)+"\n", encoding="utf-8")

def verify_corpus():
    raw=(NATIVE/"golden/manifest.json").read_bytes()
    assert hashlib.sha256(raw).hexdigest()==FROZEN_MANIFEST, "Frozen P0 manifest changed"
    _, refs=load_corpus(NATIVE/"golden")
    assert len(refs)==122 and sum(c[0].get("statistical_only",False) for c in refs.values())==3
    return refs

def patterns(spec):
    scenario=spec.get("scenario")
    rows=[[cell(69,0)]]+[[EMPTY] for _ in range(7)]
    if scenario=="presets":
        rows=[[cell(69,0 if i==0 else -1,18,p)] for i,p in enumerate((0,1,2,9))]+[[OFF]]
    elif scenario=="command":
        cmd=spec["command"]
        args={1:80,2:80,3:64,4:0x68,7:0x68,8:255,10:3,12:24,16:0x1C,17:0x1F,18:1}
        value=spec.get("command_param",args[cmd])
        applied=0 if spec.get("neutral") else cmd
        rows=[[cell(60,0,applied,value)],[cell(72 if cmd in (3,8,18) else -1,-1,applied,value)],
              [cell(cmd=cmd,param=0)],[OFF],[EMPTY],[EMPTY]]
    elif scenario=="pan_existing":
        rows=[[cell(60,0)],[cell(cmd=8,param=255)],[cell(60,0)],[OFF],[EMPTY]]
    elif scenario=="flow":
        cmd=spec["command"]
        if cmd==0:
            rows=[[cell(60,0,11,1),cell(cmd=13,param=18)]]+[[EMPTY,EMPTY] for _ in range(23)]
            second=[[cell(69,0),EMPTY] for _ in range(24)]
        else:
            rows=[[cell(60,0,cmd,1 if cmd==11 else 18)]]+[[EMPTY] for _ in range(23)]
            second=[[cell(69,0)] for _ in range(24)]
        return [rows,second]
    elif scenario=="tempo":
        rows=[[cell(69,0,15,150)],[cell(cmd=15,param=3)],[cell(cmd=15,param=0)],[OFF]]+[[EMPTY] for _ in range(4)]
    elif scenario=="clock":
        rows=[[cell(69,0) if i==0 else OFF if i==3 else EMPTY,EMPTY] for i in range(8)]
    return [rows]

def pack(rows):
    return b"".join(struct.pack("<bhBBB",*c) for row in rows for c in row)

def prepare(output):
    refs=verify_corpus()
    missing=sorted(set(refs)-set(ORIGINAL))
    assert len(missing)==32
    extras=[c for c in missing if c!=GAP]
    saved=p3.CORE_IDS[:]
    p3.prepare(output/"baseline",p4=True)
    try:
        p3.CORE_IDS=saved+extras
        p3.prepare(output,p4=True)
    finally:
        p3.CORE_IDS=saved
    fixtures=read(output/"fixtures.json")
    specs={s["id"]:s for s in specifications()}
    mapping=[]
    for f in fixtures["cases"]:
        if f["id"] not in extras:
            continue
        c=refs[f["id"]][0]
        spec=specs[f["id"]]
        reconstructed=patterns(spec)
        calls=[x for x in c["native_calls"] if x["function"]=="ZT_SetPatternData"]
        assert len(calls)==len(reconstructed)
        for call, rows in zip(calls,reconstructed):
            args=call["args"]
            assert len(rows)==args[2] and len(rows[0])==args[3]
            assert hashlib.sha256(pack(rows)).hexdigest()==args[1]["sha256"], f["id"]+": packed P0 rows changed"
        f["legacyPatterns"]=[{"rows":len(rows),"channels":len(rows[0]),"cells":[
            {"note":x[0],"instrument":x[1],"volume":x[2],"command":x[3],"argument":x[4]}
            for row in rows for x in row]} for rows in reconstructed]
        f["legacyOrder"]=list(range(len(reconstructed)))
        order_call=next(x for x in c["native_calls"] if x["function"]=="ZT_SetOrderList")
        assert hashlib.sha256(struct.pack("<"+"i"*len(f["legacyOrder"]),*f["legacyOrder"])).hexdigest()==order_call["args"][0]["sha256"]
        f["bpm"]=137.5 if f["id"].startswith("clock_137p5") else spec.get("bpm",120)
        f["lpb"]=4 if f["id"].startswith("clock_137p5") else spec.get("lpb",4)
        f["tpl"]=6
        f["eventTrack"]=spec.get("scenario")=="clock"
        f["gain"]=spec.get("initial_gain",1)
        f["actions"]=[{"frame":x["hostFrame"],"kind":"play" if x["function"]=="ZT_Play" else "stop"}
                      for x in c["native_calls"] if x["function"] in ("ZT_Play","ZT_Stop")]
        f["timeline"]="p7-contract"
        mapping.append({"id":f["id"],"sourcePatternHashes":[x["args"][1]["sha256"] for x in calls],
                        "sourceOrderHash":order_call["args"][0]["sha256"],
                        "mode":"numeric" if f["id"] in NUMERIC else "accepted-model-policy-difference"})
    write(output/"fixtures.json",fixtures)
    write(output/"p7-input-accounting.json",{"manifest_sha256":FROZEN_MANIFEST,"historical":122,"original":90,
        "additionalRendered":31,"explicitUnrepresentable":1,"analytic":3,"fixtures":len(fixtures["cases"]),"additional":mapping})
    # Every missing reference is present in accounting, even the unsupported dual-loop recording.
    write(output/"p7-disposition-plan.json",{"cases":[{"id":c,"rendered":c!=GAP,
        "gate":"old-scoped" if c in ORIGINAL else "numeric" if c in NUMERIC else "unrepresentable" if c==GAP else "model-policy"}
        for c in sorted(refs)]})
    print(f"P7 prepared: 122 accounted; 121 historical renders + 3 analytic; dual-loop explicitly unrepresentable. {output}")
    return 0

def pcm(path):
    value=array("f");value.frombytes(path.read_bytes());return value

def numeric(a,b):
    assert len(a)==len(b)
    bad=sum(abs(x-y)>1e-5+1e-4*abs(x) for x,y in zip(a,b))
    return {"bad_samples":bad,"max_absolute_error":max(abs(x-y) for x,y in zip(a,b)),"passed":bad==0}

def row_oracle(f):
    # Independent binary64 compensated deadlines. No candidate events feed this oracle.
    position=0.;comp=0.;bpm=f["bpm"];lpb=f["lpb"];rows=[]
    row=0
    while position<f["frames"]:
        if f["id"]=="command_0F" and row==0:bpm=150
        rows.append((row,math.floor(position)))
        duration=48000*60/(bpm*lpb)
        corrected=duration-comp
        end=position+corrected
        comp=(end-position)-corrected
        position=end;row+=1
        if row>=f["legacyPatterns"][0]["rows"]:break
    return rows

def compare(output):
    refs=verify_corpus()
    baseline=output/"baseline"
    for cid in ORIGINAL:
        for suffix in (".f32",".engine-events.json"):
            shutil.copyfile(output/"candidate"/(cid+suffix),baseline/"candidate"/(cid+suffix))
    (baseline/"policy-candidate").mkdir(exist_ok=True)
    for buffer in (64,333,1024):
        for suffix in (".f32",".engine-events.json"):
            name=f"p5_policy_{buffer}{suffix}"
            shutil.copyfile(output/"policy-candidate"/name,baseline/"policy-candidate"/name)
    failed=p3.compare_output(baseline)
    old=read(baseline/"comparison.json")
    reports=old["cases"]
    for report in reports:
        if report["id"] in p3.CHARACTERIZATION_IDS:
            report["nativeComparisonStatus"]=report["status"]
            report["status"]="DSP_CHARACTERIZATION_NOT_PARITY"
    fixtures={f["id"]:f for f in read(output/"fixtures.json")["cases"]}
    for cid in sorted(set(refs)-set(ORIGINAL)-{GAP}):
        f=fixtures[cid]
        trace=read(output/"candidate"/(cid+".engine-events.json"))
        a=pcm(NATIVE/"golden"/refs[cid][0]["audio_file"]);b=pcm(output/"candidate"/(cid+".f32"))
        checks={"compiled":trace["compiled"],"no_overflow":trace["overflow"]==0,
                "no_export_failure":not trace["failures"],"finite":len(b)==len(a) and all(math.isfinite(v) for v in b)}
        actual_rows=[(e["row"],e["samplePosition"]) for e in trace["events"] if e["kind"]=="Row"]
        if cid=="transport_restart":
            checks["restart_counter"]=[e["samplePosition"] for e in trace["events"] if e["kind"]=="Started"]==[0,6000]
            checks["stop_counter"]=[e["samplePosition"] for e in trace["events"] if e["kind"]=="Stopped"]==[6000]
            checks["rows"]=actual_rows==[(0,0),(0,6000),(1,12000)]
            # Native emits the next row at the end of the previous render block,
            # including a row immediately before Stop at the same sample boundary.
            # Modern dispatch occurs before the first sample of the next block.
            checks["documented_native_boundary_difference"]=[(e["rowIndex"],e["samplePosition"]) for e in refs[cid][2] if e["type"]==0]==[(0,0),(1,6000),(0,6000),(1,12000)]
        else:
            checks["rows"]=actual_rows==row_oracle(f)
        expected_commands=sum(c["command"]!=0 for p in f["legacyPatterns"] for c in p["cells"])
        expected_inactive=sum(c["command"]!=0 and (c["command"]!=15 or c["argument"]<1 or 17<=c["argument"]<=31)
                              for p in f["legacyPatterns"] for c in p["cells"])
        checks["preserved_bytes"]=trace["preservedLegacyCommands"]==expected_commands
        checks["inactive_policy"]=trace["inactiveLegacyCommands"]==expected_inactive
        checks["diagnosed_refusals"]=expected_inactive==0 or sum("LEGACY_" in d for d in trace["diagnostics"])>=expected_inactive
        if f["eventTrack"]:
            checks["authored_events"]=[(e["row"],e["samplePosition"],e["track"],e["column"]) for e in trace["events"] if e["kind"]=="Authored"]==[(r,t,1,0) for r,t in row_oracle(f)]
        comparison=numeric(a,b)
        if cid in NUMERIC:
            checks["numeric_parity"]=comparison["passed"]
            # Exact comparable native row/beat/note events, no alignment or tolerance increase.
            native=refs[cid][2]
            native_rows=[(e["rowIndex"],e["samplePosition"]) for e in native if e["type"]==0]
            checks["native_row_events"]=actual_rows==native_rows
            native_notes=[(e["samplePosition"],e["noteValue"]) for e in native if e["type"]==5]
            checks["native_note_events"]=[(e["samplePosition"],e["note"]) for e in trace["events"] if e["kind"]=="NoteOn"]==native_notes
            status="PASS" if all(checks.values()) else "FAIL"
        else:
            status="MODEL_POLICY_VERIFIED_NOT_PARITY" if all(checks.values()) else "FAIL"
        reports.append({"id":cid,"status":status,"checks":checks,"comparison":comparison,
            "scope":"Restart has exact PCM parity; end-of-block native row dispatch differs from modern before-next-sample dispatch." if cid=="transport_restart" else "Neutral 0C fixture still contains row2 C00; native silences output, modern retains and refuses the ambiguous legacy volume command." if cid=="command_0C_control" else "Raw legacy commands retained and refused; supported modern contract independently asserted. No native parity claim." if cid not in NUMERIC else "Original tolerances and exact comparable events."})
        failed|=not all(checks.values())
    gap=refs[GAP][0]
    normal=next(x["args"] for x in gap["native_calls"] if x["function"]=="ZT_SetSampleLoop")
    held=next(x["args"] for x in gap["native_calls"] if x["function"]=="ZT_SetSampleSustainLoop")
    assert normal[1:]==[3,128,3072] and held[1:]==[512,1536]
    reports.append({"id":GAP,"status":"UNREPRESENTABLE_MODEL_EXCLUDED","rendered":False,
        "assertion":"P0 has distinct held [512,1536] and release [128,3072] loop regions. Current model has one loop and release-exit; no exact adapter exists.",
        "parity":False})
    assert len(reports)==len({r["id"] for r in reports})==122
    counts=Counter(r["status"] for r in reports)
    result={"gate_success":not failed,"full_numeric_parity":False,"historical_cases":122,
            "historical_rendered":121,"analytic_cases":3,"counts":dict(counts),
            "atol":1e-5,"rtol":1e-4,"event_frame_tolerance":0,"manifest_sha256":FROZEN_MANIFEST,
            "claim":"Full accounting, not full parity. DSP characterizations, statistical and model exclusions remain explicit.",
            "cases":sorted(reports,key=lambda r:r["id"])}
    write(output/"p7-comparison.json",result)
    print(json.dumps({k:v for k,v in result.items() if k!="cases"},indent=2))
    return int(bool(failed))

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase",choices=("prepare","compare"))
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    return prepare(args.output) if args.phase=="prepare" else compare(args.output)

if __name__=="__main__":
    raise SystemExit(main())
