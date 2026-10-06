"""Generate current installed-DLL reference artifacts; does not import or execute verify.py."""
import argparse
import ctypes as C
import json
from pathlib import Path

from compare_golden import FORMAT
from golden_cases import cases, execute
from native_golden import Cell, EnvPoint, Event, DLL_PATH, Native, PACKAGE, Recorder, ROOT, RATE, encode, sha, stats

HERE=Path(__file__).resolve().parent


def write_json(path,value):
    path.write_bytes((json.dumps(value,indent=2,sort_keys=True,allow_nan=False)+'\n').encode('utf-8'))


def clock(spec):
    bpm,lpb=spec.get('bpm',120),spec.get('lpb',4)
    return {'native_bpm':bpm,'native_lpb':lpb,'initial_ticks_per_row':6,
            'requested_bpm':137.5 if bpm==275 else bpm,'requested_lpb':4,
            'row_frames':RATE*60/(bpm*lpb),'tick_frames':RATE*60/(bpm*lpb*6),
            'beat_grid':'native275/LPB2; requested137.5/LPB4 has identical row/tick clock, different beat grid' if bpm==275 else 'native',
            'beat_tick_interval_rows':4,'tempo_commands_override_initial':spec.get('scenario')=='tempo'}


def validate_pairs(records, audio):
    """Behavior assertions beyond finite/nonzero signal and individual scenario checks."""
    by_id={x['id']:x for x in records}
    evidence=[]
    def witness(label,condition):
        assert condition,label
        evidence.append(label)
    for kind in ('sample','synth','unison','fm','kit'):
        prefix='channel_'+kind+'_'
        base=audio[prefix+'base']; half=audio[prefix+'half']
        witness(kind+' channel mute initial/later rows', all(x==0 for x in audio[prefix+'mute']))
        witness(kind+' channel hard-left',all(x==0 for x in audio[prefix+'left'][1::2]))
        witness(kind+' channel hard-right',all(x==0 for x in audio[prefix+'right'][::2]))
        ratio=sum(abs(x) for x in half)/sum(abs(x) for x in base)
        witness(kind+' half channel gain ratio %.8f'%ratio,abs(ratio-.5)<1e-5)
        for pan in ('left','right'):
            witness(kind+' '+pan+' later-row signal',sum(abs(x) for x in audio[prefix+pan][12000*2:])>.01)
    witness('detune envelope enabled equals disabled (approved defect)',audio['defect_detune']==audio['detune_control'])
    witness('instrument glide enabled equals disabled (approved defect)',audio['portamento_enabled']==audio['portamento_control'])
    witness('FM unsupported 6/7 equal implemented algorithm5 (approved defect)',
            audio['defect_fm_6']==audio['defect_fm_7']==audio['fm_algorithm_5'])
    witness('envelope native mode2 equals forward0 (authoring mapping defect)',audio['envelope_loop_2']==audio['envelope_loop_0'])
    witness('envelope forward differs ping-pong',audio['envelope_loop_0']!=audio['envelope_loop_1'])
    for target in range(4):
        witness('multipoint envelope target%d changes audio versus disabled control'%target,
                audio['envelope_target_%d'%target]!=audio['envelope_control'])
    # Exclude predecessor audio: its filter already changes PCM even if kit initialization is corrected.
    kit_clean=audio['kit_reuse_control'][128*2:]
    kit_filtered=audio['kit_reuse_filtered'][128*2:]
    witness('fresh kit equals clean reused kit-only segment',audio['kit_fresh']==kit_clean)
    witness('kit reused filter changes kit-only PCM (approved defect)',kit_filtered!=kit_clean)
    witness('forward and ping-pong sample loops differ',audio['sample_loop_1']!=audio['sample_loop_2'])
    witness('held loop differs one-shot tail',sum(abs(x) for x in audio['sample_loop_1'][12000:16000])>0 and
            all(x==0 for x in audio['sample_loop_0'][12000:16000]))
    witness('sustain region changes held output',audio['sample_loop_3']!=audio['sample_loop_1'])
    witness('sample-B four blends produce distinct signals',len({by_id['sample_b_%d'%i]['audio_sha256'] for i in range(4)})==4)
    witness('six FM algorithms produce distinct signals',len({by_id['fm_algorithm_%d'%i]['audio_sha256'] for i in range(6)})==6)
    for name in ('delay','reverb','combined'):
        witness('master '+name+' changes signal and retains tail',audio['master_'+name]!=audio['master_dry'] and
                sum(abs(x) for x in audio['master_'+name][18000*2:])>1e-8)
    for label in ('120','137p5'):
        names=['clock_%s_b%d'%(label,b) for b in (64,333,1024)]
        witness(label+' exact events invariant across64/333/1024',len({by_id[n]['events_sha256'] for n in names})==1)
    witness('send subset PCM differs across block sizes (approved subchunk defect)',
            audio['defect_send_64']!=audio['defect_send_1333'])
    dry=audio['send_boundary_dry']
    split=audio['send_boundary_split']
    tick_split=audio['send_boundary_tick_split']
    witness('constant send control has stable nonzero dry signal',
            min(dry)>.01 and max(dry)-min(dry)<1e-6)
    witness('separate tick-aligned buffers retain 50-percent delay send throughout',
            all(abs(wet-1.5*base)<1e-6 for base,wet in zip(dry,split)))
    witness('single buffer loses send only after tick1000 (approved subchunk defect)',
            all(abs(wet-1.5*base)<1e-6 for base,wet in zip(dry[:2000],tick_split[:2000])) and
            all(abs(wet-base)<1e-6 for base,wet in zip(dry[2000:],tick_split[2000:])))
    for cmd in (1,2,4,8,10,12):
        witness('command%02X observably changes audio versus disabled control'%cmd,
                audio['command_%02X'%cmd]!=audio['command_%02X_control'%cmd])
    witness('command07 audio equals disabled control: volume-modifier render omission (new limitation)',
            audio['command_07']==audio['command_07_control'])
    return evidence


