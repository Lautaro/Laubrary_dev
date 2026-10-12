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

## Reload and audition follow-up (2026-10-12, T-0583 through T-0587)

The five cards cover MIDI persistence, direct Pitch/Vibrato controls, cold Pattern playback, workspace persistence and duplicate octave steps respectively. The final MIDI UI run passed 36/0, including recording, mixed keyboard chords, audio-buffer changes, unplug/replug and leaving every saved preference unchanged. The expanded instrument gesture/curve/enable/Undo regression passed 22/0. Shared section capture now preserves the underlying fold when disabled and keeps existing windows independent.

Verified by probe: clean compilation; cold offline PCM 12/0; cold live SAP 12/0; recovery after a stopped audio graph 12/0; retained sends with missing destinations 4/0; Pattern workflow including duplicate key/character and held-repeat events 9/0; instrument overhaul 13/0; all 205 slider/range instances still declare defaults, with eight reset/Undo event cases. Fresh and stopped-graph playback tests cover model and legacy Synth/FM through typing, MIDI and transport without opening Instrument preview.

Verified by eye: Pitch and Vibrato appear directly inside Oscillators, with explicit units and dependent controls hidden at zero depth. The parent used native clicks to switch synth type and collapse an FM operator, scrolled the page, then compared serialized workspace state after actual compilation. Two reloads preserved window position, tabs, cursor/count state, section folds and scroll positions, including a nonzero 157.5-pixel scroll. The selected LKMK3 MIDI input reconnected automatically after compilation. The UI Guide compliance pass found no new padded chrome, subsection arrows or ineffective controls; existing reset and Undo paths remain intact.

Not verified: physical MIDI key presses and listening through the owner's speakers. Native keyboard injection could not target the floating test window reliably, so octave evidence is dispatched UI key events rather than a physical keyboard claim. The user's exact silence sequence was not supplied; fresh startup already passed, while closed-stream reuse and missing send destinations were concrete defects repaired and covered by PCM checks.

## Live synth and FM follow-up (2026-10-12, T-0588 through T-0592)

T-0588 adds independent None / Glide / Legato / Both note behavior with Glide ms alongside; overlapping Legato notes preserve their envelopes. T-0589 keeps all Vibrato controls editable at zero depth. T-0590 adds semitone Tune steps beside Tune cents without replacing cent curves. T-0591 repairs missing/partial FM operator arrays through an explicit Undo-safe action, initializes them on Engine-to-FM, and fixes the routing diagram bounds. T-0592 applies waveform, blend mode, engine, unison, tuning, routing and sound-parameter edits to held notes and running transport without restarting the renderer. Modern FM now honors its visible Amplifier ADSR separately from operator ADSR; native legacy behavior remains compatible.

Verified by probe: clean compilation; live-synth Burst PCM checks 28/0; FM repair and attached diagram bounds 3/0; attached pitch controls 4/0, including mode bits, horizontal placement, zero-depth Vibrato editing, independent tuning and reset/Undo/Redo; instrument editing 22/0; instrument overhaul and all six DSP routing/operator-contribution checks 13/0; Pattern shortcuts/reorder 9/0; cold PCM 12/0; actual cold SAP audio graph 12/0; MIDI UI 36/0. The slider inventory now covers 207 controls with declared defaults and eight dispatched double-click reset cases. A separate actual SAP check confirms that the same running host keeps advancing while waveform/transpose edits apply and volume edits silence then restore its rendered audio.

Verified by eye and native input: the parent's two cold walks used fresh disposable songs/instruments. Play from Instrument, change Saw to Square/Triangle, switch the playing engine to FM, and see the four operator cards plus routing while the toolbar continues to show Playing. Both mode was also selected during playback and enabled the adjacent Glide ms control. The original selected FM instrument had no operators; clicking Create operators restored its four controls without replacing its other settings. Responsive two-column Synth and FM cards were inspected. The final UI Guide pass also exercised attached narrow/wide layouts and caught/fixed a wrapping Notes/Glide row and a vertically shrinking graph. No remaining clipping appeared in the checked surfaces.

