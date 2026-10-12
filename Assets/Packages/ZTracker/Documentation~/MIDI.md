# Playing ZTracker instruments from a MIDI keyboard

ZTracker can be played from a USB MIDI keyboard on Windows. It needs no driver, no extra package and no setup beyond plugging the keyboard in: it reads the keyboard through the Windows multimedia MIDI interface that every Windows version has.

## Using it

Open **Laubrary > ZTracker** and press **MIDI** at the top right of the window. It is off until you turn it on, and it is off again after a script reload or when the window closes. The status beside it says which keyboard it listens to and where the keys go, for example `→ Lead col 1 · 01 Piano  ·  Keystation 61`. Hover it for the details (channel, last key and velocity, audio buffer). **Panic** silences every note played from the keyboard at once; the song keeps playing.

Where the keys go depends on the workspace:

- **Pattern, Mixer, Automation and Song**: the keys play the instrument selected in the Instruments list, through the track and note column the pattern cursor is on, exactly as a note written there would. The track's volume, pan, mute, solo and effects apply, and so do the instrument's own effect chains. While the song plays, a key may cut the note that column was playing; that is intended.
- **Instrument**: the keys play the instrument being edited on a clean preview channel of its own: no track settings, only the instrument's own effect chains. The preview can sound over a playing song (for jamming), and it never stops the song. Edits to the instrument are heard on the preview as you make them.

Chords spread over the track's visible note columns, starting at the cursor's column: a three-key chord on a track with three note columns uses all three. The number of note columns is the polyphony. When more keys are held than there are columns, the oldest held note in that track gives way to the new key (it follows the instrument's new-note action, which by default lets it fade with its release). The clean preview channel has twelve columns, so twelve keys at once.

## Settings (right-click the MIDI button or its status)

| Setting | What it does | Default |
| --- | --- | --- |
| Device | Which MIDI input to listen to. The list follows plugging and unplugging on its own (every two seconds); an unplugged keyboard is reopened when it comes back. **Simulated keyboard** is a stand-in used by the checks. | First keyboard found |
| Channel | Listen to every channel (Omni) or to one channel. | Omni |
| Octave | Shifts every key by whole octaves. | 0 |
| Velocity | How hard you press becomes how loud: Linear follows the key, Soft makes gentle playing louder, Hard needs a firmer touch. | Linear |
| Buffer | The editor's audio buffer size, for this editor session only (see Latency below). The project's own audio setting is never changed. | Project |
| Bend range | How far the pitch wheel bends, in semitones each way. | 2 |
| Mod wheel | Which of the played instrument's eight macros the mod wheel moves. | Macro 1 |

These are remembered per machine. Fixed controller meanings: the sustain pedal (controller 64) holds notes after their keys are released until the pedal lifts; All Notes Off (controller 123) releases every played note; All Sound Off (controller 120) silences them at once; knobs sending controllers 21 to 28 move macros 1 to 8 of the played instrument (many keyboards send these from their first eight knobs). Program changes and aftertouch are ignored. The pitch wheel bends only the notes played from the keyboard, including their release tails; it never bends the song's own notes. Macros are the instrument's, so moving one also affects the song's notes of that instrument while it plays (as it would from a pattern command).

## Recording

**Rec**, next to MIDI, writes the keys you play into the pattern. While the song plays, each key is written at the row you heard when you pressed it (rounded to the nearest row), in the pattern of the sequence slot playing then. While the song is stopped, keys are written at the cursor, and the cursor moves on by the edit step once every key is up. Chords fill the next note columns of the track. Key releases are not written. Every recorded note is its own Undo step.

## When something is wrong

- **"In use by another program"**: a DAW or another program has the keyboard open and Windows lets only one program read it. Close it there; ZTracker reconnects on its own.
- **"No MIDI input found"**: no keyboard is plugged in or Windows has not finished installing it. It appears on its own once Windows lists it.
- **A note keeps sounding**: press Panic. If MIDI messages arrive faster than the renderer can take them (only possible when the audio is not running), the overflow is dropped and every played note is released, so a lost key-up cannot leave a note stuck.

## Latency

A key is never routed through the editor's main thread. The keyboard thread hands it to the renderer through a small queue that the renderer reads at the start of every audio block, so the key sounds from the first sample of the next block. Measured in the editor with the project's default buffer (1024 samples, four buffers queued, 48 kHz): from the key arriving to the renderer applying it took 8.6 ms in the middle case, 29 ms in the slowest 5 % and 38 ms at most. The output buffers then add roughly another 64 to 85 ms before the sound leaves the computer, so the default buffer gives a noticeably late feel to live playing.

For playing live, choose **Buffer: 256** in the MIDI settings: each buffer is then about 5 ms, the wait for the next block is at most about 5 ms, and the queued buffers about 21 ms. If you hear crackles, step up to 512. The setting changes only the running editor's audio and stops a playing song once (press Play again). Audio quality is the same at every buffer size: the buffer changes when audio is produced, not how.

Windows itself and the audio driver add some delay that ZTracker cannot measure; ASIO-class drivers are not used by Unity.

## For programmers

The keyboard reader, the router (settings, routing, key-up bookkeeping) and the simulated keyboard live in the playback assembly under `Runtime/Playback/Midi`. Game code can use them too: give a router a renderer's live-input door (`TrackerSapGenerator.LiveInput`) with a track, cursor column and instrument, and open a source with that router. Another kind of input (a network or on-screen keyboard) only needs to implement the small source interface or call the router's `Receive` with packed MIDI bytes.

Rules the live path keeps:

1. The renderer applies live events at the start of each block, before producing any sample of it, and does no managed work and no allocation doing so. Events are fixed-size records with no references.
2. The live queue is bounded and never blocks a producer. When full, the newest event is dropped and counted, and the renderer then releases every live note.
3. Any number of input threads may write; only the renderer reads. A host closes its door before the queue's memory is freed, and the door refuses writes from then on, so a key arriving during shutdown is dropped safely.
4. A key-up always goes to the renderer that received its key-down, even after the target moved (another workspace, a restarted song).
5. Reading a message on the input thread allocates nothing and takes no lock.

Kept checks: `TrackerMidiCheck.Execute()` (engine and router, offline, compiled renderer) and `ZTrackerModelWindow.StartMidiUiCheck()` then `ZTrackerModelWindow.MidiUiCheckReport` (the real window with real clicks and audio graphs), both through Unity CLI eval.

## Not verified without hardware

Opening a real USB keyboard, its real timestamps and hot-plug, a device held by a DAW, and the true key-to-ear delay through an audio interface were not tested; the checks use the simulated keyboard through the same router and renderer path. The device list reading and opening code was exercised only against the MIDI inputs present on the development machine (virtual ports), without opening them.
