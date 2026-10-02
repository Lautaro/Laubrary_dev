# DIAL-MAP — Torch, TorchSettings (shared by all 5 variants) (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `hFlame` | H Flame | Flame reach | Shape | Reach of the flame in SOURCE px — the reference every other px value here is relative to (the canvas height comes from the shared Height dial: u = Height × canvas / this). |
| `w0` | W0 | Base width | Shape | Half-width of the source at the base, px. |
| `wp` | Wp | Taper rate | Shape | How fast the width collapses with height: w = w0·(1 − Hn)^wp — low = a near-parallel column, high = a cone. |
| `wmin` | Wmin | Tip floor | Shape | Width floor at the tip, px — a REAL fraction of w0, so the tip is as wide as a tongue (the needle fix). |
| `uexp` | Uexp | Side shape | Shape (advanced) | Lateral falloff exponent: exp(−(u²)^uexp·ku), u = x / width. |
| `ku` | Ku | Side rate | Shape (advanced) | Lateral falloff rate. |
| `vexp` | Vexp | Height shape | Shape (advanced) | Vertical falloff exponent: exp(−Hn^vexp·kv). Must stay ABOVE wp's effect so intensity dies faster than width (the needle rule). |
| `kv` | Kv | Height rate | Shape (advanced) | Vertical falloff rate. |
| `gain` | Gain | Core heat | Shape | Heat at the heart; the band thresholds are compared against a field that peaks near this. |
| `cap0` | Cap0 | Ceiling start | Shape | Height (fraction of the reach) where the ceiling starts eating the source — the flame ends in the ragged zone between Cap0 and Cap1 where the noise owns the contour. |
| `cap1` | Cap1 | Ceiling end | Shape | Height (fraction of the reach) where the ceiling has fully cut the source. |
| `rmin` | Rmin | Root width | Shape | Width at the very bottom relative to w0 (the root pinch: a torch grows out of something; a campfire sits flat at 1). |
| `rh` | Rh | Root open | Shape | Height (fraction of the reach) over which the root pinch opens to full width. |
| `foot` | Foot | Foot bite | Shape | Bite taken out of the heat in the bottom rows (the light-bar fix): the fuel is not the hottest part of a fire, the gas above it is. Kept shallow so it never becomes a dark shelf. |
| `footH` | Foot H | Foot bite | Shape (advanced) | Height of the foot bite, px. |
| `frx` | Frx | Reach freq X | Breathing (advanced) | Reach-variation noise frequency along x (cells per Xsc): low Fry makes it almost a function of x alone, so neighbouring columns disagree and tongues of different heights stand side by side. |
| `fry` | Fry | Reach freq Y | Breathing (advanced) | Reach-variation noise frequency along y (cells per Ysc). The scroll period is round(12·Fry) lattice cells per loop. |
| `rvar` | Rvar | Height var. | Breathing | Amplitude of the reach variation (± fraction of the reach). |
| `breathe` | Breathe | Breath slow | Breathing | Breathing of the whole body, first harmonic (± fraction of the reach, once per loop). |
| `breathe2` | Breathe2 | Breath fast | Breathing | Breathing second harmonic (twice per loop) so the pulse is not a metronome. |
| `bphase` | Bphase | Breath phase | Breathing (advanced) | Phase of the first breathing harmonic, radians. |
| `fw` | Fw | Sway freq | Sway & lean (advanced) | Sway noise frequency along x (cells per Xsc). |
| `yaniso` | Yaniso | Sway stretch | Sway & lean (advanced) | Vertical anisotropy of the sway noise (its y frequency = Fw × this); the scroll period is round(12·Fw·Yaniso) cells per loop. |
| `sway` | Sway | Sway amount | Sway & lean | Noise-driven lateral displacement at the top, px — on the order of the flame's own WIDTH, not a fraction of it (a 2 px wiggle on an 11 px column reads as a jittering cone). |
| `lean` | Lean | Lean amount | Sway & lean | Rigid lean at the top, px, once per loop. |
| `lphase` | Lphase | Lean phase | Sway & lean (advanced) | Phase of the lean, radians. |
| `lash` | Lash | Whip amount | Whip | Whip amplitude at the top, px. 0 = off. The wave travels UP the flame so the column takes an S and the tip cracks. |
| `lashK` | Lash K | Whip cycles | Whip (advanced) | Whip cycles per loop — an integer so the wave closes with the loop. |
| `lashWave` | Lash Wave | Whip lag | Whip (advanced) | How far the whip's phase lags per unit height (radians per reach): the base is already returning while the top is still going out. |
| `lashPh` | Lash Ph | Whip phase | Whip (advanced) | Whip phase, radians. |
| `pulse` | Pulse | Surge jump | Surge | Reach jump per surge (fraction of the reach). 0 = no surge (calm draws cost nothing here). |
| `pulseN` | Pulse N | Surges | Surge (advanced) | Surges per loop — an integer so the loop closes. |
| `pulsePh` | Pulse Ph | Loop seam | Surge (advanced) | Where in the surge the loop seam falls (fraction of a surge). Put it in the slow decay (≈0.7), not the fast attack, or the wrap lands on the steepest frame. |
| `pulseSkew` | Pulse Skew | Surge attack | Surge (advanced) | Attack asymmetry: below 1 the peak comes early (≈28 % of the cycle at 0.55) — a jump then a settle, not a breath. |
| `pulseSharp` | Pulse Sharp | Surge tail | Surge (advanced) | Tail flattening: higher = the flame spends more of the cycle low and is only briefly tall. |
| `pulseGain` | Pulse Gain | Surge glow | Surge | Brightness jump per surge (fraction of Gain) — fire that surges gets hotter as well as taller. |
| `bulge` | Bulge | Heat lump | Surge | Heat of the lump that rides UP the column on each surge — what actually makes a surge legible (taller alone reads as a zoom). |
| `bulgeW` | Bulge W | Lump height | Surge | Height of the travelling lump, fraction of the reach. |
| `curl` | Curl | Curl warp | Curl warp | RMS displacement of the divergence-free warp on the noise sampling coordinates, px. 0 = off. Lobes ROLL over instead of wobbling (the warp cannot compress anything). |
| `curlX` | Curl X | Curl freq X | Curl warp (advanced) | Curl potential frequency along x (cells per Xsc). |
| `curlY` | Curl Y | Curl freq Y | Curl warp (advanced) | Curl potential frequency along y (cells per Ysc); scroll period round(12·CurlY) cells per loop. |
| `bigKind` | Big Kind | Lobe noise | Texture | Kind of the big-lobe noise (2 octaves). |
| `abig` | Abig | Lobe amt. | Texture | Amplitude of the big-lobe noise (added to the envelope: the silhouette is a CONTOUR of source + noise). |
| `turbKind` | Turb Kind | Lick noise | Texture | Kind of the lick noise: ridged carves the upper flame into separate tongues; billow makes rounded lumps. |
| `alick` | Alick | Lick strength | Texture | Amplitude of the lick noise. |
| `fbx` | Fbx | Lobe freq X | Texture (advanced) | Big-lobe noise frequency along x (cells per Xsc). |
| `fby` | Fby | Lobe freq Y | Texture (advanced) | Big-lobe noise frequency along y (cells per Ysc); scroll period round(12·Fby). |
| `ftx` | Ftx | Lick freq X | Texture (advanced) | Lick noise frequency along x (cells per Xsc). |
| `fty` | Fty | Lick freq Y | Texture (advanced) | Lick noise frequency along y (cells per Ysc); scroll period round(12·Fty). |
| `toct` | Toct | Lick octaves | Texture (advanced) | Octaves of the lick noise (at most 4 with the 64-cell lattice). |
| `bias` | Bias | Erosion bias | Texture (advanced) | Constant subtracted with the noise (field units) — erodes the contour so licks pinch off. |
| `pexp` | Pexp | Gate shape | Texture (advanced) | Exponent on the envelope that gates the noise (famp = shape^pexp): the noise lives where the source is, normalised so Gain moves colour, not shape. |
| `still` | Still | Base height | Texture (advanced) | Height (fraction of the reach) over which the noise fades in from the base — keeps the bottom rows quiet so the flame stays anchored. |
| `xsc` | Xsc | Noise size X | Texture (advanced) | Noise cell size along x, px per lattice cell. |
| `ysc` | Ysc | Noise size Y | Texture (advanced) | Noise cell size along y, px per lattice cell. |
| `glow` | Glow | Bed glow | Fuel bed glow | Heat of the wide low pool under the flame, shimmering slowly. 0 = off. Keep it a THIN strip: too much and the whole draw is one glowing loaf. |
| `glowW` | Glow W | Bed width | Fuel bed glow | Half-width of the fuel-bed pool, px. |
| `glowH` | Glow H | Bed height | Fuel bed glow | Height of the fuel-bed pool above Glow Y, px. |
| `glowY` | Glow Y | Bed pos. | Fuel bed glow | Height above the fuel bed where the pool starts, px. |
| `tongues` | Tongues | Tongue count | Tongues | Discrete licks alive in the population — flame-shaped tongues that peel off the flanks, lean outward as they climb and burn out. 0 = off. |
| `tongueGain` | Tongue Gain | Tongue glow | Tongues | Heat of a tongue at its peak. |
| `tongueX` | Tongue X | Root spread | Tongues | Root distance from the axis, px (range, either side). MUST sit at the body's rim or the licks live inside the core and add nothing. |
| `tongueY` | Tongue Y | Root height | Tongues | Root height above the fuel bed, px (range). |
| `tongueRise` | Tongue Rise | Tongue climb | Tongues | How far a tongue climbs over its life, px (range). |
| `tongueOut` | Tongue Out | Tongue peel | Tongues | How far a tongue peels away sideways over its life, px (range). |
| `tongueW` | Tongue W | Tongue width | Tongues | Tongue half-width, px (range). |
| `tongueL` | Tongue L | Tongue length | Tongues | Tongue length, px (range). |
| `tongueLife` | Tongue Life | Tongue life | Tongues | Tongue lifetime as a fraction of the loop (range). |
| `tongueLean` | Tongue Lean | Tongue lean | Tongues | How much a tongue's upper part leans outward as it ages (shear of its shape). |
| `embers` | Embers | Ember count | Embers | Sparks in the population — cooling fuel in world space, taking the same palette. 0 = off. |
| `emberSpread` | Ember Spread | Ember spread | Embers | Half-width of the ember launch zone across the axis, px. |
| `emberRise` | Ember Rise | Ember rise | Embers | How far an ember rises over its life, px (range). |
| `emberDrift` | Ember Drift | Ember drift | Embers | Sideways drift over an ember's life, px (× −1..1 per ember). |
| `emberWobble` | Ember Wobble | Ember wobble | Embers | Sideways wobble amplitude, px. |
| `emberSize` | Ember Size | Ember size | Embers | Ember radius, px (range). |
| `emberY0` | Ember Y0 | Ember height | Embers | Launch height above the fuel bed, px. |
| `emberGain` | Ember Gain | Ember glow | Embers | Heat of an ember at its peak. |
| `cool` | Cool | Height cool | Colour | How much the colour field is cooled with height: C = H·(1 − cool·clip(Hn, 0, 1.4)) — tips go red while the heart stays white, without the silhouette changing. |
| `aLo` | A Lo | Fade-in point | Colour | Heat where opacity lifts off (field units). Below it the pixel is transparent. |
| `aHi` | A Hi | Solid point | Colour | Heat where opacity reaches 1 (field units). Alpha is a smoothstep between A Lo and A Hi on the raw heat — continuous, no threshold, no dither. |
| `ramp` | Ramp | Colour ramp | Colour | The cel palette: a free gradient sampled into hard bands (thresholds evenly divide 0..Ramp Top). Edit the ramp freely; the Bands count only changes the sampling resolution, so it never loses a colour you've picked. Presets: PyreRampPresets.TorchGradient(hot / ember / white / rim / gold). |
| `rampTop` | Ramp Top | Ramp ceiling | Colour | Field value of the ramp's top (position 1). Thresholds are compared against a heat that peaks near Gain, so this must sit ABOVE the peak or the whole core lands in the top band (the white-blob fix). |