Limits: curve/device/effect-chain topology changes remain structural and report that stop/play is required; ordinary values and the exposed synth-engine/unison choices update live. Unison expansion uses free slots in the existing shared voice pool. Audio evidence is actual rendered PCM, not physical speaker listening. MIDI reconnect/settings checks do not claim that a hardware key was physically pressed during recompilation.

The repeated T-0583 through T-0587 cards were reopened and rechecked alongside the new work. Actual final compilation preserved all ten MIDI preferences, LKMK3 MIDI reconnection, window rectangle, selected tabs/cursor/octave/step and the complete serialized workspace pages, including a closed FM operator section and 120px scroll. The original view was restored. The selected incomplete instrument's four-operator repair was saved to `Assets/Demos/ZTrackerDemo/Synth.asset`, retaining the other current authored values.

Round-two cleanup removed the `.round2-*.cs` and `.round2-*.ps1` scripts, proof SessionState entries and all disposable windows/in-memory assets. One original ZTracker window remains open in the canonical editor for the owner. No player build, additional editor, test server or network listener was started. Unrelated dirty reviews and authored project/sample changes remain outside these source commits.

## Cleanup

The reload follow-up also removed `.bugfix-ahq.ps1`, `.bugfix-check.cs`, `.bugfix-save.cs`, `.bugfix-walk.cs` and its temporary SessionState snapshots. Only the user's original ZTracker window remains in `D:\UNITY\Laubrary Dev`, deliberately left open with LKMK3 MIDI connected for testing. No test processes or builds were started. The final reload check also confirms no stale preview-warning state.

The temporary `ZTracker overhaul check` windows and their in-memory songs/instruments were closed and destroyed. Root `.overhaul-*.cs` and `.overhaul-ahq.ps1` scratch files were removed. No test server, extra Unity editor, player executable or network listener was launched. The user's canonical editor and pre-existing project/sample changes remain in place.

## Animated track reorder (2026-10-12/13, T-0572 revision)

The owner rejected the first version because dragging was not animated. Reordering now uses ZUI's shared drag-to-reorder for every list in ZTracker: the dragged item follows the pointer, the other items slide (110 ms, ease-out) into the places they would take, and the data order changes once, after the drop settles, as one Undo step. Pattern headers move sideways and carry their whole visible column; Mixer rows move up and down. Sequence slots, samples, modulation sets and devices, instrument chains and macro mappings use the same component; the native drag-and-drop reorder path is deleted. The Add-track type menu was left as it was.

Verified by probe: compilation clean. Pattern overhaul 10/0, including a new group that drags a header with pointer events dispatched through the panel and checks, mid-drag, that the header follows the pointer, its cells travel with it, the next header slides aside, no vertical drift, no data change before the drop and none while settling, then one Undo. Mixer 13/0, with the old native-drop group replaced by the same live-drag assertions plus a click that selects without reordering. Instrument 11/0, Review 13/0. A sequence-slot drag (a wrapping row of buttons) reorders without firing the button's click, and a later click still selects. A plain `ZuiReorder.MakeGrip` list (the form Pyre, Chunks, DotGen and others use) still reports the same move. Sampled timing: a neighbour header slid 0 → -26 → -34 → -39 → -41 px over about 90 ms; the dropped header eased 160 → 140 px over about 75 ms before the order was committed.

Verified by eye: a disposable walk window was dragged with real panel pointer events and captured with PrintWindow mid-drag: in the Pattern view the Lead column (header and cells) floats between Drums 3 and Sequencer 4 while Master and Drums 3 have shifted left to show the gap; in the Mixer the dragged row sits under the pointer with the rows above moved up.

Not verified: a drag with the physical mouse. Injected OS mouse input reached the cursor position but not the editor window (the session appeared not to accept synthetic clicks), so no native-input claim is made. No auto-scroll while dragging past the edge of a scrolled Pattern view: dropping onto a track that is off screen needs scrolling first.
