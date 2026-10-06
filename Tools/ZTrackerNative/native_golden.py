"""Import-safe ctypes contract and offline recorder for the installed reference DLL."""
import ctypes as C
import hashlib
import math
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / 'Assets/Packages/ZTracker'
DLL_PATH = Path(__file__).resolve().parent / 'retired/ZTrackerEngine.dll'
RATE = 48000


class Cell(C.Structure):
    _pack_ = 1
    _fields_ = [('note', C.c_int8), ('instrument', C.c_int16), ('volume', C.c_uint8),
                ('effectCmd', C.c_uint8), ('effectParam', C.c_uint8)]


class EnvPoint(C.Structure):
    _pack_ = 1
    _fields_ = [('time', C.c_float), ('value', C.c_float), ('exponent', C.c_float)]


class Event(C.Structure):
    _fields_ = [('type', C.c_uint8), ('samplePosition', C.c_uint64),
                ('patternIndex', C.c_int32), ('rowIndex', C.c_int32), ('channelIndex', C.c_int32),
                ('noteValue', C.c_int32), ('instrumentID', C.c_int32), ('intParam', C.c_int32),
                ('floatParam', C.c_float), ('stringPayload', C.c_char * 64)]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def floats(values):
    return (C.c_float * len(values))(*values)


def encode(values):
    return struct.pack('<%df' % len(values), *values)


