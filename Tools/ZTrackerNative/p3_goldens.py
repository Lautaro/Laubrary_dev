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
P4_IDS = [*[f"synth_wave_{i}" for i in range(5)], *[f"synth_blend_{i}" for i in range(4)],
          *[f"envelope_target_{i}" for i in range(4)], "envelope_control",
          *[f"envelope_loop_{i}" for i in range(3)], "modulation", "portamento_control", "portamento_enabled",
          "synth_unison_3", "synth_unison_8", "detune_control", "defect_detune",
          *[f"channel_{family}_{setting}" for family in ("synth", "unison", "fm") for setting in ("base", "mute", "half", "left", "right")],
          *[f"fm_algorithm_{i}" for i in range(6)], "defect_fm_6", "defect_fm_7", *[f"sample_b_{i}" for i in range(4)]]
P4_EXTRA_IDS = ["command_07", "command_07_control", "noise_5", "noise_6", "noise_vibrato_jitter"]


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def prepare(output, p4=False):
    _, reference = load_corpus(ROOT / "golden")
    manifest = json.loads((ROOT / "golden/manifest.json").read_text(encoding="utf-8"))
    refdir, candidate = output / "reference", output / "candidate"
    refdir.mkdir(parents=True, exist_ok=True)
    candidate.mkdir(parents=True, exist_ok=True)
    scoped = copy.deepcopy(manifest)
    scoped["cases"] = [copy.deepcopy(reference[cid][0]) for cid in CORE_IDS + CHARACTERIZATION_IDS + (P4_IDS + P4_EXTRA_IDS if p4 else [])]
    scoped["audio_bytes"] = sum(c["frames"] * 8 for c in scoped["cases"])
    fixtures = []
    for case in scoped["cases"]:
        if case['id']=='command_07':
            # PM T-0013 todo4 ruling. Only the copied comparison metadata is tagged;
            # checked-in manifest, reference audio and native events remain untouched.
            case['approved_defects']=['tremolo_ignored']
        for name in (case["audio_file"], case["events_file"]):
            shutil.copyfile(ROOT / "golden" / name, refdir / name)
        original = reference[case["id"]][0]
        samples, instruments, actions = [], {}, []
        def extra(args):
            return {"values": args}
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
            elif name == "ZT_SetSynthInstrument":
                instruments[args[0]] = {"id": args[0], "kind": "synth", "values": args, "entries": [], "filter": []}
            elif name == "ZT_SetFMInstrument":
                instruments[args[0]] = {"id": args[0], "kind": "fm", "values": args, "entries": [], "filter": []}
            elif name == "ZT_SetFMOperator":
                instruments[args[0]]["entries"].append(extra(args))
            elif name == "ZT_SetInstrumentVibrato":
                instruments[args[0]]["vibrato"] = args
            elif name == "ZT_SetInstrumentPortamento":
                instruments[args[0]]["glide"] = args
            elif name == "ZT_SetInstrumentArpeggio":
                notes, times, values = [0, 4, 7], [0, .1], [.015, .035]
                for src, raw in ((args[2], struct.pack('<3i', *notes)), (args[5], struct.pack('<2f', *times)), (args[6], struct.pack('<2f', *values))):
                    if digest(raw) != src['sha256']: raise ValueError('arpeggio original-source hash mismatch')
                instruments[args[0]]["arp"] = {"notes": notes, "times": times, "values": values, "perNote": bool(args[4])}
            elif name == "ZT_SetInstrumentEnvelope":
                ranges = {0: (.05, .9, .2), 1: (.15, .8, .3), 2: (.5, 3, 1), 3: (.02, .7, .13), 4: (-100, 100, -20)}
                a, b, d = ranges[args[1]]
                values = [[0, a, 1], [.035, b, 2], [.09, d, .5]]
                if digest(struct.pack('<9f', *(x for row in values for x in row))) != args[3]['sha256']: raise ValueError('envelope original-source hash mismatch')
                instruments[args[0]].setdefault('envelopes', []).append({'target': args[1], 'points': [extra(row) for row in values], 'loop': bool(args[6]), 'mode': args[7], 'start': args[8], 'end': args[9]})
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
                         "timeline": "portamento" if case['id'].startswith('portamento_') else "tremolo" if case['id'] in ('command_07','command_07_control') else "",
                         "masterFilter": master_filter, "delay": delay, "reverb": reverb})
    # Additional policy probes use independently calculated PCM, separate from P0 parity.
    if p4:
        for buffer in (64,333,1024):
            frames=12000
            fixtures.append({"id":f"p5_policy_{buffer}","frames":frames,"buffer":buffer,"samples":[{"rate":48000,"left":[.2]*1000,"right":[.2]*1000,"loop":1,"start":0,"end":1000}],"instruments":[{"id":0,"kind":"sample","values":[0,0,69,0,1,0,0,0,1,1],"entries":[],"filter":[]}],"actions":[{"frame":0,"kind":"play"}],"blocks":[{"hostFrame":at,"frames":min(buffer,frames-at)} for at in range(0,frames,buffer)],"sequenced":True,"rows":8,"everyRow":False,"gain":1,"pan":0,"timeline":"p5-policy"})
    write_json(refdir / "manifest.json", scoped)
    write_json(output / "fixtures.json", {"cases": fixtures})
    print(f"Prepared {len(fixtures)} fixtures (90 retained source-hash cases plus 3 independent P5 policy cases when --p4): {output}")


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
            if not trace["sequenced"] or name not in ("Started", "Row", "Beat", "NoteOn", "NoteOff"):
                continue
            kind = {"Started": 2, "Row": 0, "Beat": 9, "NoteOn": 5, "NoteOff": 6}[name]
            note = name in ("NoteOn", "NoteOff")
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
    policy=[]
    for buffer in (64,333,1024):
        cid=f"p5_policy_{buffer}";path=output/"policy-candidate"/(cid+".f32")
        if not path.exists():continue
        raw=array("f");raw.frombytes(path.read_bytes())
        trace=json.loads((output/"policy-candidate"/(cid+".engine-events.json")).read_text())
        expected=[0 if frame<3000 else .2*(8/15)*(.5 if frame<4500 else .75 if frame<6000 else 1) for frame in range(12000)]
        error=max(abs(raw[frame*2+channel]-expected[frame]) for frame in range(12000) for channel in (0,1))
        notes=[event["samplePosition"] for event in trace["events"] if event["kind"]=="NoteOn"]
        if error>1e-6 or notes!=[3000] or not trace["compiled"] or trace["overflow"] or trace["failures"]:raise ValueError(f"{cid}: policy error={error},notes={notes},compiled={trace['compiled']}")
        policy.append({"id":cid,"max_error":error,"note_frames":notes,"compiled":trace["compiled"]})
    write_json(output/"p5-policy-result.json",{"cases":policy,"scope":"Independent policy PCM/event oracle; no Renoise/native parity claim"})
    if policy:print(f"P5 independent policy PCM/events: {len(policy)}/3 passed")
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

    def pcm(cid):
        value=array('f');value.frombytes((output/'candidate'/f'{cid}.f32').read_bytes());return value
    def observations(cid):
        return json.loads((output/'candidate'/f'{cid}.engine-events.json').read_text())['observations']
    def equal_audio(a,b):
        x,y=pcm(a),pcm(b)
        return len(x)==len(y) and all(abs(v-w)<=1e-5+1e-4*abs(w) for v,w in zip(x,y))
    def different(a,b):
        return max(abs(x-y) for x,y in zip(pcm(a),pcm(b)))>.001
    def corrected_detune(*args):
        # Independently integrate the published exponential curve and 5ms smoother.
        expected=19.0;time=0.0;by_frame={}
        for frame in range(1,16001):
            if time<=.035:target=-100+200*(time/.035)**2
            elif time<=.09:target=100-120*((time-.035)/(.09-.035))**.5
            else:target=-20
            expected+=(target-expected)/(48000*.005)
            by_frame[frame]=expected
            # Native engine stores time in float32; reproduce storage, not candidate values.
            time=struct.unpack('<f',struct.pack('<f',time+struct.unpack('<f',struct.pack('<f',1/48000))[0]))[0]
        obs=observations('defect_detune')
        return bool(obs) and all(abs(o['detune']-by_frame[o['frame']])<.002 for o in obs) and different('defect_detune','detune_control')
    def corrected_glide(*args):
        obs=[o for o in observations('portamento_enabled') if o['frame']>6000 and o['note']==72]
        # Previous 60 -> new 72 starts at one half of target frequency, 120ms exponential.
        return bool(obs) and all(abs(o['glide']-(1-.5*math.exp(-(o['frame']-6000)/(.12*48000))))<1e-6 for o in obs) and different('portamento_enabled','portamento_control')
    def corrected_loop(*args):
        return all(o['loop']==3 for o in observations('envelope_loop_2')) and equal_audio('envelope_loop_2','envelope_loop_1') and different('envelope_loop_2','envelope_loop_0')
    def corrected_fm(cid):
        return lambda *args: bool(observations(cid)) and all(o['algorithm']==5 for o in observations(cid)) and equal_audio(cid,'fm_algorithm_5')
    def corrected_tremolo(*args):
        a,b=pcm('command_07'),pcm('command_07_control');phase=0.0
        for frame in range(1,len(a)//2):
            phase=struct.unpack('<f',struct.pack('<f',phase+struct.unpack('<f',struct.pack('<f',3/48000))[0]))[0]
            phase-=math.floor(phase)
            gain=max(0,1+math.sin(phase*math.tau)*8/15)
            if any(abs(a[frame*2+c]-b[frame*2+c]*gain)>1e-5+1e-4*abs(b[frame*2+c]*gain) for c in (0,1)):return False
        return different('command_07','command_07_control') and a[:2]==b[:2]
    exemptions={'kit_reuse_filtered':'kit_filter_leak'};checks={'kit_reuse_filtered':corrected_kit}
    if any(c['id']=='defect_detune' for c in manifest['cases']):
        for cid,defect,callback in [('defect_detune','detune_envelope',corrected_detune),('portamento_enabled','instrument_glide',corrected_glide),('envelope_loop_2','envelope_mapping',corrected_loop),('defect_fm_6','fm_choices',corrected_fm('defect_fm_6')),('defect_fm_7','fm_choices',corrected_fm('defect_fm_7')),('command_07','tremolo_ignored',corrected_tremolo)]:exemptions[cid]=defect;checks[cid]=callback
    result = compare(output / "reference", output / "candidate",exemptions=exemptions,correction_checks=checks)
    write_json(output / "comparison.json", result)
    for case in result["cases"]:
        if case['id'] in CHARACTERIZATION_IDS:
            case['scope'] = 'nearest-parameter AudioCore mapping characterization; different DSP/mix contract'
            print(f"{case['id']}: CHARACTERIZATION {case['status']} maxError={case['max_absolute_error']:.9g}")
            continue
        print(f"{case['id']}: {case['status']} maxError={case['max_absolute_error']:.9g}")
    write_json(output / "comparison.json", result)
    failures = [c for c in result["cases"] if (c['id'] not in CHARACTERIZATION_IDS and c["status"] not in ("PASS", "APPROVED_CORRECTION_VERIFIED", "STATISTICAL_ONLY_EXCLUDED")) or c['event_errors'] or c['state_errors']]
    result['equivalent_success'] = not failures
    result['equivalent_case_count'] = len(CORE_IDS)
    result['characterization_case_count'] = len(CHARACTERIZATION_IDS)
    result['p4_case_count']=sum(c['id'] in P4_IDS for c in result['cases'])
    result['p4_numeric_scope']=sum(c['id'] in P4_IDS+P4_EXTRA_IDS and c['status']=='PASS' and c['id'] not in exemptions for c in result['cases'])
    result['correction_count']=sum(c['status']=='APPROVED_CORRECTION_VERIFIED' for c in result['cases'])
    write_json(output / "comparison.json", result)
    core_failed=sum(c['id'] in CORE_IDS for c in failures)
    print(f"P3 equivalent comparison: {len(CORE_IDS)-core_failed} passed / {core_failed} failed; {len(CHARACTERIZATION_IDS)} explicit DSP characterizations")
    if result['p4_case_count']:
        p4_failed=sum(c['id'] in P4_IDS for c in failures)
        print(f"P4 scoped comparison: {result['p4_case_count']-p4_failed}/{result['p4_case_count']} accepted; {result['p4_numeric_scope']} new ordinary numeric cases including extra control; {result['correction_count']} total separate corrections; {len(failures)} gate failures")
    return bool(failures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=("prepare", "compare"))
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--p4", action="store_true", help="Extend the retained P3 gate with P4 synth/FM/B/correction fixtures")
    options = parser.parse_args()
    return compare_output(options.output) if options.phase == "compare" else prepare(options.output, options.p4)


if __name__ == "__main__":
    raise SystemExit(main())
