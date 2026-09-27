# Audio baseline, captured 2026-09-26, BEFORE the Burst port changed anything

This is the reference the restructure is checked against. It was produced by running the existing offline renderer — the same engine code the audio thread runs — on a synthetic, fully reproducible input. Re-run the probe (source preserved beside this file) after each restructuring step and diff against these numbers.

## Conditions, chosen deliberately to sit on the dangerous boundaries

- Output sample rate **48000**. Chosen because it does **not** divide the reverb tuning table's 44100 base evenly, so the reverb's buffer-length scaling is a genuine fractional product rather than a clean multiple. At 44100 this trap is invisible.
- Delay time **250 ms**, which at 48000 is **exactly 12000 samples** — a whole-sample boundary. This is the shape of computation that previously produced a real off-by-one at the ring wrap.
- Source: an impulse at frame 0 (exposes tap positions and reverb structure) plus a 440 Hz sine at quarter amplitude (exposes interpolation), right channel at half the left. Half a second of source, one second rendered so tails are included. No asset, so this is reproducible anywhere.

## Derived sizes — the numbers a rounding change would break silently

| Quantity | Value |
|---|---|
| Delay ring frames (time 250, max 500, rate 48000) | **24005** |
| Reverb state floats at 48000 | **28266** → **28264** after unifying the formula, see below |
| Reverb state floats at 44100 | **26030** (unchanged) |

**Why the 48 kHz reverb figure moved by two floats, and why that is the right answer.** The reverb's buffer length used to be computed in two places with two different expressions; they have since been unified so the sizing and the render share one function. At 44.1 kHz the result is identical. At 48 kHz the new figure is **two floats smaller**, and the cause is precisely the float-rounding trap this document exists to guard against: the old sizing hoisted the sample-rate ratio into a local variable before multiplying, while the shared function computes it inline — and C# is permitted to evaluate the inline form at wider precision, so the ceiling lands differently for two of the twelve slots.

So the two-float drop is not a regression. It is the removal of an artefact of the *old duplicate's* different rounding. The new figure is, by construction, at least what the render consumes, because both now call the same function — which the old pair could never guarantee. **Treat 28264 as the reference from now on**, and treat a return to 28266 as a sign that someone has reintroduced a second copy of the calculation.

The reverb figures are the dangerous pair. That number decides how much per-voice state is allocated, so if the ported code computes it even one float differently from the code that allocated the buffer, the result is a memory error rather than a wrong sound. The two rates differ by a ratio of about 1.0859 against a rate ratio of 1.0884, which confirms the per-stage ceiling is doing real work and is rate-sensitive.

Note: the delay ring came out at 24005, not the 24004 a plain "ceiling of the product, plus four" would give. Do not treat that as a bug — it is the current behaviour and therefore the thing to preserve. It is recorded here precisely so nobody "fixes" it into a different number.

## Whole-render checksums — the fast regression detector

**The checksum is the reliable signal. Use it.** It reproduced bit-identically across separate runs and across a change proven neutral, so any movement in it is real.

| Chain | Frames | Blocks | Peak | Non-zero | Checksum |
|---|---|---|---|---|---|
| dry (no effects) | 24576 | 24 | 1 | 24000 | 0.62498872469826772 |
| delay | 48000 | 47 | 0.500000834 | 47998 | 0.80033948923215359 |
| reverb | 48000 | 47 | 0.5 | 48000 | 2.240136952959034 |
| modulator — oscillator | 24576 | 24 | 1 | 13818 | 3.7184298986113289 |
| modulator — envelope | 24576 | 24 | 1 | 24000 | 0.62498872469826772 |
| modulator — step list | 24576 | 24 | 1 | 23996 | 3.0997783891205772 |

### Why the modulator rows exist

The layout holds three variable-length-per-modulator arrays — each modulator's own parameters, its curve, and its step list — and those are the one part of the layout that is not already a flat indexed structure, so they are the part the port has to reshape. **Without these rows, that reshaping would be an unverifiable change.** The oscillator and step-list rows have clearly distinct checksums, so those two arrays are genuinely exercised.

**One weakness to be aware of rather than trust:** the envelope row's checksum is *identical to dry*, because a default envelope multiplies by one throughout and so has no audible effect. It does **not**, on its own, prove the curve data reaches the render. The curve is nevertheless covered, because the oscillator modulator also uses it as a ramp over time and that row does differ from dry. If you want a direct check, give the envelope a non-flat shape first.

### The frame counts differ on purpose

The dry and modulator voices stop at 24576 because their source is exhausted and the voice frees itself; the delay and reverb chains keep the voice alive to 48000 so their tails ring out. A port that made these equal would have broken tail handling.

### Do NOT trust this probe's allocation figure — but the property itself is now MEASURED elsewhere

**Settled 2026-09-27.** The zero-allocation property has since been established properly, on a quiet heap, one chain at a time, with a discarded warm-up measurement and a long enough run that a per-block allocation of even a few bytes would accumulate into an obvious number. Result: **zero bytes, on five chains including all sixteen effects at once and a modulated chain, on two independent runs each.** The retraction below still applies to THIS probe's figure, which remains too noisy to use — it just no longer means the property is unknown.

The earlier retraction, kept because the reasoning still matters:


An earlier version of this document claimed the zero-allocation guarantee was "measured" here. **That was an overclaim and is retracted.** The figure comes from sampling total managed heap around the render loop, which also catches anything else allocating at the same time — including the probe's own text building between captures, and ordinary editor activity. Observed values for the *same unchanged engine code* ranged from 0 to 184320 bytes across runs purely depending on what else was happening.

The engine almost certainly does not allocate per block — it was written not to, and the offline renderer has a dedicated facility for checking exactly that. But **proving it needs that facility used deliberately on a quiet heap, one chain at a time, not this multi-capture probe.** Treat the property as an unverified claim until then, and do not let a low reading here be mistaken for evidence.

## Exact samples at the boundaries that matter

Delay — the tap lands **exactly** on frame 12000, as intended:

```
[11999] L=-0.007197311    R=-0.00359865557
[12000] L=0.500000834     R=0.250000417     <-- the delayed impulse, sample-exact
[12001] L=0.0148757752    R=0.0074378876
[24000] L=0.199798509     R=0.0998992547    <-- second tap (feedback), also exact
```

Dry, for comparison, has no such event — frame 12000 is just the sine at 1.7088114E-06.

Reverb — dense tail energy by 12000, right channel much quieter than left because width is at maximum:

```
[11998] L=-0.152883008   R=-0.00589549169
[12000] L=-0.158541441   R=-0.0105199981
[23999] L=-0.1375978     R=0.0183329247
[47999] L=0.001073572    R=0.00562811969
```

Delay defaults for reference: time 250 ms, feedback 0.4, mix 0.3, max time 500 ms, ping-pong off. Reverb defaults: room 0.5, damping 0.5, width 1, mix 0.3. The captures above used mix 0.5 for both so the effect is clearly visible against the dry signal.

## How to use this

1. Re-run the probe unchanged. Every number above should match exactly while the change is meant to be behaviour-neutral (the container conversions, the maths-helper swap, the layout flattening).
2. **One step is expected to change these numbers: unifying the read cursor away from double precision.** Short-sound figures should match to within rounding; a large difference means something else broke. Capture a second baseline immediately after that step and carry both forward.
3. When Burst compilation is switched on, diff again. A difference at that point is the compiler's floating-point behaviour, not your arithmetic — which is exactly why the restructure is verified first, while the two causes are still separable.