def stats(values):
    assert values and all(math.isfinite(x) for x in values), 'empty/nonfinite audio'
    return {'peak': max(abs(x) for x in values),
            'rms': math.sqrt(sum(x*x for x in values) / len(values)),
            'mean': sum(values) / len(values),
            'left_rms': math.sqrt(sum(x*x for x in values[::2]) / (len(values)//2)),
            'right_rms': math.sqrt(sum(x*x for x in values[1::2]) / (len(values)//2))}


class Native:
    def __init__(self):
        self.dll = C.CDLL(str(DLL_PATH))
        types = {'void': None, 'int': C.c_int, 'float': C.c_float, 'double': C.c_double,
                 'IntPtr': C.c_void_p, 'float[]': C.POINTER(C.c_float), 'int[]': C.POINTER(C.c_int),
                 'TrackerCellData[]': C.POINTER(Cell), 'ZTrackerEventData': C.POINTER(Event),
                 'NativeEnvPointData[]': C.POINTER(EnvPoint), 'string': C.c_char_p}
        self.signatures = {}
        text = (Path(__file__).resolve().parent / 'native_abi.cs').read_text(encoding='utf-8-sig')
        for ret, name, params in re.findall(r'public static extern (\w+) (\w+)\((.*?)\);', text, re.S):
            pairs = [p.strip().split()[-2:] for p in params.split(',')] if params.strip() else []
            fn = getattr(self.dll, name)
            fn.restype = types[ret]
            fn.argtypes = [types[t] for t, _ in pairs]
            self.signatures[name] = pairs
        assert self.dll.ZT_GetABIVersion() == 1
        assert (C.sizeof(Cell), C.sizeof(EnvPoint), C.sizeof(Event)) == (6, 12, 112)
        assert Event.samplePosition.offset == 8 and Event.stringPayload.offset == 44


class Recorder:
    """Every case owns one context, destroyed even when an assertion fails."""
    def __init__(self, native, buffer):
        self.native = native
        self.ctx = native.dll.ZT_Create(RATE)
        assert self.ctx, 'ZT_Create failed'
        self.buffer = buffer
        self.frame = 0
        self.audio, self.events, self.controls, self.calls = [], [], [], []
        self.render_blocks = []
        self.max_drain = 0
        self.sources = []

    def close(self):
        self.native.dll.ZT_Destroy(self.ctx)
        self.ctx = None

    def call(self, name, *args):
        # Arrays have their exact bytes fingerprinted rather than rounded JSON floats.
        def describe(x):
            if isinstance(x, C.Array):
                return {'count': len(x), 'ctype': type(x)._type_.__name__, 'sha256': sha(bytes(x))}
            if isinstance(x, bytes):
                return x.decode('utf-8')
            return x
        self.calls.append({'hostFrame': self.frame, 'function': name, 'args': [describe(x) for x in args]})
        return getattr(self.native.dll, name)(self.ctx, *args)

    def named(self, name, **kwargs):
        return self.call(name, *[kwargs[n] for _, n in self.native.signatures[name] if n != 'ctx'])

    def drain(self):
        ev = Event()
        count = 0
        while self.native.dll.ZT_PollEvent(self.ctx, C.byref(ev)):
            count += 1
            assert count < 4095, 'event queue at capacity: possible unreported overflow'
            item = {name: getattr(ev, name) for name, _ in Event._fields_}
            item['stringPayload'] = item['stringPayload'].decode('utf-8', errors='strict')
            assert 0 <= item['type'] <= 9 and 0 <= item['samplePosition'] < 2**64
            assert math.isfinite(item['floatParam'])
            assert not self.events or item['samplePosition'] >= self.events[-1]['samplePosition']
            self.events.append(item)
        self.max_drain = max(self.max_drain, count)

    def render(self, frames):
        while frames:
            n = min(frames, self.buffer)
            left, right = (C.c_float*n)(), (C.c_float*n)()
            self.render_blocks.append({'hostFrame':self.frame,'frames':n})
            self.native.dll.ZT_Process(self.ctx, left, right, n)
            assert all(math.isfinite(x) for x in left) and all(math.isfinite(x) for x in right)
            self.audio.extend(v for pair in zip(left, right) for v in pair)
            self.frame += n
            frames -= n
            self.drain()

    def observe(self, label, channels=(0,)):
        obs = {'provenance': 'harness block-boundary query', 'hostFrame': self.frame, 'label': label,
               'activeCount': sum(self.native.dll.ZT_IsVoiceActive(self.ctx, i) for i in range(128)),
               'channels': []}
        for ch in channels:
            voice = self.native.dll.ZT_GetChannelVoiceID(self.ctx, ch)
            obs['channels'].append({'channel': ch, 'voice': voice,
                'pitch': self.native.dll.ZT_GetVoiceCurrentPitch(self.ctx, voice),
                'preset': self.native.dll.ZT_GetChannelPreset(self.ctx, ch),
                'instrument': self.native.dll.ZT_GetChannelInstrument(self.ctx, ch),
                'macros': [self.native.dll.ZT_GetChannelMacro(self.ctx, ch, m) for m in range(4)]})
        self.controls.append(obs)
        return obs

    def note(self, instrument=0, note=69, velocity=1):
        voice = self.call('ZT_NoteOn', instrument, note, velocity)
        self.controls.append({'provenance': 'harness direct injection', 'hostFrame': self.frame,
                              'action': 'noteOn', 'instrument': instrument, 'note': note,
                              'velocity': velocity, 'returnedVoice': voice})
        return voice

    def load(self, name='A', length=4096, stereo=False, rate=RATE, constant=False):
        # Explicit integer phase periods; no random library or external fixtures.
        left = [(.2 if constant else .45*math.sin(math.tau*i/97) + .12*math.sin(math.tau*i/31)) for i in range(length)]
        right = [.3*math.cos(math.tau*i/73) for i in range(length)] if stereo else None
        l, r = floats(left), floats(right) if right else None
        sid = self.call('ZT_LoadSample', l, r, length, rate, 2 if stereo else 1)
        assert sid >= 0, 'sample load rejected'
        self.sources.append({'name': name, 'sampleID': sid, 'frames': length, 'sampleRate': rate,
                             'channels': 2 if stereo else 1, 'left_sha256': sha(bytes(l)),
                             'right_sha256': sha(bytes(r)) if r is not None else None,
                             'formula': 'constant .2' if constant else 'A=.45*sin(tau*i/97)+.12*sin(tau*i/31); R=.3*cos(tau*i/73)'})
        return sid

    def song(self, rows, bpm=120, lpb=4, patterns=None, order=None, event_track=False):
        channels = len(rows[0])
        self.call('ZT_SetSongTempo', bpm, 6)
        self.call('ZT_SetLinesPerBeat', lpb)
        self.call('ZT_SetChannelCount', channels)
        self.call('ZT_SetBeatTickInterval', 4)
        for pid, data in enumerate(patterns or [rows]):
            cells = (Cell*(len(data)*channels))(*[Cell(*cell) for row in data for cell in row])
            self.call('ZT_SetPatternData', pid, cells, len(data), channels)
        order = order or list(range(len(patterns or [rows])))
        self.call('ZT_SetOrderList', (C.c_int*len(order))(*order), len(order))
        if event_track:
            self.call('ZT_SetChannelType', channels-1, 1)
            for row in range(len(rows)):
                self.call('ZT_SetEventString', 0, row, channels-1, ('row-%d' % row).encode())

    def play(self):
        self.call('ZT_Play', 0, 0)
        self.drain()
