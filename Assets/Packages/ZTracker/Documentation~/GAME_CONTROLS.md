# Game controls

Call these methods on a playing `ZTrackerPlayback` from the main thread. Each returns whether the request was accepted for delivery, not whether the speaker has already played it. The existing SAP command pipe transfers fixed-size records; the renderer never resolves strings or touches game objects. A command takes effect after delivery at an audio block boundary unless a row boundary is specified below. Output buffering still separates rendering from what the listener hears; use `Clock.TryGetHeard` for gameplay tied to audible rows.

| Method | Behavior |
| --- | --- |
| `SetTempo(float bpm)` | Accepts 32–999 BPM. Last queued value wins, beginning at the next row. Current row duration is unchanged. This is a persistent override above authored tempo commands and lanes; authored values continue advancing beneath it. Authored transport-stop commands still stop. |
| `ClearTempoOverride()` | Removes the override at the next row, restoring the then-current authored tempo, including intervening authored changes. |
| `SetTrackTranspose(int track, int semitones)` | Sets an additive offset from −120 through +120 semitones for all active and future voices on this track. Zero restores authored pitch. |
| `SetTrackDetune(int track, float cents)` | Sets an independent additive offset from −1200 through +1200 cents. It composes with transposition, authored fine tuning, macros, per-note envelopes and modulation. |
| `SetCommandParameter(string uid, float normalized, int track = -1)` | Resolves an authored dynamic command definition by its stable `uid`, not its two-character display ID. Accepts 0–1 within the definition's declared range. Instrument-effect definitions need the sequencer track index because each track owns a separate effect instance. Other scopes use their definition's target. |

Pitch offsets are applied to rendering after note/sample selection. They do not rewrite authored cells, alter drum mapping or change the pitch values in note callbacks. A game excludes drums by leaving their track at zero. Set transposition and detune separately so a health detune can be cleared without clearing a progression's semitone offset. Both members of a paired sample receive the offsets. Extreme combined pitch still uses the engine's existing bounded-pitch diagnostics.

Stable command writes follow the existing evaluator: track controls ramp over 5 ms; instrument/macros/effect settings ramp over 10 ms in four-sample slices. Later authored commands or automation can replace these writes. Dynamic tempo writes are exceptional: they wait for row entry, follow that row's authored timing writes and remain below a persistent `SetTempo` override. Pending writes to one definition coalesce to the last value. Invalid IDs, unsupported targets and nonfinite/out-of-range inputs are refused without rebuilding the song. Compiled indices are private to each preparation; resolve again on each send rather than caching across refresh.

Play, Stop and Seek clear game pitch offsets and the tempo override. A compatible live refresh preserves them and pending dynamic tempo writes. Pause/resume preserve them. A full replacement uses its own fresh defaults. Resetting these controls does not mutate saved assets.

`TrackerCommand.SetTempo`, `ClearTempoOverride`, `SetTrackTranspose` and `SetTrackDetune` expose the same fixed-size requests for lower-level consumers. `TrackerPreparedSong.TryCommandParameter` resolves the stable definition on the main thread; `TrackerOffline.SetCommandParameter` provides the same resolution for deterministic rendering.

Run `Laubrary.ZTracker.Engine.TrackerGameControlCheck.Execute()` through editor eval for scoped checks. It creates and destroys in-memory fixtures, does not open a device or window and does not force collection. It checks row timestamps across three block sizes, override precedence and clearing, effect ramp/output, active/future/excluded-track pitch, sample pairs, macro composition, refresh/reset and rejected inputs.