def generate(output):
    output=Path(output)
    if output.resolve()==(HERE/'golden').resolve():
        raise ValueError('Frozen P0 corpus is read-only; generate into .golden-rerun')
    output.mkdir(parents=True,exist_ok=True)
    specs=cases()
    expected={s['id']+suffix for s in specs for suffix in ('.f32','.events.json')}
    stale={p.name for p in output.iterdir() if p.name.endswith(('.f32','.events.json'))}-expected
    if stale: raise ValueError('output contains unrelated/stale cases; use an empty directory: '+str(stale))
    native=Native()
    records=[]; audio={}
    for spec in specs:
        r=Recorder(native,spec['buffer'])
        try:
            execute(r,spec)
            assert r.frame==spec['frames'],(spec['id'],r.frame)
            signal=stats(r.audio)
            if '_mute' not in spec['id']: assert signal['rms']>1e-5,(spec['id'],'silent')
            pcm=encode(r.audio)
            trace={'native_events':r.events,'harness_controls':r.controls,
                   'event_timeline':'native cumulative playing samples; direct controls use hostFrame'}
            audio_file=spec['id']+'.f32'; event_file=spec['id']+'.events.json'
            (output/audio_file).write_bytes(pcm); write_json(output/event_file,trace)
            record={**spec,'audio_file':audio_file,'events_file':event_file,'clock':clock(spec),
                    'audio_sha256':sha(pcm),'events_sha256':sha(json.dumps(r.events,sort_keys=True).encode()),
                    'trace_file_sha256':sha((output/event_file).read_bytes()),'signal_stats':signal,
                    'source_fixtures':r.sources,'native_calls':r.calls,'native_event_count':len(r.events),
                    'native_render_blocks':r.render_blocks,
                    'max_events_drained_per_block':r.max_drain}
            audio[spec['id']]=r.audio
            records.append(record)
        except Exception as exc:
            raise RuntimeError(spec['id']+': '+str(exc)) from exc
        finally: r.close()
    checks=validate_pairs(records,audio)
    fingerprints={str(p.relative_to(ROOT)).replace('\\','/'):sha(p.read_text(encoding='utf-8-sig').encode('utf-8') if p==HERE/'native_abi.cs' else p.read_bytes()) for p in
                  sorted(list((HERE/'include').glob('*.h'))+list((HERE/'src').rglob('*.cpp'))+
                         [HERE/'CMakeLists.txt',HERE/'native_abi.cs',
                          PACKAGE/'Editor/ZTrackerWindow.Instrument.cs'])}
    tooling={p.name:sha(p.read_bytes()) for p in (HERE/'native_golden.py',HERE/'golden_cases.py',
            HERE/'generate_golden.py',HERE/'compare_golden.py',HERE/'test_golden.py',HERE/'README.template.md')}
    manifest={'schema_version':1,'generator':{'name':'generate_golden.py','version':1,
               'tool_sha256':tooling,'fixed_seed':0,'seed_policy':'procedural periodic PCM, no PRNG; legacy noise unseedable/statistical-only'},
              'reference_engine':'archived offline native DLL; not Burst','sample_rate':RATE,'format':FORMAT,
              'dll':{'path':str(DLL_PATH.relative_to(ROOT)).replace('\\','/'),'sha256':sha(DLL_PATH.read_bytes()),
                     'bytes':DLL_PATH.stat().st_size,'abi_version':1,'build':'Release /O2 /fp:fast static MSVC CRT'},
              'abi':{'pointer_bytes':C.sizeof(C.c_void_p),'cell_bytes':C.sizeof(Cell),
                     'envelope_point_bytes':C.sizeof(EnvPoint),'event_bytes':C.sizeof(Event),
                     'struct_offsets':{t.__name__:{n:getattr(t,n).offset for n,_ in t._fields_} for t in (Cell,EnvPoint,Event)},
                     'bound_export_count':len(native.signatures)},
              'source_sha256':fingerprints,'audio_bytes':sum(s['frames']*8 for s in specs),
              'assertion_evidence':checks,'event_overflow_policy':'no exported counter; drain every block, reject capacity, assert exact clock/event counts; bounded cases far below queue capacity',
              'unresolved_reference_limitations':['command07 sets tremolo state but all render paths ignore volumeMods; no exported query for this modifier. Audible-active07 requirement cannot be met without forbidden engine changes. Not treated as preapproved exemption.'],
              'cases':records}
    write_json(output/'manifest.json',manifest)
    table=['| Case | Frames / block | Coverage | Comparison |','|---|---:|---|---|']
    for s in specs:
        policy='statistical only' if s.get('statistical_only') else 'explicit defect: '+', '.join(s['approved_defects']) if s['approved_defects'] else 'numeric + exact events'
        table.append('| %s | %d / %d | %s | %s |'%(s['id'],s['frames'],s['buffer'],s['description'],policy))
    readme=(HERE/'README.template.md').read_text(encoding='utf-8').replace('{{CASE_TABLE}}','\n'.join(table)).replace('{{CASE_COUNT}}',str(len(specs))).replace('{{AUDIO_BYTES}}',str(manifest['audio_bytes']))
    (output/'README.md').write_bytes(readme.encode('utf-8'))
    print('Generated %d cases, %d audio bytes, %d paired behavior assertions'%(len(specs),manifest['audio_bytes'],len(checks)))
    return manifest


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=HERE/'.golden-rerun')
    generate(parser.parse_args().output)
