"""Readable case specifications. All native settings are also captured in the manifest."""
import ctypes as C
import math
from native_golden import EnvPoint, floats

EMPTY = (-1, -1, 255, 0, 0)
OFF = (127, -1, 255, 0, 0)


def cell(note=-1, inst=-1, cmd=0, param=0, volume=255):
    return (note, inst, volume, cmd, param)


def instrument(r, kind='synth', id=0, **changes):
    if kind == 'sample':
        sid = r.load(stereo=changes.pop('stereo', False), length=changes.pop('length', 4096),
                     rate=changes.pop('source_rate', 48000), constant=changes.pop('constant', False))
        mode = changes.pop('loop', 1)
        r.call('ZT_SetSampleLoop', sid, mode, 128, 3072 if r.sources[-1]['frames'] > 3072 else r.sources[-1]['frames'])
        if mode == 3:
            r.call('ZT_SetSampleSustainLoop', sid, 512, 1536)
        b = -1
        if changes.pop('sample_b', False):
            b = r.load('B', stereo=True, rate=32000)
            r.call('ZT_SetSampleLoop', b, 1, 256, 3000)
        settings = dict(id=id, sampleID=sid, baseNote=69, fineTune=0, volume=.3, pan=0,
                        attack=.001, decay=.015, sustain=.8, release=.025,
                        sampleIDB=b, baseNoteB=65, fineTuneB=17, blendMode=0, blendDefault=.4,
                        pmDepth=17, blendEnvelopeEnabled=0, blendAttack=.01, blendDecay=.02,
                        blendSustain=.3, blendRelease=.02)
        settings.update(changes)
        r.named('ZT_SetSampleInstrument', **settings)
    elif kind in ('synth', 'unison'):
        settings = dict(id=id, waveA=0, waveB=2, blendMode=0, blendDefault=.35, pmDepth=.21,
                        waveBRatio=1.5, blendEnvelopeEnabled=0, blendAttack=.003, blendDecay=.02,
                        blendSustain=.3, blendRelease=.02, unisonVoices=3 if kind == 'unison' else 1,
                        unisonDetune=19, unisonSpread=.8, volume=.3, pan=0, attack=.001,
                        decay=.015, sustain=.8, release=.025, pulseWidth=.31)
        settings.update(changes)
        r.named('ZT_SetSynthInstrument', **settings)
    elif kind == 'fm':
        r.call('ZT_SetFMInstrument', id, changes.get('algorithm', 0), .23, .3, 0)
        for op, ratio in enumerate((1, 2.3, 3.7, .5)):
            r.call('ZT_SetFMOperator', id, op, ratio, 173 if op == 3 else 0, .7-.1*op, 0,
                   .001*(op+1), .03, .65+.05*op, .025)
    elif kind == 'kit':
        sid = r.load(length=changes.get('length', 12000))
        r.call('ZT_SetKitInstrument', id)
        r.call('ZT_SetKitOverlap', id, 1)
        for note, pan in ((36, -.35), (38, .35), (69, 0)):
            r.call('ZT_SetKitEntry', id, note, sid, 60, .3, pan, .001, .01, .8, .025)


def envelope(r, target, loop=None):
    ranges = {0:(.05,.9,.2), 1:(.15,.8,.3), 2:(.5,3,1), 3:(.02,.7,.13), 4:(-100,100,-20)}
    a,b,d = ranges[target]
    pts = (EnvPoint*3)(EnvPoint(0,a,1), EnvPoint(.035,b,2), EnvPoint(.09,d,.5))
    r.call('ZT_SetInstrumentEnvelope', 0, target, 1, pts, 3, .09,
           int(loop is not None), loop or 0, .015, .07)


