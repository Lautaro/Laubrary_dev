"""Prepare and compare the explicitly scoped P3 sampler corpus.

Fixtures recover original float32 PCM and native control/block schedules. Unity's
P3GoldenExporter produces candidate audio using the same Burst struct as SAP.
The checked-in P0 corpus is never modified and never supplies candidate samples.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import shutil
import struct
from array import array
from pathlib import Path

from compare_golden import compare, load_corpus

CORE_IDS = [
    "sample_mono", "sample_stereo_tuned", "sample_loop_0", "sample_loop_1",
    "sample_loop_2", "kit_mapped",
    *[f"channel_{family}_{setting}" for family in ("sample", "kit")
      for setting in ("base", "mute", "half", "left", "right")],
    "master_dry", "kit_reuse_control", "kit_reuse_filtered", "kit_fresh",
    "send_boundary_dry", "voice_stealing",
]
CHARACTERIZATION_IDS = [*[f"master_filter_{i}" for i in range(3)],
                        "master_delay", "master_reverb", "master_combined",
                        *[f"defect_send_{i}" for i in (64, 333, 1024, 1333)],
                        "send_boundary_split", "send_boundary_tick_split"]
ROOT = Path(__file__).resolve().parent
DEFAULT_OUTPUT = ROOT / ".golden-p3"


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def prepare(output):
    _, reference = load_corpus(ROOT / "golden")
    manifest = json.loads((ROOT / "golden/manifest.json").read_text(encoding="utf-8"))
    refdir, candidate = output / "reference", output / "candidate"
    refdir.mkdir(parents=True, exist_ok=True)
    candidate.mkdir(parents=True, exist_ok=True)
    scoped = copy.deepcopy(manifest)
    scoped["cases"] = [copy.deepcopy(reference[cid][0]) for cid in CORE_IDS + CHARACTERIZATION_IDS]
    scoped["audio_bytes"] = sum(c["frames"] * 8 for c in scoped["cases"])
    fixtures = []
    for case in scoped["cases"]:
        for name in (case["audio_file"], case["events_file"]):
            shutil.copyfile(ROOT / "golden" / name, refdir / name)
        original = reference[case["id"]][0]
        samples, instruments, actions = [], {}, []
        master_filter, delay, reverb = [], [], []
        returned = iter(x["returnedVoice"] for x in reference[case["id"]][3]["harness_controls"]
                        if x.get("action") == "noteOn")
        for call in original["native_calls"]:
            name, args = call["function"], call["args"]
            if name == "ZT_LoadSample":
                source = original["source_fixtures"][len(samples)]
                constant = source["formula"] == "constant .2"
                left = [(.2 if constant else .45 * math.sin(math.tau * i / 97)
                         + .12 * math.sin(math.tau * i / 31)) for i in range(args[2])]
                right = ([.3 * math.cos(math.tau * i / 73) for i in range(args[2])]
                         if args[4] == 2 else [])
                rawleft = struct.pack("<" + "f" * len(left), *left)
                rawright = struct.pack("<" + "f" * len(right), *right)
                if digest(rawleft) != args[0]["sha256"] or (right and digest(rawright) != args[1]["sha256"]):
                    raise ValueError(case["id"] + ": regenerated PCM differs from P0 source hashes")
                left = list(struct.unpack("<" + "f" * len(left), rawleft))
                right = list(struct.unpack("<" + "f" * len(right), rawright)) if right else []
                samples.append({"rate": args[3], "left": left, "right": right,
                                "loop": 0, "start": 0, "end": args[2]})
            elif name == "ZT_SetSampleLoop":
                samples[args[0]].update(loop=args[1], start=args[2], end=args[3])
            elif name == "ZT_SetSampleInstrument":
                instruments[args[0]] = {"id": args[0], "kind": "sample", "values": args,
                                        "entries": [], "filter": []}
            elif name == "ZT_SetKitInstrument":
                instruments[args[0]] = {"id": args[0], "kind": "kit", "values": [],
                                        "entries": [], "filter": []}
            elif name == "ZT_SetKitEntry":
                instruments[args[0]]["entries"].append({"values": args})
            elif name == "ZT_SetInstrumentEffects":
                instruments[args[0]]["filter"] = args
            elif name == "ZT_SetMasterFilter":
                master_filter = args
            elif name == "ZT_SetDelayParams":
                delay = args
            elif name == "ZT_SetReverbParams":
                reverb = args
            elif name == "ZT_NoteOn":
                actions.append({"frame": call["hostFrame"], "kind": "on", "a": args[0],
                                "b": args[1], "value": args[2], "expectedVoice": next(returned)})
            elif name == "ZT_NoteOff":
                actions.append({"frame": call["hostFrame"], "kind": "off", "a": args[0]})
            elif name == "ZT_Play":
                actions.append({"frame": call["hostFrame"], "kind": "play"})
        sequenced = any(a["kind"] == "play" for a in actions)
        gain, pan = original.get("gain", 1), original.get("pan", 0)
        fixtures.append({"id": case["id"], "frames": case["frames"], "buffer": case["buffer"],
                         "samples": samples, "instruments": [instruments[k] for k in sorted(instruments)],
                         "actions": actions, "blocks": original["native_render_blocks"],
                         "sequenced": sequenced, "rows": 4 if case["id"].startswith("channel_") else 8,
                         "everyRow": case["id"].startswith("channel_"), "gain": gain, "pan": pan,
                         "masterFilter": master_filter, "delay": delay, "reverb": reverb})
    write_json(refdir / "manifest.json", scoped)
    write_json(output / "fixtures.json", {"cases": fixtures})
    print(f"Prepared {len(fixtures)} original-source-hash-verified fixtures: {output}")


def compare_output(output):
    manifest = json.loads((output / "reference/manifest.json").read_text(encoding="utf-8"))
    for case in manifest["cases"]:
        raw = (output / "candidate" / case["audio_file"]).read_bytes()
        trace = json.loads((output / "candidate" / (case["id"] + ".engine-events.json")).read_text())
        mapped = []
        for event in trace["events"]:
            # P0 omits preview events and internal ticks. It includes sequencer
            # start, row, beat and note notifications in this scoped corpus.
            name = event["kind"]
            if not trace["sequenced"] or name not in ("Started", "Row", "Beat", "NoteOn"):
                continue
            kind = {"Started": 2, "Row": 0, "Beat": 9, "NoteOn": 5}[name]
            note = name == "NoteOn"
            started = name == "Started"
            mapped.append({"type": kind, "samplePosition": event["samplePosition"],
                           "patternIndex": -1 if started else event["pattern"],
                           "rowIndex": -1 if started else event["row"],
                           "channelIndex": event["track"] if note else -1,
                           "instrumentID": event["instrument"] if note else -1,
                           "noteValue": event["note"] if note else -1,
                           "intParam": 0, "floatParam": 0.0, "stringPayload": ""})
        if not trace["compiled"] or trace["overflow"] != 0 or trace["failures"]:
            raise ValueError(case["id"] + ": export proof failed: " + str(trace))
        evraw = json.dumps({"native_events": mapped, "harness_controls": [],
                            "event_timeline": []}, indent=2).encode()
        (output / "candidate" / case["events_file"]).write_bytes(evraw)
        case["audio_sha256"], case["events_sha256"] = digest(raw), digest(evraw)
    manifest["reference_engine"] = "P3 same-struct compiled Burst offline renderer"
    write_json(output / "candidate/manifest.json", manifest)

    def corrected_kit(*args):
        # The approved defect concerns only inherited filter history. Compare
        # the reused kit segment to an independently rendered fresh kit, not
        # to the contaminated native prefix or a fabricated replacement.
        def pcm(cid):
            v = array("f")
            v.frombytes((output / "candidate" / (cid + ".f32")).read_bytes())
            return v
        reused, fresh, control = pcm("kit_reuse_filtered"), pcm("kit_fresh"), pcm("kit_reuse_control")
        pairs = list(zip(reused[256:], fresh))
        correct = len(reused[256:]) == len(fresh) and all(abs(a-b) <= 1e-5 + 1e-4 * abs(b) for a,b in pairs)
        correct = correct and all(abs(a-b) <= 1e-5 + 1e-4 * abs(b) for a,b in zip(control[256:], fresh))
        return correct

    result = compare(output / "reference", output / "candidate",
                     exemptions={"kit_reuse_filtered": "kit_filter_leak"},
                     correction_checks={"kit_reuse_filtered": corrected_kit})
    write_json(output / "comparison.json", result)
    for case in result["cases"]:
        if case['id'] in CHARACTERIZATION_IDS:
            case['scope'] = 'nearest-parameter AudioCore mapping characterization; different DSP/mix contract'
            print(f"{case['id']}: CHARACTERIZATION {case['status']} maxError={case['max_absolute_error']:.9g}")
            continue
        print(f"{case['id']}: {case['status']} maxError={case['max_absolute_error']:.9g}")
    write_json(output / "comparison.json", result)
    failures = [c for c in result["cases"] if (c['id'] in CORE_IDS and c["status"] not in ("PASS", "APPROVED_CORRECTION_VERIFIED")) or c['event_errors'] or c['state_errors']]
    result['equivalent_success'] = not failures
    result['equivalent_case_count'] = len(CORE_IDS)
    result['characterization_case_count'] = len(CHARACTERIZATION_IDS)
    write_json(output / "comparison.json", result)
    print(f"P3 equivalent comparison: {len(CORE_IDS)-len(failures)} passed / {len(failures)} failed; {len(CHARACTERIZATION_IDS)} explicit DSP characterizations")
    return bool(failures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=("prepare", "compare"))
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    options = parser.parse_args()
    return compare_output(options.output) if options.phase == "compare" else prepare(options.output)


if __name__ == "__main__":
    raise SystemExit(main())
