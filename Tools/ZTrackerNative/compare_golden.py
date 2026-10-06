"""Numeric corpus comparison library/CLI. A native self-match is not Burst parity."""
import argparse
from array import array
import json
import math
from pathlib import Path
import sys

FORMAT = {'encoding':'float32', 'endianness':'little', 'channels':2, 'layout':'interleaved-LR'}
EVENT_FIELDS = {'type','samplePosition','patternIndex','rowIndex','channelIndex','noteValue',
                'instrumentID','intParam','floatParam','stringPayload'}
APPROVED_DEFECTS=frozenset({'detune_envelope','envelope_mapping','fm_choices','instrument_glide',
                         'kit_filter_leak','random_noise','send_subchunks','tremolo_ignored'})


def load_corpus(directory):
    """Validate files, metadata, event integer precision and every audio sample before comparing."""
    directory=Path(directory)
    manifest=json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
    if manifest.get('schema_version')!=1 or manifest.get('format')!=FORMAT or manifest.get('sample_rate')!=48000:
        raise ValueError('unsupported schema/rate/layout')
    result={}
    files=set()
    for case in manifest['cases']:
        cid=case['id']
        if cid in result or not isinstance(cid,str) or any(x in cid for x in ('/','\\','..')):
            raise ValueError('duplicate/unsafe case ID')
        if type(case['frames']) is not int or case['frames']<=0 or case.get('buffer',0)<=0:
            raise ValueError(cid+': invalid shape/buffer')
        paths=[case['audio_file'],case['events_file']]
        if paths!=[cid+'.f32',cid+'.events.json']:
            raise ValueError(cid+': unexpected filenames')
        files.update(paths)
        raw=(directory/paths[0]).read_bytes()
        if len(raw)!=case['frames']*8:
            raise ValueError(cid+': audio shape mismatch')
        values=array('f'); values.frombytes(raw)
        if sys.byteorder!='little': values.byteswap()
        if not all(math.isfinite(x) for x in values):
            raise ValueError(cid+': nonfinite audio')
        trace=json.loads((directory/paths[1]).read_text(encoding='utf-8'))
        events=trace['native_events']
        previous=-1
        for event in events:
            if set(event)!=EVENT_FIELDS:
                raise ValueError(cid+': event fields mismatch')
            pos=event['samplePosition']
            if type(pos) is not int or not 0<=pos<2**64 or pos<previous:
                raise ValueError(cid+': invalid uint64 event position')
            previous=pos
            for field in EVENT_FIELDS-{'samplePosition','floatParam','stringPayload'}:
                if type(event[field]) is not int: raise ValueError(cid+': event integer field malformed')
            if not 0<=event['type']<=9 or not isinstance(event['stringPayload'],str):
                raise ValueError(cid+': malformed event')
            if not isinstance(event['floatParam'],(int,float)) or not math.isfinite(event['floatParam']):
                raise ValueError(cid+': nonfinite event parameter')
        result[cid]=(case,values,events,trace)
    actual={p.name for p in directory.iterdir() if p.name.endswith(('.f32','.events.json'))}
    if actual!=files: raise ValueError('missing/extra case artifact files: '+str(sorted(actual^files)))
    if not result: raise ValueError('empty corpus')
    return manifest,result