def cases():
    out = []
    def add(name, description, kind='synth', frames=16000, buffer=333, defects=(), **spec):
        out.append(dict(id=name, description=description, kind=kind, frames=frames,
                        buffer=buffer, approved_defects=list(defects), **spec))
    add('sample_mono', 'Looping mono, pitched velocity note and release', 'sample')
    add('sample_stereo_tuned', 'Stereo source-rate conversion, fine tune and explicit velocity', 'sample',
        settings={'stereo':True, 'source_rate':32000, 'fineTune':31}, note=74, velocity=.625)
    for mode in (0,1,2,3):
        add('sample_loop_%d'%mode, 'Direct normal loop setter mode %d; held then released'%mode, 'sample',
            settings={'loop':mode}, release_at=8000)
    for wave in range(5):
        add('synth_wave_%d'%wave, 'Native wave %d (sine/square/saw/reverse-saw/triangle)'%wave,
            settings={'waveA':wave, 'blendDefault':0})
    for mode in range(4):
        add('synth_blend_%d'%mode, 'Oscillator blend mode %d and blend ADSR'%mode,
            settings={'blendMode':mode, 'blendEnvelopeEnabled':1})
        add('sample_b_%d'%mode, 'Stereo B source with independent tuning/loop; blend mode %d'%mode, 'sample',
            settings={'sample_b':True, 'blendMode':mode, 'blendEnvelopeEnabled':1}, envelopes=[0,3])
    for count in (3,8):
        add('synth_unison_%d'%count, 'Detuned stereo unison with %d oscillators'%count, 'unison',
            settings={'unisonVoices':count})
    for algorithm in range(6):
        add('fm_algorithm_%d'%algorithm, 'Four FM operators, feedback, fixed-Hz operator, algorithm %d'%algorithm,
            'fm', settings={'algorithm':algorithm})
    for algorithm in (6,7):
        add('defect_fm_%d'%algorithm, 'Unsupported FM index falls back to additive algorithm 5', 'fm',
            settings={'algorithm':algorithm}, defects=['fm_choices'])
    add('kit_mapped', 'Mapped drums fixed pitch, overlapping notes, unmapped rejection and release', 'kit', scenario='kit')
    for target in range(4):
        add('envelope_target_%d'%target, 'Three-point nonlinear envelope target %d'%target,
            settings={'waveA':1, 'blendMode':3}, envelopes=[target])
    add('envelope_control', 'Same synth settings without multipoint envelopes', settings={'waveA':1,'blendMode':3})
    for mode in (0,1,2):
        add('envelope_loop_%d'%mode, 'Native envelope loop %d (0 forward, 1 ping-pong, 2 falls forward)'%mode,
            settings={'waveA':1}, envelopes=[1], env_loop=mode,
            defects=['envelope_mapping'] if mode==2 else [])
    add('modulation', 'Deterministic instrument vibrato and curved arpeggio', modulation=True)
    add('detune_control', 'Unison control without detune envelope', 'unison')
    add('defect_detune', 'Enabled detune envelope has no effect on current unison', 'unison',
        envelopes=[4], defects=['detune_envelope'])
    for enabled in (False,True):
        add('portamento_%s'%('enabled' if enabled else 'control'), 'Sequential notes with instrument glide %s'%enabled,
            scenario='portamento', glide=enabled, defects=['instrument_glide'] if enabled else [])
    for kind in ('sample','synth','unison','fm','kit'):
        for label, gain, pan in (('base',1,0),('mute',0,0),('half',.5,0),('left',1,-1),('right',1,1)):
            add('channel_%s_%s'%(kind,label), 'Channel %s %s, initial and subsequent rows'%(kind,label), kind,
                frames=13000, scenario='channel', gain=gain, pan=pan)
    for mode in (0,1,2):
        add('master_filter_%d'%mode, 'Master filter LP/HP/BP mode %d'%mode, 'sample', master={'filter':mode})
    for fx in ('dry','delay','reverb','combined'):
        add('master_'+fx, 'Master %s insert and tail; instrument sends zero'%fx, 'sample', frames=24000,
            release_at=6000, master={fx:True})
    for buffer in (64,333,1024,1333):
        add('defect_send_%d'%buffer, 'Tick-split delay/reverb sends, buffer %d'%buffer, 'sample',
            frames=8000, buffer=buffer, settings={'constant':True}, sends=True, defects=['send_subchunks'])
    for filtered in (False,True):
        add('kit_reuse_%s'%('filtered' if filtered else 'control'), 'Kit reuses a completed sample voice with prior filter %s'%filtered,
            'kit', scenario='kit_reuse', filtered=filtered, defects=['kit_filter_leak'] if filtered else [])
    add('kit_fresh', 'Fresh kit reference for the kit-only segment after voice reuse', 'kit',
        scenario='kit_fresh', frames=15872)
    for label, send, buffer in (('dry',0,1333),('split',.5,1000),('tick_split',.5,1333)):
        add('send_boundary_'+label, 'Constant source isolates delay-send alignment at tick1000: '+label,
            'sample', scenario='send_boundary', frames=1333, buffer=buffer, delay_send=send,
            settings={'constant':True,'attack':0,'decay':0,'sustain':1},
            defects=['send_subchunks'] if label=='tick_split' else [])
    for kind in ('synth','sample'):
        add('presets_'+kind, 'Base, variants 1/2, remembered user index and invalid fallback', kind,
            scenario='presets', frames=25000)
    add('voice_stealing', '160 active looping sample requests; quietest and release-priority stealing', 'sample',
        frames=12000, scenario='steal', settings={'release':.2})
    for cmd in (1,2,3,4,7,8,10,12,16,17,18):
        add('command_%02X'%cmd, 'Command byte %d / hex %02X, tick-zero/nonzero, continuation/zero, OFF'%(cmd,cmd),
            scenario='command', command=cmd, frames=25000)
        if cmd in (1,2,4,7,8,10,12):
            add('command_%02X_control'%cmd, 'Disabled-command control for byte %d'%cmd,
                scenario='command', command=cmd, neutral=True, frames=25000)
    add('command_0A_up', 'Gain slide up from .4 initial gain, nonzero ticks and zero reset',
        scenario='command', command=10, command_param=0x30, initial_gain=.4, frames=25000)
    add('command_08_existing', 'Pan setter leaves held voice unchanged, then affects next note',
        scenario='pan_existing', frames=25000)
    for cmd in (11,13):
        add('command_%02X'%cmd, 'Order jump' if cmd==11 else 'Pattern break with raw row byte 0x12 (=18)',
            scenario='flow', command=cmd, frames=13000)
    add('command_jump_break', 'Combined jump and raw-row break on independent channels', scenario='flow', command=0)
    add('command_0F', 'Tempo byte 150, TPL3, F00 => TPL1 and exact tempo events', scenario='tempo', frames=20000)
    for bpm,lpb,label in ((120,4,'120'),(275,2,'137p5')):
        for buffer in (64,333,1024):
            add('clock_%s_b%d'%(label,buffer), 'Exact native event clock and event-track strings at requested '+label,
                scenario='clock', bpm=bpm, lpb=lpb, buffer=buffer, frames=27000)
    add('transport_restart', 'Cumulative playing-only counter versus host frames on stop/idle/restart', scenario='restart')
    for wave in (5,6):
        add('noise_%d'%wave, 'Legacy rand noise: statistical-only, nonportable, excluded from numeric parity',
            frames=4096, settings={'waveA':wave, 'blendDefault':0}, statistical_only=True, defects=['random_noise'])
    add('noise_vibrato_jitter', 'Legacy random vibrato rate: statistical-only nonportable reference',
        frames=4096, jitter=True, statistical_only=True, defects=['random_noise'])
    return out


