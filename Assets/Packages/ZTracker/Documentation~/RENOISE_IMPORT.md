# Renoise and Redux import, version 1

Version 1 brings the composition into ZTracker: pattern lengths and order, separate tracks, notes and note-offs, instrument slots, note-column values, and a restricted set of commands. The linked mapper keeps replacement choices while the composer revises the source. Finish the sound design in ZTracker. A successful import is not a promise that an arbitrary Renoise mix sounds identical.

## A first import

1. Open ZTracker and choose **Renoise** on its existing toolbar. In the import window choose **Source…**, then an `.xrns` song or `.xrni` instrument. Inspection reads the archive without creating assets.
2. Read **Issues** and **Changes**. The mapping list already identifies source instruments, declared plugins, effect chains and devices. Unsupported content remains visible. Choose the import name and press **Import** to create its folder under `Assets/RenoiseImports/`.
3. In **Mappings**, work through **Unmapped** first. Pick an existing ZTracker instrument through the target asset field, or use **New target…** to create a Synth, FM, Sampler or Drum kit. Plugin names and suggestions describe possible replacements; they do not decode a plugin patch. **Edit target** opens the chosen sound for authoring.
4. Select a sequence occurrence where the source is used, set the row range, and **Audition** the replacement. The start row is inclusive and the end row exclusive. The preview plays the selected source's part on a clean channel with its instrument effects; source track effects and routing are excluded. **Stop** affects this preview only. The song's renderer is independent.
5. Replace unsupported effect chains where needed, mark reviewed rows **Dealt**, then **Save** the mapper. **Song** opens the imported arrangement. Instruments remain ordinary ZTracker assets in the shared asset browser.

An XRNI has no song arrangement. Its audition plays a short note through the selected instrument. Clearing an instrument target preserves the notes and source metadata and makes that part explicitly silent until another target is chosen. Creating an empty sampler or drum kit does not create its sound content; assign samples or drum sounds in the instrument editor.

The list can filter **All / Unmapped / Approximate / Mapped**, sort exceptions, names or usage, and show removed entries through **History**. Notes, occupied rows, share of song notes, pitch range and track names help prioritize replacements. Usage counts follow sequence occurrences, so a repeated pattern counts again; muted triggers are excluded. These are composition statistics, not analysis of rendered loudness or arbitrary pattern-flow commands. Instrument chain/device rows inherit their instrument's usage; track chain/device rows count direct notes on that track. Indirect sound arriving through group, send and master routing is not counted. The issue list groups repeated causes and shows a bounded selection of affected locations.

## What the status means

| Status | Meaning |
| --- | --- |
| Mapped | This item fits the implemented conversion profile. It does not certify the original and replacement DSP as identical. |
| Approximate | A usable translation or explicit replacement has a known difference. Read the issue and audition the result. |
| Unmapped | No usable automatic translation or target has been selected. Instrument notes remain available for a later replacement; unsupported effects and control data are not executed. |
| Dealt with | A separate review mark. It records that a person has addressed or accepted the item; it never changes compatibility or turns unsupported behavior on. |

Instrument and plugin rows belonging to the same source slot share the chosen instrument. A plugin row can expose its declared name and identifier even when the plugin is unavailable on this PC. Missing names cannot be reconstructed from an opaque state blob. The mapper never loads plugins or executes source scripts.

## The composer contract

Use a current Renoise song with the modern playback engine, straightforward pattern notes, explicit note-offs, and simple sample or replacement-friendly instrument sounds. Use unique, stable instrument and chain names. Leave final effects and detailed timbre to ZTracker. This profile gives the most useful iterative workflow with the fewest exceptions.

### Arrangement and timing

| Compose with | Import behavior |
| --- | --- |
| Separate note tracks, groups, sends and one master | Track roles, names, groups and default parent/master routing are preserved. Hardware outputs and other non-default routes require review. |
| Patterns of 1–512 rows; sequence references and repeated patterns | Length, sparse rows, sequence order and per-occurrence track mutes are retained. Rows stored beyond a shortened pattern's playable length remain only in the source archive. |
| Pattern-track aliases | Note/effect rows are resolved into ordinary target rows. The destination track keeps its own automation. Circular or invalid aliases are refused. |
| Up to 12 note columns and 8 effect columns per track | Column positions and payload are retained. Hidden or muted note columns remain inactive. Make intended playing note columns visible before export. |
| Notes, `OFF`, instrument-only rows and command-only rows | These remain distinct; empty rows still occupy time. An omitted instrument selection carries the previous selection in its note column. |
| Numeric volume and pan columns `00`–`80`; delay `00`–`FF` | Values, including explicit zero, are retained. Pan center is `40`; delay is a fraction of a row, not a rounded tick. |
| Initial BPM 32–999, lines per beat 1–256 and ticks per line 1–16 | Fractional initial BPM is supported. For simple exchange, keep timing fixed or use only the supported timing commands below. |
| Straight timing | Active groove/shuffle and track delay are reported but not reproduced. Turning groove off is supported. |

