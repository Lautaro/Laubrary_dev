# DIAL-MAP — Fork Blast (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `progress` | Progress | Progress | Clock | The blast's progress over the layer's life — a TIME REMAP as one envelope. The default straight line plays in real time; bend it to snap in and hold, slow the tail, or freeze a pose (a Static value). |
| `reach` | Reach | Travel distance | Clock | How far the fastest puffs travel, as a fraction of the canvas half-extent, over life. |
| `flash` | Flash | Ignition flash | Clock | White-hot ignition flash at each blast's birth, over life. Tinted from the Fill's hot end. |
| `spread` | Spread | Spread angle | Emission shape | Half-angle of the emission arc, degrees. 180 = a full circle (a true radial blast). |
| `aim` | Aim | Aim direction | Emission shape | The arc's centre direction, degrees. Only visible when Spread is below 180 (a full circle has no facing). |
| `bias` | Bias | Angle bias | Emission shape | Angle-distribution power across the arc. 1 = uniform coverage — keep this near 1 on a full circle, or the puffs pile back into a beam. |
| `puffs` | Puffs | Puff count | Emission shape | Puffs per blast. |
| `drag` | Drag | Drag | Puff physics | Higher decelerates a puff sooner, so it stalls closer to the source. |
| `buoyancy` | Buoyancy | Rise (buoyancy) | Puff physics | Upward rise late in a puff's life. |
| `growth` | Growth | Growth by distance | Puff physics | Radius gained per pixel travelled (entrainment) — a puff fattens as it slows. |
| `swell` | Swell | Growth by age | Puff physics | Radius gained per unit AGE rather than distance — fills a stalled centre so the fireball doesn't hollow into a smoke ring. |
| `elongation` | Elongation | Birth stretch | Puff physics | Extra length/width at birth, along the puff's own travel direction; decays as it slows. |
| `roundAt` | Round At | Round by age | Puff physics | The age by which a puff has stopped stretching and is round again. |
| `jitter` | Jitter | Puff variation | Puff physics | Per-puff variation in speed / size / amplitude / life. |
| `fillVolume` | Fill Volume | Fill the centre | Puff physics | 0 = every puff leaves at one speed, which reads as a hollow expanding SHELL. Above 0 spreads the speeds so the middle fills in with slow-travelling gas instead of hollowing into a smoke ring. |
| `birthSkew` | Birth Skew | Birth attack | Detonation timing | Above 1 piles puff births at the FRONT of the birth span — a hard attack with a ragged tail, which is what makes this read as a detonation rather than a steady jet. |
| `birthSpan` | Birth Span | Birth spread | Detonation timing | How much of the blast's own clock the puff births are spread over. |
| `puffLife` | Puff Life | Puff lifetime | Detonation timing | How long a puff burns, as a fraction of the blast's own clock. |
| `cool` | Cool | Cooling rate | Detonation timing | Amplitude falloff exponent over a puff's life. |
| `hold` | Hold | Brightness hold | Death | Above 0 holds a puff's amplitude up and drops it LATE instead of dimming from birth — the delay that keeps the body solid long enough for Shrink to be the thing you see. |
| `opacity` | Opacity | Solidity | Death | Exponent on the Fill's own alpha ceiling. Below 1 pushes the body toward solid while doing least at the coolest, already-thin rim — so the body opens up without trading away the edge falloff. |
| `shrink` | Shrink | Shrink amount | Death | How much of its radius a puff loses by the end of its life — dies by getting SMALLER, not more transparent. The kernel's peak is its amplitude, so a shrinking puff stays exactly as bright at its centre. |
| `shrinkAt` | Shrink At | Shrink starts at | Death | The age the contraction starts at. |
| `leadDie` | Lead Die | Outer dies first | Death | The fastest (outermost) gas dies first, so the silhouette closes INWARD as it collapses — shrinking every puff by the same amount alone leaves the outer shell in place and merely makes it smaller. |
| `gobs` | Gobs | Gob count | Shed mass (gobs) | Lumps of burning mass shed off the blast in its OPENING phase, that then dissipate — optional, 0 = none. Opposite death to the body: it balloons and thins as it goes, which is what makes it read as having come off something. The Gob dials below act only when this is above 0. |
| `gobSizePx` | Gob Size Px | Gob size | Shed mass (gobs) | A gob's radius — mass, not a spark; several times a puff's own size. |
| `gobReach` | Gob Reach | Gob travel | Shed mass (gobs) | A gob's travel, as a fraction of Reach. |
| `gobSwell` | Gob Swell | Gob swell | Shed mass (gobs) | A gob DISSIPATES — it balloons as it goes out, the opposite of the body, which shrinks and stays solid. |
| `gobLife` | Gob Life | Gob lifetime | Shed mass (gobs) | A gob's life, as a multiple of Puff life. |
| `gobAmount` | Gob Amount | Gob brightness | Shed mass (gobs) | Gob brightness. |
| `gobTiming` | Gob Timing | Gob birth window | Shed mass (gobs) | Gobs are born inside this fraction of the birth span — the opening phase. A gob that leaves late just reads as a second, smaller explosion. |
| `puffSizePx` | Puff Size Px | Puff size | Look & exposure | A puff's radius at the source. |
| `turbulencePx` | Turbulence Px | Puff wobble | Look & exposure | A per-puff wobble that breaks up the disc into licks — an approximation of true domain-warp turbulence. |
| `soot` | Soot | Soot tint | Look & exposure | Tint gained by the end of a puff's life, darkening/desaturating it — a third route to transparency-reading if pushed too high; keep it modest. |
| `edgeSoftness` | Edge Softness | Edge softness | Look & exposure | Width of the falloff at the silhouette's edge, in heat-field units. |
| `autoExposure` | Auto Exposure | Auto exposure | Look & exposure | Fit the heat ceiling to this frame's own measured peak instead of a fixed Field high. On by default — leaving it off means Field high has to be re-fitted by hand any time puff count, amplitude, blast count, or almost any other dial changes, or the blast reads as a flat, washed-out silhouette (ceiling too high) or a blown-out core (ceiling too low). |
| `exposure` | Exposure | Exposure | Look & exposure | Multiplier on the measured peak of this frame's heat field. Lower = brighter/hotter overall (clips more of the field to the ramp's hot end); higher = dimmer, more rim. |
| `fieldLow` | Field Low | Field floor | Look & exposure | Heat-field value at the silhouette's outer edge — everything below this is fully transparent. |
| `fieldHigh` | Field High | Field ceiling | Look & exposure | Heat-field value at which the Fill ramp tops out (hottest). Puff count/amplitude/overlap shift the field's real range, so this and Field low are the two dials that keep the blast from reading as all-rim or all-core. |
| `curve` | Curve | Ramp bend | Look & exposure | Bends where the ramp is spent along the heat field. Below 1 = hotter/brighter overall, above 1 = mostly cool envelope with a tight hot spine. |
