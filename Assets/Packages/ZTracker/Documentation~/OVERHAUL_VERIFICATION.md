# ZTracker overhaul verification

Verified on 2026-10-12 in the running canonical editor at `D:\UNITY\Laubrary Dev`, on branch `feat/ztracker-overhaul`. The board group is [ZTracker overhaul](http://127.0.0.1:8778/?project=Laubrary_Dev&node=ZTracker-overhaul). Existing songs, instrument assets and external sample-library settings were preserved.

## Request accounting

| Card | Result |
| --- | --- |
| T-0569 | Plus/minus changes octave, Space starts/stops, Escape toggles Edit and Tab advances Step rows. Text entry keeps its typing keys. Repeated drum octave changes use the drum keyboard's octave. |
| T-0570 | The first FX identifier character opens suggestions. A catalog describes supported commands, arguments, ranges and applicability; device commands come from actual parameter descriptors. Selecting a suggestion applies the command. The notation is ZTracker's existing notation, not a claim of full Renoise compatibility. |
| T-0571 | Pattern length and selected-track Name, Notes and FX counts appear directly left of the pattern grid. Cropped rows and automation return through Undo. |
| T-0572 | Pattern headers and Mixer rows support native pointer dragging. Identity-based pattern data, routing and automation remain attached. Add track opens a Notes/Drums/Group/Send/Event menu. |
| T-0573 | Section headers fold without arrow glyphs. Optional sections use an actual checkbox before their title, retain authored values and cannot unfold while disabled. Shared chain caret styling is scoped to ZTracker. |
| T-0574 | Shared ZUI scrub-number control provides a bold number, bounded drag, Shift fine adjustment, click-to-type and double-click reset. Applied to octave, column counts, step and other discrete counts. |
| T-0575 | Compact and borderless action styles, joined V/P/D/FX switches, smaller transpose/source controls, group spacing, icon transport/Edit and + Instrument/+ Drumkit actions. Long drum labels reserve icon space and truncate safely. |
| T-0576 | All matching sources are reachable through 100-item pages. Immediate parent folder tags remain searchable, custom tags are retained and known aliases ignore case/spacing. Kicks and Snares aliases match the owner's folder. |
| T-0577 | Synth/sampler details use responsive columns; amplifier controls and envelope split when sufficient width is available. Pitch and Vibrato belong under Oscillators. Narrow windows stack content. |
| T-0578 | FM graph reflects all six actual DSP algorithms, carriers/modulators and summed-output feedback. All four operators participate in every algorithm. Each operator has its envelope and relevant frequency/level controls. |
| T-0579 | Instrument/Mixer effects use the shared Zounds chain editor/runtime. New filters belong in the chain; an already-enabled per-voice legacy filter remains editable under Effects to preserve sound. Delay/reverb sends appear only for applicable instruments with matching song Send destinations; orphaned stored amounts receive explanations. |
| T-0580 | Irrelevant unison, vibrato, fixed-Hz/ratio, disabled envelope and beat-interval controls are conditional. Unresolved automation targets offer rebinding/removal instead of ineffective value editing. |
| T-0581 | Parent integrated worker changes, ran compilation/regressions and native visual walks, checked UI rules, reconciled all cards and removed temporary fixtures/scripts. |

## Verified by probe

- Unity compilation completed with no errors after the final source edit. No player build or Unity Test Runner was used.
- Pattern overhaul: 8 groups passed, 0 failed. Includes shortcuts, text exclusion, repeated drum octave changes, length/Undo, reorder/Undo/Redo, retained cell selection after reorder, first-character FX suggestions and compiler/catalog coverage.
- Instrument overhaul: 13 groups passed, 0 failed. Includes actual attached-panel layouts at 1250/760 logical pixels, checkbox state preservation, retained filter/send visibility, shared effect editor identity, fixed frequency versus ratio, full-lane FM envelope dragging without page rebuild or scroll loss, an expanded mechanical UI audit, all six FM graphs against generated Burst PCM and audible-contribution probes for each operator.
- Mixer workflow: 11 groups passed, 0 failed. Includes type menu, real retained pointer events, reorder identity/Undo, routing, sends, cloning/removal, shared chain Undo, automation and conditional beat interval.
- Engine: 33 groups passed; drum runtime: 7 passed; modulation/synth regression: 27 passed; instrument authoring: 11 passed. All zero failures.
- Keyboard focus and polyphony checks passed: button-focused chords, repeat suppression, text input exclusion, blank-space input, focus-loss release and voice handling. The existing unified computer-key/MIDI input path remains in use.
- Shared controls check passed: disabled sections stay folded, re-enable restores availability, scrub numbers clamp to bounds.
- Drum catalogue check passed: 205 synthetic records reach all three pages (100/100/5), custom parent tags span pages correctly, aliases and normalized search match. Recursive filesystem scanner check passed.
- Read-only scan of the configured `E:\Audio Resources\Drumz\Arcade Drumz` found all 55 clips: Hats 18, Kicks 8, Snares 29, with 29 FLAC and 26 WAV files and no scan errors.

## Verified by eye and native input

The parent personally repeated the Pattern/Mixer authoring walk with fresh disposable in-memory fixtures: scrub length from 16 to 19, drag Drums before Synth in Pattern, enter Mixer and see the same order, drag Synth back in Mixer, open Add track and choose Drums, then see the new drum track. Re-entry preserved the intended presentation and model changes. The FX helper was also inspected after retained keyboard/cell input.

Wide synth/FM views, narrow FM stacking, true enable checkboxes, compact transport/column controls, source-label spacing and the parent-folder filters were inspected. The actual Kick and Snare filters showed 6 and 18 sources respectively. The UI Guide compliance pass found and corrected over-wide responsive cards, a stale beat-interval control and overlapping long drum labels. No remaining clipping was observed on the checked surfaces at those sizes.

## Limits and compatibility

- Native MIDI hardware and listening through the owner's physical audio device were not tested in this overhaul. Audio correctness evidence is rendered PCM and input/voice probes, not a listening claim.
- Large-library paging was checked with 205 generated catalogue records; the configured real folder has 55 supported files. Supported external formats are WAV, OGG, MP3, AIF, AIFF and FLAC.
- Existing enabled per-voice filtering cannot be silently replaced by one post-mix chain filter without potentially changing polyphonic sound. Its compatibility controls therefore remain under Effects; the standalone Synth Filter section is removed.
- No OS file picker was driven during this final overhaul walk. Earlier folder-selection work remains intact; this pass used the configured folder read-only.

## Slider reset follow-up (2026-10-12, T-0582)

Verified by probe: compilation succeeded; all 205 slider/range instances exercised across instrument, sampler, drum, Pattern, Mixer, Automation and Song views declare defaults. Eight dispatched double-clicks verify volume, FM operator level, envelope milliseconds and tempo across rebuilt views, including Undo/Redo. Pattern checks pass 8/0 and Mixer checks pass 13/0, including actual reset events for gain and Automation value/range controls.

Verified by eye: the parent used the native mouse to drag synth volume to 148%, double-click back to its declared 25%, then repeat from 130% to 25%. The tooltip names the reset value. The UI Guide compliance pass confirms default values are converted to display units and reset follows the same Undo-aware edit path.

Not verified: every individual slider was not separately exercised with native mouse input; exhaustive coverage is the control inventory plus representative reset/Undo probes. Physical audio listening was not repeated for this UI-only change.

The disposable reset window and its in-memory assets were closed; the root reset scratch script was removed. The user's editor and authored assets were retained.

## Cleanup

The temporary `ZTracker overhaul check` windows and their in-memory songs/instruments were closed and destroyed. Root `.overhaul-*.cs` and `.overhaul-ahq.ps1` scratch files were removed. No test server, extra Unity editor, player executable or network listener was launched. The user's canonical editor and pre-existing project/sample changes remain in place.
