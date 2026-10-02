# The Laubrary Dev editor died with a segfault on the audio thread — read this before dismissing it

Left by the agent being retired, for whoever is now working on the stop-completion barrier. I did not write, touch,
commit or revert any of the in-flight source changes for this task; they are yours and I left them exactly as found.

## What happened

The Laubrary Dev editor process exited with signal 11 (a segmentation fault). This was reported to me as a background
task failure, after my own work was finished and committed.

## Why it is probably a RESULT rather than a setback

The crash stack, preserved beside this note in `editor-crash-2026-09-27-stack.txt`, ends like this:

```
audio::GeneratorFMODRead
FMOD::DSPFilter::read
... (a stack of DSP filter reads)
FMOD::DSPSoundCard::read
FMOD::Output::mix
FMOD::OutputWASAPI::mixerUpdate
FMOD::Thread::callback
```

That is **the audio mixer thread, faulting while reading a generator** — in other words, the audio side was in the
middle of producing samples for a voice when the memory under it went away. That is precisely the hazard this task is
blocked on, and precisely what the check being written here set out to determine.

So the likely reading is that the bad case is **real and demonstrable**: tearing a voice down does not guarantee the
audio side has stopped reading it by the time the call returns. A crash is an unpleasant way to learn that, but it is a
far stronger answer than any amount of reasoning about the documentation would have been.

## The caveats, because this deserves confirming rather than assuming

- **The timing is suggestive, not conclusive.** The crash is logged at 14:54:17; the current version of
  `ZoundsStopCompletionCheck.cs` was saved at 14:54:58, i.e. *after*. That is consistent with an earlier version of the
  probe having been run and crashed, and the file then being edited further — but it does not prove the probe caused it.
- **Rule out the other candidate.** Nothing in the work committed before this frees voice memory: the live-parameter
  read and the output monitor both only READ memory the audio thread writes, which is a deliberate and documented benign
  race for a display, not a free. But confirm that yourself rather than taking my word for it.
- **This branch has a specific history of confident reasoning being wrong** (three occasions, listed in T-0418). Treat
  the above as a strong hypothesis to be confirmed, not as a finding.

## What would confirm it

Reproduce deliberately and in isolation, with nothing else running in the editor, and with the probe written so that the
dangerous step is the only thing that could fault. If it reproduces, the answer to this task's blocking question is
settled in the worst direction, which means the barrier genuinely has to wait for confirmation rather than assume it —
and the memory saving behind it must not be attempted until that barrier exists.

If it does NOT reproduce, suspect the editor was destabilised by something else and keep looking, because an
intermittent fault here is worse than a reliable one.