def compare(reference, candidate, atol=1e-5, rtol=1e-4, event_frame_tolerance=0,
            exemptions=None, correction_checks=None):
    """Exemptions map case IDs to exact approved defect IDs; correction callbacks must verify corrected behavior.

    Without a supplied callback an exempted numeric mismatch remains unresolved (CLI exit 2).
    Statistical-only noise is deliberately excluded from numeric parity and explicitly reported.
    """
    if not math.isfinite(atol) or not math.isfinite(rtol) or min(atol,rtol)<0:
        raise ValueError('tolerances must be finite and nonnegative')
    if type(event_frame_tolerance) is not int or event_frame_tolerance<0:
        raise ValueError('event tolerance must be nonnegative integer frames')
    _,ref=load_corpus(reference); _,got=load_corpus(candidate)
    if ref.keys()!=got.keys(): raise ValueError('missing/extra cases: '+str(sorted(ref.keys()^got.keys())))
    exemptions=exemptions or {}; correction_checks=correction_checks or {}
    if exemptions.keys()-ref.keys(): raise ValueError('unknown exempted case')
    reports=[]
    for cid,(rc,a,events,_) in ref.items():
        gc,b,other,trace=got[cid]
        for field in ('frames','buffer','clock'):
            if rc[field]!=gc[field]: raise ValueError(cid+': '+field+' mismatch')
        exemption=exemptions.get(cid)
        if exemption and (exemption not in APPROVED_DEFECTS or (exemption=='tremolo_ignored' and cid!='command_07')):
            raise ValueError(cid+': unknown or incorrectly scoped approved defect')
        if exemption and exemption not in rc.get('approved_defects',[]):
            raise ValueError(cid+': exemption is not an approved defect for this case')
        event_errors=[]
        if len(events)!=len(other): event_errors.append('event count mismatch')
        for index,(x,y) in enumerate(zip(events,other)):
            if abs(x['samplePosition']-y['samplePosition'])>event_frame_tolerance:
                event_errors.append('event %d samplePosition mismatch'%index)
            if any(x[k]!=y[k] for k in EVENT_FIELDS-{'samplePosition'}):
                event_errors.append('event %d payload mismatch'%index)
        state_errors=[]
        # G/H lack native audio mapping: audio-only comparison cannot establish command parity.
        if rc.get('scenario')=='presets' or (rc.get('scenario')=='command' and rc.get('command') in (16,17,18)):
            def observations(t):
                return {o['hostFrame']:o['channels'] for o in t.get('harness_controls',[]) if 'channels' in o}
            expected=observations(ref[cid][3]); actual=observations(trace)
            if expected.keys()!=actual.keys():
                state_errors.append('missing/extra macro/preset observation boundaries')
            for frame in expected.keys() & actual.keys():
                if len(expected[frame])!=len(actual[frame]):
                    state_errors.append('channel observation count mismatch'); continue
                for x,y in zip(expected[frame],actual[frame]):
                    if any(x[k]!=y.get(k) for k in ('channel','preset','instrument')):
                        state_errors.append('preset/user-instrument state mismatch at frame%d'%frame)
                    macros=y.get('macros',[])
                    if len(macros)!=4 or any(not isinstance(v,(int,float)) or not math.isfinite(v) for v in macros):
                        state_errors.append('malformed macro observations'); continue
                    if any(abs(u-v)>atol+rtol*abs(u) for u,v in zip(x['macros'],macros)):
                        state_errors.append('macro state mismatch at frame%d'%frame)
        max_error=0; sum_sq=0; first=None; bad=0
        for i,(x,y) in enumerate(zip(a,b)):
            error=abs(x-y); max_error=max(max_error,error); sum_sq+=error*error
            if error>atol+rtol*abs(x):
                bad+=1
                if first is None: first={'frame':i//2,'channel':i%2,'reference':x,'candidate':y,'absolute_error':error}
        status='PASS'
        if rc.get('statistical_only'):
            status='STATISTICAL_ONLY_EXCLUDED'
            # Wide sanity gate, not a calibrated spectrum or portable noise parity claim.
            rms=math.sqrt(sum(x*x for x in b)/len(b))
            if not 1e-4<rms<1 or max(abs(x) for x in b)>4:
                status='FAIL_STATISTICAL_SANITY'
        elif exemption:
            callback=correction_checks.get(cid)
            status=('APPROVED_CORRECTION_VERIFIED' if callback and callback(gc,b,other,trace)
                    else 'EXPECTED_DIVERGENCE_REQUIRES_CORRECTION')
        elif bad:
            status='FAIL_AUDIO'
        if event_errors: status='FAIL_EVENTS'
        if state_errors: status='FAIL_STATE'
        reports.append({'id':cid,'status':status,'approved_exemption':exemption,'bad_samples':bad,
                        'max_absolute_error':max_error,'rms_error':math.sqrt(sum_sq/len(a)),
                        'first_offending_sample':first,'event_errors':event_errors,'state_errors':state_errors})
    success=all(x['status'] in ('PASS','STATISTICAL_ONLY_EXCLUDED','APPROVED_CORRECTION_VERIFIED') for x in reports)
    return {'success':success,'claim':'corpus comparison only; does not certify Burst parity',
            'atol':atol,'rtol':rtol,'event_frame_tolerance':event_frame_tolerance,
            'numeric_cases':sum(x['status']!='STATISTICAL_ONLY_EXCLUDED' for x in reports), 'cases':reports}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate',type=Path)
    parser.add_argument('--reference',type=Path,default=Path(__file__).with_name('golden'))
    parser.add_argument('--atol',type=float,default=1e-5); parser.add_argument('--rtol',type=float,default=1e-4)
    parser.add_argument('--event-frame-tolerance',type=int,default=0)
    parser.add_argument('--exempt',action='append',default=[],metavar='CASE:DEFECT',
                        help='Explicit sample comparison exemption; remains unresolved without library correction callback')
    parser.add_argument('--report',type=Path)
    args=parser.parse_args()
    try:
        exemptions=dict(x.split(':',1) for x in args.exempt)
        result=compare(args.reference,args.candidate,args.atol,args.rtol,args.event_frame_tolerance,exemptions)
        if args.report:
            args.report.parent.mkdir(parents=True,exist_ok=True)
            args.report.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
        failures=[x['id']+': '+x['status'] for x in result['cases'] if x['status'] not in ('PASS','STATISTICAL_ONLY_EXCLUDED','APPROVED_CORRECTION_VERIFIED')]
        print('%s: %d cases; %d statistical-only; max error %.9g; no Burst certification'%(
            'PASS' if result['success'] else 'FAIL',len(result['cases']),
            sum(x['status']=='STATISTICAL_ONLY_EXCLUDED' for x in result['cases']),
            max(x['max_absolute_error'] for x in result['cases'])))
        for failure in failures: print(failure)
        return 0 if result['success'] else 2
    except (ValueError,KeyError,OSError,TypeError) as exc:
        print('INVALID CORPUS:',exc,file=sys.stderr); return 1


if __name__=='__main__':
    raise SystemExit(main())