def execute(r, spec):
    kind, scenario = spec['kind'], spec.get('scenario','direct')
    if scenario=='kit_reuse':
        instrument(r,'sample',id=1,length=64,loop=0)
        r.call('ZT_SetInstrumentEffects',1,int(spec['filtered']),0,.06,.7,0,0)
        old=r.note(1)
        r.render(128)
        assert not r.native.dll.ZT_IsVoiceActive(r.ctx,old)
        instrument(r,'kit')
        voice=r.note(0,69)
        assert voice==old==0, 'kit contamination witness must reuse same slot'
        r.render(spec['frames']-r.frame)
        return
    instrument(r,kind,**spec.get('settings',{}))
    if scenario=='kit_fresh':
        assert r.note(0,69)==0
        r.render(spec['frames'])
        return
    if scenario=='send_boundary':
        r.call('ZT_SetInstrumentEffects',0,0,0,.5,.7,spec['delay_send'],0)
        r.song([[cell(69,0)]]+[[EMPTY] for _ in range(7)])
        r.play()
        r.render(spec['frames'])
        return
    for target in spec.get('envelopes',[]):
        envelope(r,target,spec.get('env_loop'))
    if spec.get('modulation'):
        r.call('ZT_SetInstrumentVibrato',0,35,5,.03,0)
        r.call('ZT_SetInstrumentArpeggio',0,1,(C.c_int*3)(0,4,7),3,1,
               floats([0,.1]),floats([.015,.035]),2)
    if spec.get('jitter'):
        r.call('ZT_SetInstrumentVibrato',0,70,7,0,.6)
    if spec.get('sends'):
        r.call('ZT_SetInstrumentEffects',0,0,0,.5,.7,.5,.3)
    master=spec.get('master',{})
    if 'filter' in master: r.call('ZT_SetMasterFilter',master['filter'],.12,.8)
    if master.get('delay') or master.get('combined'): r.call('ZT_SetDelayParams',.027,.4,.3)
    if master.get('reverb') or master.get('combined'): r.call('ZT_SetReverbParams',.65,.3,.3)
    if scenario=='steal':
        voices=[]
        for i in range(128):
            v=r.note(0,48+i%36,.05 if i==0 else .8)
            assert v==i, 'distinct active pool slots required'
            voices.append(v)
            r.render(64)
        assert r.observe('full-pool')['activeCount']==128
        v=r.note(0,90,.8)
        assert v==0, 'quietest active voice should be stolen'
        assert r.native.dll.ZT_GetVoiceCurrentNote(r.ctx,0)==90
        r.render(64)
        r.call('ZT_NoteOff',17)
        r.render(128)
        r.call('ZT_NoteOff',18)
        v=r.note(0,91,.8)
        assert v==17, 'quieter released voice should be stolen before active voices'
        assert r.native.dll.ZT_GetVoiceCurrentNote(r.ctx,17)==91
        r.render(64)
        for i in range(30):
            assert 0<=r.note(0,60+i%24,.8)<128
            r.render(64)
        assert r.observe('after-160-requests')['activeCount']==128
        r.render(spec['frames']-r.frame)
        return
    if scenario=='kit':
        assert r.note(0,37)==-1, 'unmapped kit note must reject'
        v=r.note(0,36)
        r.render(1000)
        v2=r.note(0,38)
        assert v!=v2 and r.native.dll.ZT_IsVoiceActive(r.ctx,v)
        assert r.native.dll.ZT_GetVoiceCurrentPitch(r.ctx,v)==r.native.dll.ZT_GetVoiceCurrentPitch(r.ctx,v2)==1
        r.render(2000)
        r.call('ZT_NoteOff',v)
        r.render(spec['frames']-r.frame)
        assert r.observe('kit-natural-end')['activeCount']==0
        return
    if scenario=='direct':
        if spec.get('sends'):
            # Transport creates actual mid-block subchunks (idle voice renders do not).
            rows=[[cell(69,0)]]+[[EMPTY] for _ in range(7)]
            r.song(rows); r.play()
            r.render(spec['frames']); return
        voice=r.note(0,spec.get('note',69),spec.get('velocity',1))
        assert voice>=0
        release=spec.get('release_at',10000)
        release=min(release,spec['frames'])
        r.render(release)
        r.controls.append({'provenance':'harness direct query','hostFrame':r.frame,
                           'label':'held','active':r.native.dll.ZT_IsVoiceActive(r.ctx,voice),
                           'pitch':r.native.dll.ZT_GetVoiceCurrentPitch(r.ctx,voice)})
        if spec.get('settings',{}).get('loop',1)!=0:
            assert r.native.dll.ZT_IsVoiceActive(r.ctx,voice), 'held loop/synth/FM must survive'
        r.call('ZT_NoteOff',voice)
        r.render(spec['frames']-r.frame)
        return
    if scenario in ('presets','command') and (scenario=='presets' or spec['command']==18):
        for index,vol in ((10,.12),(11,.28),(12,.45)):
            instrument(r,kind,id=index,volume=vol)
        r.call('ZT_SetPresetMap',0,10,2)
    rows=[[cell(69,0)]]+[[EMPTY] for _ in range(7)]
    patterns=None
    if scenario=='channel':
        rows=[[cell(69,0)] for _ in range(4)]
    elif scenario=='presets':
        rows=[[cell(69,0 if i==0 else -1,18,p)] for i,p in enumerate((0,1,2,9))]+[[OFF]]
    elif scenario=='portamento':
        r.call('ZT_SetInstrumentPortamento',0,int(spec['glide']),.12,1)
        rows=[[cell(60,0)],[cell(72,0)],[EMPTY],[OFF],[EMPTY]]
    elif scenario=='command':
        cmd=spec['command']
        params={1:80,2:80,3:64,4:0x68,7:0x68,8:255,10:0x03,12:24,16:0x1C,17:0x1F,18:1}
        param=spec.get('command_param',params[cmd])
        applied=0 if spec.get('neutral') else cmd
        rows=[[cell(60,0,applied,param)],[cell(72 if cmd in (3,8,18) else -1,-1,applied,param)],
              [cell(cmd=cmd,param=0)],[OFF],[EMPTY],[EMPTY]]
    elif scenario=='pan_existing':
        rows=[[cell(60,0)],[cell(cmd=8,param=255)],[cell(60,0)],[OFF],[EMPTY]]
    elif scenario=='flow':
        cmd=spec['command']
        if cmd==0:
            rows=[[cell(60,0,11,1),cell(cmd=13,param=18)]]+[[EMPTY,EMPTY] for _ in range(23)]
            second=[[cell(69,0),EMPTY] for _ in range(24)]
        else:
            rows=[[cell(60,0,cmd,1 if cmd==11 else 18)]]+[[EMPTY] for _ in range(23)]
            second=[[cell(69,0)] for _ in range(24)]
        patterns=[rows,second]
    elif scenario=='tempo':
        rows=[[cell(69,0,15,150)],[cell(cmd=15,param=3)],[cell(cmd=15,param=0)],[OFF]]+[[EMPTY] for _ in range(4)]
    if scenario=='clock':
        rows=[[cell(69,0) if i==0 else OFF if i==3 else EMPTY,EMPTY] for i in range(8)]
    r.song(rows,bpm=spec.get('bpm',120),lpb=spec.get('lpb',4),patterns=patterns,event_track=scenario=='clock')
    if scenario=='channel':
        r.call('ZT_SetChannelVolume',0,spec['gain']); r.call('ZT_SetChannelPan',0,spec['pan'])
    if 'initial_gain' in spec:
        r.call('ZT_SetChannelVolume',0,spec['initial_gain'])
    r.play()
    # Query exactly at tick/row boundaries. These are host observations, never event timestamps.
    checkpoints=[1,999,1000,2000,5000,6000,7000,11000,12000,18000,24000]
    if scenario in ('command','presets','tempo','flow','portamento','pan_existing'):
        for frame in checkpoints:
            if frame<=spec['frames']:
                r.render(frame-r.frame); r.observe('boundary-%d'%frame)
    if scenario=='restart':
        r.render(6000); r.call('ZT_Stop'); r.drain(); r.render(1000)
        r.call('ZT_Play',0,0); r.drain(); r.render(spec['frames']-r.frame)
        starts=[e['samplePosition'] for e in r.events if e['type']==2]
        assert starts==[0,6000]
        return
    r.render(spec['frames']-r.frame)
    assert any(e['type']==2 for e in r.events) and any(e['type']==0 for e in r.events)
    if scenario=='clock':
        expected=[0,6000,12000,18000,24000] if spec['bpm']==120 else [0,5237,10473,15710,20946,26182]
        actual=[e['samplePosition'] for e in r.events if e['type']==0]
        assert actual==expected,(spec['id'],actual,expected)
        assert len([e for e in r.events if e['type']==8])==len(expected), 'event-track loss/overflow'
    if scenario=='flow':
        row_events=[e for e in r.events if e['type']==0]
        assert row_events[1]['patternIndex']==1 and row_events[1]['rowIndex']==(0 if spec['command']==11 else 18)
    if scenario=='presets':
        assert [o['channels'][0]['preset'] for o in r.controls if o.get('label') in ('boundary-1','boundary-6000','boundary-12000','boundary-18000')]==[0,1,2,9]
    if scenario=='command' and spec['command']==3:
        assert len([e for e in r.events if e['type']==5])==1, 'glide should retain voice'
        pitches=[o['channels'][0]['pitch'] for o in r.controls if o.get('label') in ('boundary-6000','boundary-11000')]
        assert pitches[1]>pitches[0], 'command03 must actively glide'
    if scenario=='command' and spec['command']==16:
        assert abs(r.controls[0]['channels'][0]['macros'][1]-.8)<1e-6
    if scenario=='command' and spec['command']==17:
        assert r.controls[0]['channels'][0]['macros'][1]==0
        assert r.controls[3]['channels'][0]['macros'][1]>0
    if scenario=='command' and spec['command'] in (1,2) and not spec.get('neutral'):
        initial=r.controls[0]['channels'][0]['pitch']
        later=r.controls[3]['channels'][0]['pitch']
        assert later>initial if spec['command']==1 else later<initial
        assert r.controls[2]['channels'][0]['pitch']==initial, 'first tick target smooths on subsequent audio'
    if scenario=='pan_existing':
        assert r.audio[14000:20000:2]==r.audio[14001:20000:2], '08 must not repan held voice'
        assert all(x==0 for x in r.audio[14000*2:16000*2:2]), 'next note must use new hard-right pan after prior release'