Notes preserve Renoise's numeric pitch convention: C-4 is 48. A sampler plays relative to its saved root key. Do not add an octave shift simply because another application labels MIDI octaves differently. Audition a replacement synth in the actual song's range before settling its tuning.

Global headroom, master automatic gain, soft clipping and DC correction are not equivalent mix stages in this profile. Match the final gain in ZTracker. Surround and stereo-width differences are also reported. A muted track and a track whose output is off retain their different target mute roles.

### Pattern commands

The importer admits a deliberately smaller set than ZTracker can play when authored directly. Accepted sample/local commands are `0A`, `0U`, `0D`, `0G`, `0V`, `0I`, `0O`, `0T`, `0C`, `0S`, `0B`, `0E`, `0N` and `0Y`. Track effect columns additionally admit `0M`, `0Q`, `0R`, `0L`, `0P` and `0X`. These cover the target's supported arpeggio, pitch/gain/pan operations, sample offset/direction/envelope operations, probability, delay/retrigger and selected mixer/stop controls, with their original scope and hexadecimal arguments retained. Volume/pan short commands are restricted to the implemented `I O J K U D G C B Q Y R` family; other tokens are kept inactive.

Supported timing/flow commands are `ZT`, `ZL`, `ZK`, `ZB` and `ZD`; `ZG00` is the supported groove-off case. Command BPM is restricted to 32–255, with the target's zero-stop behavior; tick count is 1–16. Not every accepted argument has identical historical Renoise behavior. The target still diagnoses invalid or unsupported command forms. Its probability draws use its own deterministic seed. General device-addressed commands, instrument macro commands, MIDI messages, phrase/script triggers, routing command `0J`, groove-on, and unknown commands remain inactive.

Old pitch/offset compatibility switches are not silently treated as modern behavior: affected commands are retained inactive. With older LPB playback engines 4 or 5, notes and the initial clock import but **all pattern commands**, including later tempo changes, remain inactive. Engines outside 4–6 cannot be converted. If upgrading an old song in Renoise, do it on a copy and check the timing there; the importer does not perform a historical speed-engine migration.

For replacement synths, use note composition and simple numeric volume/pan first. A sample-specific command cannot manufacture the corresponding VST behavior. ZTracker's arpeggio supports sampler, synth and FM, but that does not make every sample command meaningful for a synth replacement.

### Sample instruments and Redux

Save an instrument from Renoise or Redux as **XRNI**. Redux's DAW project state, `.vstpreset` and `.xrnl` library bundles are not XRNI inputs. Export the instrument itself rather than renaming or wrapping a host preset. No installed Redux is needed to import an exported XRNI.

Use embedded mono or stereo WAV/FLAC samples, inclusive key and velocity zones, root keys, ordinary transposition, and note-on layers. WAV input supports PCM 8/16/24/32-bit and 32-bit IEEE float; FLAC supports the bounded native lossless stream profile with a declared frame count. Sample rate is retained within 1–384 kHz. The importer identifies audio from its header and source ordinal, so an old external filename or misleading extension does not redirect it to another file. Missing audio is reported; original machine paths are never followed.

Decoded PCM is written to an immutable float WAV, then imported by Unity for playback. The decoder checks preserve the synthetic source samples exactly; Unity's PCM import and asset reload introduced up to one 16-bit step (1/32768 of full scale) in the measured persistence fixture. The persistence check allows that measured quantization. Do not treat successful storage as a claim of bit-identical playback for arbitrary source audio.

