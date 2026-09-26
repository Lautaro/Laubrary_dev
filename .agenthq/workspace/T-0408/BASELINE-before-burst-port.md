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
| Reverb state floats at 48000 | **28266** |
| Reverb state floats at 44100 | **26030** |

The reverb figures are the dangerous pair. That number decides how much per-voice state is allocated, so if the ported code computes it even one float differently from the code that allocated the buffer, the result is a memory error rather than a wrong sound. The two rates differ by a ratio of about 1.0859 against a rate ratio of 1.0884, which confirms the per-stage ceiling is doing real work and is rate-sensitive.

Note: the delay ring came out at 24005, not the 24004 a plain "ceiling of the product, plus four" would give. Do not treat that as a bug — it is the current behaviour and therefore the thing to preserve. It is recorded here precisely so nobody "fixes" it into a different number.

## Whole-render checksums — the fast regression detector

| Chain | Frames | Blocks | Peak | Non-zero | Allocated during render | Checksum |
|---|---|---|---|---|---|---|
| dry (no effects) | 24576 | 24 | 1 | 24000 | 40960 bytes | 0.62498872469826772 |
| delay | 48000 | 47 | 0.500000834 | 47998 | **0** | 0.80033948923215359 |
| reverb | 48000 | 47 | 0.5 | 48000 | **0** | 2.240136952959034 |

Two things worth reading off this table:

**The zero-allocation guarantee is real and now measured.** Both effect renders allocated nothing at all across 47 blocks. That property must survive the port — it is half the reason this engine was written the way it was. The dry render's 40960 bytes is first-call warm-up, not per-block churn; compare like with like.

**The frame counts differ on purpose.** The dry voice stops at 24576 because its source is exhausted and the voice frees itself; the effect chains keep the voice alive to 48000 so their tails ring out. A port that made these equal would have broken tail handling.

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
