"""Corpus behavior, repeat-generation and comparator failure-path verification, stdlib only."""
import argparse
from array import array
import json
import math
from pathlib import Path
import shutil
import unittest

from compare_golden import compare, load_corpus
from native_golden import sha

HERE=Path(__file__).resolve().parent
REFERENCE=HERE/'golden'
RERUN=HERE/'.golden-rerun'

# Two frozen source files had mixed LF/CRLF storage. These pairs pin both the
# original corpus provenance and its exact content after newline normalization.
MIXED_NEWLINE_SOURCES={
    'Tools/ZTrackerNative/include/Common.h': (
        '1e5bbd6636ddc77251afe990d48772c546f02ceeaf7661793f54135417ba43b0',
        '453af14cf3e6f4ea4f64d0af24a0002c3f787fc4db0a50b80f6a084ccce4940a'),
    'Tools/ZTrackerNative/src/Sequencer/Sequencer.cpp': (
        'abe43523efd7d0e0c62e966014c200813b59e46ec524bbe4ad441445dfd4c0fb',
        '00565573968ec081fe3e422150b18d1bbfca4d33ec690b91e709d13d612e9d05'),
}


class GoldenTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work=HERE/'.golden-tests'
        cls.work.mkdir(exist_ok=True)
        cls.candidate=cls.work/'candidate'
        shutil.copytree(REFERENCE,cls.candidate,dirs_exist_ok=True)
        cls.cid='clock_120_b64'

    def setUp(self):
        for path in REFERENCE.iterdir():
            if path.name in ('manifest.json',self.cid+'.f32',self.cid+'.events.json'):
                shutil.copy2(path,self.candidate/path.name)

    def change_audio(self,change):
        path=self.candidate/(self.cid+'.f32')
        values=array('f'); values.frombytes(path.read_bytes())
        change(values)
        path.write_bytes(values.tobytes())

    def change_trace(self,change):
        path=self.candidate/(self.cid+'.events.json')
        trace=json.loads(path.read_text(encoding='utf-8')); change(trace)
        path.write_text(json.dumps(trace),encoding='utf-8')

    def test_self_match(self):
        self.assertTrue(compare(REFERENCE,REFERENCE)['success'])

    def test_full_repeat_generation(self):
        original=json.loads((REFERENCE/'manifest.json').read_text(encoding='utf-8'))
        repeated=json.loads((RERUN/'manifest.json').read_text(encoding='utf-8'))
        # Authoring/comparator/docs evolved; P7 relocated the offline loader and
        # its ABI. Native source, DLL bytes, calls, PCM and event traces stay exact.
        for name in ('README.template.md','compare_golden.py','test_golden.py','native_golden.py','generate_golden.py'):
            original['generator']['tool_sha256'].pop(name)
            self.assertEqual(repeated['generator']['tool_sha256'].pop(name),sha((HERE/name).read_bytes()),name)
        editor='Assets/Packages/ZTracker/Editor/ZTrackerWindow.Instrument.cs'
        original['source_sha256'].pop(editor)
        self.assertEqual(repeated['source_sha256'].pop(editor),sha((HERE.parent.parent/editor).read_bytes()))
        self.assertEqual(original['source_sha256'].pop('Assets/Packages/ZTracker/Runtime/Data/ZTrackerNative.cs'),
                         '8c33fb3c8c9d0ed376f89395d2ccd3b41a5b346b2af6f72602b09eed78c90996')
        self.assertEqual(repeated['source_sha256'].pop('Tools/ZTrackerNative/native_abi.cs'),
                         'c6a1e59f6d62c6a8d8ad85e2c852d0f8180e316bb43eb12b5f0d84b33911dbf7')
        # Fresh Windows checkouts can store unchanged native text as CRLF. Keep
        # actual-byte provenance exact, then verify only newline-normalized
        # content against frozen source. No PCM/event tolerance changes.
        for source in list(original['source_sha256']):
            if not source.startswith('Tools/ZTrackerNative/'):
                continue
            raw=(HERE.parent.parent/source).read_bytes()
            self.assertEqual(repeated['source_sha256'].pop(source),sha(raw),source)
            expected=original['source_sha256'].pop(source)
            normalized=raw.replace(b'\r\n',b'\n')
            if source in MIXED_NEWLINE_SOURCES:
                frozen,normalized_hash=MIXED_NEWLINE_SOURCES[source]
                self.assertEqual(expected,frozen,source)
                self.assertEqual(sha(normalized),normalized_hash,source)
            else:
                self.assertIn(expected,(sha(normalized),sha(normalized.replace(b'\n',b'\r\n'))),source)
        self.assertEqual(repeated['dll']['path'],'Tools/ZTrackerNative/retired/ZTrackerEngine.dll')
        repeated['dll']['path']=original['dll']['path']
        self.assertEqual(repeated['reference_engine'],'archived offline native DLL; not Burst')
        repeated['reference_engine']=original['reference_engine']
        self.assertEqual(original,repeated)
        for path in REFERENCE.iterdir():
            if path.name=='manifest.json':continue # exact case/native semantics checked above
            if path.name=='README.md':
                # Documentation may have platform newline storage. Audio/event bytes and
                # manifest semantics below/above retain their exact repeat-generation gate.
                self.assertEqual(path.read_text(encoding='utf-8'),(RERUN/path.name).read_text(encoding='utf-8'),path.name)
            else:self.assertEqual(sha(path.read_bytes()),sha((RERUN/path.name).read_bytes()),path.name)

    def test_small_perturbation_pass(self):
        self.change_audio(lambda x:x.__setitem__(100,x[100]+1e-7))
        self.assertTrue(compare(REFERENCE,self.candidate)['success'])

    def test_large_perturbation_fail(self):
        self.change_audio(lambda x:x.__setitem__(100,x[100]+.01))
        result=compare(REFERENCE,self.candidate)
        self.assertFalse(result['success'])
        record=next(x for x in result['cases'] if x['id']==self.cid)
        self.assertEqual(record['first_offending_sample']['frame'],50)
        self.assertGreater(record['max_absolute_error'],.009)

    def test_shifted_events_fail_and_explicit_tolerance(self):
        self.change_trace(lambda t:t['native_events'][0].__setitem__('samplePosition',1))
        # The first event shares stamp0 with the next event; move the whole initial group to preserve order.
        self.change_trace(lambda t:[e.__setitem__('samplePosition',1) for e in t['native_events'] if e['samplePosition']==0])
        self.assertFalse(compare(REFERENCE,self.candidate)['success'])
        self.assertTrue(compare(REFERENCE,self.candidate,event_frame_tolerance=1)['success'])

    def test_removed_event_fail(self):
        self.change_trace(lambda t:t['native_events'].pop())
        self.assertFalse(compare(REFERENCE,self.candidate)['success'])

    def test_nonfinite_audio_fail(self):
        self.change_audio(lambda x:x.__setitem__(100,math.nan))
        with self.assertRaisesRegex(ValueError,'nonfinite'): compare(REFERENCE,self.candidate)

    def test_wrong_macro_state_fails_identical_audio(self):
        path=self.candidate/'command_10.events.json'; original=path.read_bytes()
        try:
            trace=json.loads(original)
            trace['harness_controls'][0]['channels'][0]['macros'][1]=0
            path.write_text(json.dumps(trace),encoding='utf-8')
            result=compare(REFERENCE,self.candidate)
            self.assertFalse(result['success'])
            self.assertEqual(next(x for x in result['cases'] if x['id']=='command_10')['status'],'FAIL_STATE')
        finally: path.write_bytes(original)

    def test_nonfinite_event_fail(self):
        self.change_trace(lambda t:t['native_events'][0].__setitem__('floatParam',math.inf))
        with self.assertRaisesRegex(ValueError,'nonfinite'): compare(REFERENCE,self.candidate)

    def test_shape_fail(self):
        path=self.candidate/(self.cid+'.f32'); path.write_bytes(path.read_bytes()[:-4])
        with self.assertRaisesRegex(ValueError,'shape'): compare(REFERENCE,self.candidate)

    def test_missing_extra_case_fail(self):
        path=self.candidate/'manifest.json'
        m=json.loads(path.read_text(encoding='utf-8')); m['cases'].pop()
        path.write_text(json.dumps(m),encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'extra'): compare(REFERENCE,self.candidate)

    def test_wrong_rate_fail(self):
        path=self.candidate/'manifest.json'; m=json.loads(path.read_text(encoding='utf-8'))
        m['sample_rate']=44100; path.write_text(json.dumps(m),encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'rate'): compare(REFERENCE,self.candidate)

    def test_uint64_is_exact(self):
        path=self.candidate/(self.cid+'.events.json')
        trace=json.loads(path.read_text(encoding='utf-8'))
        for event in trace['native_events']: event['samplePosition']+=2**53+1
        path.write_text(json.dumps(trace),encoding='utf-8')
        _,cases=load_corpus(self.candidate)
        self.assertEqual(cases[self.cid][2][0]['samplePosition'],2**53+1)
        self.assertFalse(compare(REFERENCE,self.candidate)['success'])

    def test_unapproved_exemption_rejected(self):
        with self.assertRaisesRegex(ValueError,'not an approved'):
            compare(REFERENCE,REFERENCE,exemptions={self.cid:'send_subchunks'})

    def test_approved_exemption_requires_correction_proof(self):
        cid='defect_detune'
        result=compare(REFERENCE,REFERENCE,exemptions={cid:'detune_envelope'})
        self.assertFalse(result['success'])
        self.assertEqual(next(x for x in result['cases'] if x['id']==cid)['status'],
                         'EXPECTED_DIVERGENCE_REQUIRES_CORRECTION')
        self.assertTrue(compare(REFERENCE,REFERENCE,exemptions={cid:'detune_envelope'},
                        correction_checks={cid:lambda *_:True})['success'])

    def test_tremolo_requires_scoped_metadata_and_correction(self):
        with self.assertRaisesRegex(ValueError,'not an approved'):
            compare(REFERENCE,REFERENCE,exemptions={'command_07':'tremolo_ignored'})
        # Copy metadata for the PM-approved P4 comparison without modifying the native corpus.
        manifest=json.loads((self.candidate/'manifest.json').read_text())
        next(c for c in manifest['cases'] if c['id']=='command_07')['approved_defects']=['tremolo_ignored']
        (self.candidate/'manifest.json').write_text(json.dumps(manifest))
        result=compare(self.candidate,self.candidate,exemptions={'command_07':'tremolo_ignored'})
        self.assertFalse(result['success'])
        self.assertTrue(compare(self.candidate,self.candidate,exemptions={'command_07':'tremolo_ignored'},correction_checks={'command_07':lambda *_:True})['success'])
        self.assertFalse(compare(self.candidate,self.candidate,exemptions={'command_07':'tremolo_ignored'},correction_checks={'command_07':lambda *_:False})['success'])
        with self.assertRaisesRegex(ValueError,'incorrectly scoped'):
            compare(self.candidate,self.candidate,exemptions={self.cid:'tremolo_ignored'},correction_checks={self.cid:lambda *_:True})

    def test_invented_defect_refused_even_if_metadata_declares_it(self):
        manifest=json.loads((self.candidate/'manifest.json').read_text())
        next(c for c in manifest['cases'] if c['id']==self.cid)['approved_defects']=['anything']
        (self.candidate/'manifest.json').write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError,'unknown'):
            compare(self.candidate,self.candidate,exemptions={self.cid:'anything'},correction_checks={self.cid:lambda *_:True})


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference',type=Path,default=REFERENCE)
    parser.add_argument('--rerun',type=Path,default=RERUN)
    args=parser.parse_args(); REFERENCE=args.reference; RERUN=args.rerun
    suite=unittest.defaultTestLoader.loadTestsFromTestCase(GoldenTests)
    result=unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(0 if result.wasSuccessful() else 1)