| Sample feature | Version 1 treatment |
| --- | --- |
| Root key, note/velocity ranges, key tracking | Imported directly. Overlapping Play All zones remain layers. |
| Gain, pan, semitone transpose | Imported. Fine tuning uses 100/127 cents per signed source step and is explicitly approximate; the endpoint law has not been calibrated against a Renoise render. |
| Off / Forward / Backward / PingPong loops | Modes and saved frame bounds are retained, with target-exclusive end and a minimum two-frame loop. Endpoint playback parity is uncalibrated and reported; invalid bounds disable the loop. |
| Loop release, one-shot and Cut / NoteOff / Continue | Translated to the corresponding target controls. Mute-group choking is scoped to the target instrument and note track, so cross-track parity is unverified. |
| Linear / Cubic interpolation | Kept. Other interpolation choices use cubic with an issue. Oversampling, automatic fade and automatic seek are not reproduced. |
| Simple volume point envelope | Unsynced, unipolar, multiplying, unquantized straight-line points can import with sustain/loop markers. The millisecond interpretation and control-rate parity are uncalibrated; the source release-decay law is not translated and the target has its 50 ms release. This is an approximation. |
| Other modulation and sample filtering | Source XML is retained; AHDSR, LFO, stepper, filter, curved/stepped envelopes, tempo-synced modulation and unsupported operators stay inactive. These can be authored in ZTracker afterward. |
| Cycle / Random overlapping sample selection | One playable alternative is retained for an overlapping region; competing alternatives remain inactive. The target does not reproduce round-robin/random choice. |
| Note-off layers and slice aliases | Retained inactive. Slice markers remain source metadata; automatic slice-trigger mapping is not provided. |
| Velocity-to-gain disabled | Velocity zone selection survives, but note velocity still affects target gain. Reported as a difference. |
| Beat-sync/stretch, phrases, scripts, scales, quantize, custom tuning and sampler mono/glide | Not reproduced. Export a simpler sample map or select a replacement and rebuild the behavior in ZTracker. |

Instrument effects and macro destinations do not automatically become equivalent target modulation. Sample-only imports use a sampler even when they resemble a drum kit; this preserves their available zones and layers. Instruments combining sample and plugin generators need an explicit target choice.

### Plugins and sound design

There are **no plugin patch converters in version 1**, including Helm, OPNplug, Serum, Dexed or Redux-as-a-plugin. The mapper reads identity and state evidence and suggests a target family. A suggestion such as FM is a starting point for a person, not proof that the patch fits four operators or that its settings were transferred.

For a composing patch intended for a ZTracker replacement, keep to simple oscillator waves and a small amplitude envelope, or a four-sine-operator FM arrangement that can be rebuilt in ZTracker. Helm and OPNplug can be useful composing choices for those restricted shapes if already part of the composer's setup. Avoid relying on wavetables, plugin arpeggiators/sequencers, six-operator patches, unusual feedback/modulation and plugin effects for the composition's identity. ZTracker has two oscillator sources and its own four-operator FM setups, envelopes, modulation, sampler layers and instrument effects; their availability does not imply automatic decoding of another synthesizer's state.

For an existing song with an unknown plugin, prioritize its note count and range, choose a rough target, audition one of its used passages, and refine the sound in ZTracker. Original-plugin A/B audio requires a reference render made in a host with that plugin. An opaque saved state cannot audition itself.

### Effects and automation

| Renoise source | Automatic import profile |
| --- | --- |
| Track/group/send/master mixer | Static pre/post gain and centered pan conversion. No inferred surround, master clipping, auto-gain or DC-correction equivalent. |
| Gainer | Only saved gain 0–4, centered pan and no left/right phase inversion. Bypass state is retained. |
| Send | Only a valid internal send destination, centered send pan, gain 0–16 and Mute Source off; its insertion position and pre/post-fader choice are retained. Disabled sends are inactive. |
| Pre-pan and post-pan mixer automation | Unipolar, unquantized Points/Lines (held/linear), ordered points and no curvature. Modern subline times convert to fractional rows. |
| Gain automation, effect parameters and all other automation destinations | Retained inactive unless explicitly rebound/reauthored in ZTracker. A saved physical gain value does not prove the normalized automation curve is the same. |
| Delay, filter, compressor, EQ, reverb, distortion, meta devices, plugin effects, nested Doofer/Splitter routing | No automatic algorithm/parameter conversion in version 1. Choose a replacement chain or recreate the effect. |

ZTracker can play many effects that this importer deliberately does not auto-map. Different gain laws, time scales, EQ band placement, wet/dry routing, feedback topology and automation normalization are enough to make equal-looking knob values sound different. Author those effects in ZTracker or explicitly select an existing target chain. More native mappings require measured source-to-target parameter laws, not just matching device names.

For a top-level track chain, select an existing **Instrument chain…** or **Song chain…**, or choose **Dry**. This replaces the whole source chain, not just the selected unknown device. Source device-addressed automation stays inactive after a topology replacement; rebind it deliberately. **Automatic** restores the restricted source conversion where offered. Nested chains can be inspected, but are not independent automatic replacements.

