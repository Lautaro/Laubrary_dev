"""Exercise the archived offline reference DLL and ABI; no Unity or shipped runtime import."""
import ctypes as c
import math
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
package = root / "Assets/Packages/ZTracker"
bindings = (Path(__file__).resolve().parent / "native_abi.cs").read_text(encoding="utf-8-sig")
dll = c.CDLL(str(Path(__file__).resolve().parent / "retired/ZTrackerEngine.dll"))

class Cell(c.Structure):
    _pack_ = 1
    _fields_ = [("note", c.c_int8), ("instrument", c.c_int16), ("volume", c.c_uint8),
                ("effectCmd", c.c_uint8), ("effectParam", c.c_uint8)]

class Event(c.Structure):
    _fields_ = [("type", c.c_uint8), ("samplePosition", c.c_uint64),
                ("patternIndex", c.c_int), ("rowIndex", c.c_int), ("channelIndex", c.c_int),
                ("noteValue", c.c_int), ("instrumentID", c.c_int), ("intParam", c.c_int),
                ("floatParam", c.c_float), ("stringPayload", c.c_char * 64)]

types = {"void": None, "int": c.c_int, "float": c.c_float, "double": c.c_double,
         "IntPtr": c.c_void_p, "float[]": c.POINTER(c.c_float), "int[]": c.POINTER(c.c_int),
         "TrackerCellData[]": c.POINTER(Cell), "ZTrackerEventData": c.POINTER(Event)}
signatures = {}
exports = 0
for ret, name, params in re.findall(r"public static extern (\w+) (\w+)\((.*?)\);", bindings, re.S):
    fn = getattr(dll, name)
    exports += 1
    pairs = [p.strip().split() for p in params.split(",")] if params.strip() else []
    pairs = [(p[-2], p[-1]) for p in pairs]
    signatures[name] = pairs
    if ret in types and all(t in types for t, _ in pairs):
        fn.restype = types[ret]
        fn.argtypes = [types[t] for t, _ in pairs]
assert dll.ZT_GetABIVersion() == 1
print(f"ABI 1; all {exports} managed entry points exported; packed cell size {c.sizeof(Cell)}")

def call(name, **kwargs):
    return getattr(dll, name)(*[kwargs[n] for _, n in signatures[name]])

def render(ctx, frames):
    left, right = (c.c_float * frames)(), (c.c_float * frames)()
    dll.ZT_Process(ctx, left, right, frames)
    assert all(math.isfinite(x) for x in list(left) + list(right))
    return sum(abs(x) for x in left), sum(abs(x) for x in right)

def exercise(kind, volume=1, pan=0, unison=1):
    ctx = dll.ZT_Create(48000)
    assert ctx
    try:
        if kind == "sample":
            pcm = (c.c_float * 48000)(*[math.sin(i * math.tau * 440 / 48000) for i in range(48000)])
            sid = dll.ZT_LoadSample(ctx, pcm, None, 48000, 48000, 1)
            call("ZT_SetSampleInstrument", ctx=ctx, id=0, sampleID=sid, baseNote=69, fineTune=0,
                 volume=.4, pan=.2, attack=.001, decay=.01, sustain=1, release=.01,
                 sampleIDB=-1, baseNoteB=69, fineTuneB=0, blendMode=0, blendDefault=0,
                 pmDepth=0, blendEnvelopeEnabled=0, blendAttack=.01, blendDecay=.01,
                 blendSustain=1, blendRelease=.01)
        else:
            call("ZT_SetSynthInstrument", ctx=ctx, id=0, waveA=0, waveB=0, blendMode=0,
                 blendDefault=0, pmDepth=0, waveBRatio=1, blendEnvelopeEnabled=0,
                 blendAttack=.01, blendDecay=.01, blendSustain=1, blendRelease=.01,
                 unisonVoices=unison, unisonDetune=10, unisonSpread=.8,
                 volume=.4, pan=.2, attack=.001, decay=.01, sustain=1, release=.01, pulseWidth=.5)
        dll.ZT_SetSongTempo(ctx, 120, 6)
        dll.ZT_SetLinesPerBeat(ctx, 4)
        dll.ZT_SetChannelCount(ctx, 1)
        cells = (Cell * 4)(*[Cell(69, 0, 255, 0, 0) for _ in range(4)])
        dll.ZT_SetPatternData(ctx, 0, cells, 4, 1)
        order = (c.c_int * 1)(0)
        dll.ZT_SetOrderList(ctx, order, 1)
        dll.ZT_SetChannelVolume(ctx, 0, volume)
        dll.ZT_SetChannelPan(ctx, 0, pan)
        dll.ZT_Play(ctx, 0, 0)
        energy = render(ctx, 4096)
        ev = Event()
        events = []
        while dll.ZT_PollEvent(ctx, c.byref(ev)):
            events.append(ev.type)
        assert 2 in events and 5 in events and 0 in events
        # A second row must also honor channel settings.
        render(ctx, 4096)
        later = render(ctx, 4096)
        dll.ZT_Stop(ctx)
        dll.ZT_AllNotesOff(ctx)
        for _ in range(20): tail = render(ctx, 4096)
        assert sum(tail) < 1e-5, tail
        return energy, later
    finally:
        dll.ZT_Destroy(ctx)

for kind, unison in [("sample", 1), ("synth", 1), ("synth", 3)]:
    baseline, _ = exercise(kind, unison=unison)
    muted, muted_later = exercise(kind, volume=0, unison=unison)
    left, left_later = exercise(kind, pan=-1, unison=unison)
    right, right_later = exercise(kind, pan=1, unison=unison)
    half, _ = exercise(kind, volume=.5, unison=unison)
    assert sum(baseline) > 1
    assert sum(muted) == 0 and sum(muted_later) == 0
    assert left[0] > 1 and left[1] == 0 and left_later[1] == 0
    assert right[1] > 1 and right[0] == 0 and right_later[0] == 0
    ratio = sum(half) / sum(baseline)
    assert abs(ratio - .5) < .001, ratio
    print(f"PASS {kind} unison={unison}: zero gain silent; pan endpoints isolated; half gain ratio={ratio:.6f}; rows/events/stop finite")
print("Native regression checks passed")