For a top-level instrument chain, first choose the instrument target. Applying a chain creates an owned copy of that target, leaving the original sound asset unchanged. With an imported sampler and identifiable source sample slots, the chosen chain is assigned to the samples that used that source chain. A manually substituted sound has no trustworthy source-sample correspondence: its whole/first instrument chain is replaced, and sampler samples use that replacement. This broader operation is explicitly approximate. Design more intricate routing in the target instrument editor.

## Reimport and ownership

The imported song links to its source path, an import identity and the SHA-256 of the original archive. **Reimport** inspects the linked path again; **Changes** reports added/removed entries and changed, added or removed patterns. **Apply revision** replaces the arrangement from that inspection. Canceling inspection makes no asset changes. If the source moved, choose it with **Source…** in the linked mapper and review the proposed revision before applying.

The conflict rule is intentionally simple: **Renoise owns the imported arrangement; ZTracker owns every existing mapped instrument's sound design.** Reimport retains both deliberately selected targets and previously generated sampler targets. It therefore does not overwrite edited samples, envelopes or patches merely because the source instrument changed. It also does not update those retained samples automatically. To adopt a changed source sample bank, make a separate fresh import, compare the generated instrument, and deliberately map to that new target. Local arrangement edits will be replaced by applying the next source revision; keep composition edits in Renoise or keep a separate ZTracker song copy.

Matching uses plugin identity plus saved patch/state evidence, sample-map evidence for samplers, and an occurrence disambiguator for repeated identities. A changed patch can retain a choice by an unambiguous instrument name and declared plugin identity. Effect-chain keys use their declared name when present; unnamed chains use a deterministic fingerprint of device types and order. The selected row displays the match reason and provides technical identity evidence in tooltips. Names are useful identity evidence, not universal IDs: renaming, reordering indistinguishable copies or changing an unnamed chain's topology may create a new unresolved entry. Review **Changes** rather than assuming every match is correct. Removed entries and review marks remain in History; a review mark never suppresses a new compatibility issue.

Each import owns a new folder. Normal song/instrument assets and immutable decoded PCM WAV sources live there with their Unity metadata. Samples deduplicate within an instrument by canonical PCM, sample rate and channel/frame geometry; playback settings remain separate. There is no project-wide sample bank, automatic deletion of prior revisions or compact embedded-audio package in this release. Original archives are retained under `Source~` for recovery and future adapters; that folder is editor-only and is not a runtime audio dependency. Keep the entire import folder when moving the work, and keep any attribution or license notes alongside it.

Save writes the linked song and mapping recipe without saving unrelated project assets. Mapping changes participate in Undo/Redo. An import failure rolls back newly created import content. Undoing a mapping is not a general file-deletion operation: generated PCM and source-history files may remain owned by the import folder. Do not delete individual generated samples that instruments still reference. Imported songs need no Renoise, Redux or plugin host during playback.

## Limits and verification

The reader accepts song document revisions **54–67** and instrument revisions **24–34**, the Renoise 3.0–3.5 format families. Intermediate revisions are accepted with a compatibility warning; the format revision is not an exact application version. Older files are refused rather than silently assigned modern timing. Redux XRNI is accepted when its document revision fits this range.

Structural caps are 128 total tracks, 64 note tracks, 4096 instruments/patterns, 65,536 sequence slots, and 4096 samples/zones per instrument. Alias expansion and usage analysis have additional work limits. Archives are bounded to 256 MiB compressed and 512 MiB expanded, with XML/entry/depth limits; ZIP64, ambiguous members, path traversal and encrypted/corrupt archives are refused. Decoded audio has the engine's **256 MiB song budget**, checked before sample allocation. A small FLAC can still decode to a large buffer. Near that bound, differently encoded duplicate waveforms may require additional decoding headroom before deduplication is known; byte-identical sample members are reused directly.

Verification distinguishes structure, storage and sound. Synthetic checks cover exact PCM decoding, corruption/size refusal, mappings, song conversion and scoped persistence. They cannot establish original-host timing, endpoint or timbre equivalence. No audible-parity claim is made. The maintained check entrypoints and the main session's actual executed results belong in [OVERHAUL_VERIFICATION.md](OVERHAUL_VERIFICATION.md); do not infer a live UI or listening pass from a successful compile.

Source references for the composing concepts are the [Renoise effects reference](https://tutorials.renoise.com/wiki/Effect_Commands), [sample modulation manual](https://tutorials.renoise.com/wiki/Sampler_Modulation), [keyzone manual](https://tutorials.renoise.com/wiki/Keyzones) and [Renoise/Redux library manual](https://tutorials.renoise.com/wiki/Libraries). The tables above describe this importer's implemented subset, which is narrower than those products' full feature sets.
