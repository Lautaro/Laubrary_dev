# DIAL-MAP — Jet family shared settings, JetSettings (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `w` | W | Source frame width (px) | Source frame (advanced) | Width of the draw's SOURCE frame in px — the reference every px dial and every canvas-width dial here is relative to (Scale × canvas width / this = the scale). |
| `h` | H | Source frame height (px) | Source frame (advanced) | Height of the draw's source frame in px. Nothing is drawn outside the frame. |
| `nozzleX` | Nozzle X | Nozzle across frame | Source frame (advanced) | Where the nozzle sits across the source frame, as a fraction of its width (contract `nozzle` x). |
| `nozzleY` | Nozzle Y | Nozzle down frame | Source frame (advanced) | Where the nozzle sits down the source frame, as a fraction of its height from the TOP (contract `nozzle` y; the source is y-down). |
| `aim` | Aim | Aim direction | Where it goes | Aim in degrees: 0 = straight right, + = downward (the source's y-down frame). |
| `reach` | Reach | Travel distance | Where it goes | Travel of a puff over its whole life, in canvas WIDTHS of the source frame. |
| `spread` | Spread | Spread angle | Where it goes | Half-angle of the emission arc, degrees (180 = a full disc). The directional jet biases angles towards the axis by a fixed \|x\|^1.7 power, so the stream has a spine and a ragged fringe rather than a paper fan; the radial jet exposes that power as its Bias dial. |
| `drag` | Drag | Drag | Puff physics | Exponential drag: > 0 decelerates, the higher the sooner the puff stalls. Travel = reach·(1 − e^(−drag·s))/(1 − e^(−drag)). |
| `buoy` | Buoy | Rise (buoyancy) | Puff physics | Upward rise by the end of a puff's life, canvas widths — applied as s^2.4, so the root runs flat and only the slowed tip rolls over. |
| `grav` | Grav | Sag (gravity) | Puff physics | Downward sag by the end of life, canvas widths — applied as s² (unburnt fuel is heavy). |
| `r0` | R0 | Puff size at nozzle | Puff physics | Puff radius at the nozzle, source px. |
| `growth` | Growth | Growth by distance | Puff physics | Radius gained per px travelled (entrainment): the cone fattens as it slows. |
| `elong` | Elong | Birth stretch | Puff physics | Extra length-to-width of a puff at birth (a streak along its velocity); decays as it slows. |
| `roundAt` | Round At | Round by age | Puff physics | Age (fraction of life) by which a puff is round again: aspect = 1 + elong·e^(−s/round_at). |
| `slots` | Slots | Puff slots | Emission | Puff slots. Slot i is born at phase i/slots every loop, so the set of live puffs at phase 1 is the set at phase 0. |
| `life` | Life | Puff lifetime | Emission | A puff's life as a fraction of the loop. |
| `jitter` | Jitter | Puff variation | Emission | Per-slot variation of speed / size / amplitude / life, 0..1 (scales the uniform jitters of the slot table). |
| `strength` | Strength | Heat strength | Emission | Peak heat a puff deposits (the kernel's height); overlap sums. |
| `cool` | Cool | Cooling rate | Emission | Amplitude falloff exponent over a puff's life: amp ∝ (1 − s)^cool. |
| `soot` | Soot | Soot tint | Emission | Soot tint gained by the end of a puff's life (tint = clip(soot·s)); drives the crossfade into the second ramp. 0 = the second ramp is never used. |
| `pulseN` | Pulse N | Surges per loop | Surge & sweep | Surges per loop frozen into each puff at birth; 0 = a steady jet. |
| `pulseDepth` | Pulse Depth | Surge depth | Surge & sweep | Depth of the surges: amp × clip(1 + depth·cos(2π·pulse_n·birth), 0.05, 2.5). |
| `sweep` | Sweep | Sweep angle | Surge & sweep | Degrees the aim swings either side, frozen into each puff at birth — a swept stream CURVES because its tail still points where the nozzle was. |
| `sweepN` | Sweep N | Sweeps per loop | Surge & sweep | Sweeps per loop (integer, so the loop stays exact). |
| `shockN` | Shock N | Shock diamond count | Shock diamonds | Shock diamonds: standing bright nodes down the axis, this many per reach; the gas travels through them. 0 = none. |
| `shockDepth` | Shock Depth | Shock diamond strength | Shock diamonds | Depth of the shock modulation: amp × (1 + depth·cos(2π·shock_n·d/reach)). |
| `rootR` | Root R | Root lump size | Root | Radius of the dense hot lump at the nozzle, source px; 0 = none. It is what makes the stream read as THROWN from a source rather than drifting. |
| `rootAmp` | Root Amp | Root lump brightness | Root | Heat of the root lump. |
| `shed` | Shed | Shed fraction | Shedding & sparks | Share of slots that detach: extra lateral throw, sideways drift, 0.72 amplitude and a longer life — fireballs tumbling off the end. |
| `shedKick` | Shed Kick | Shed throw | Shedding & sparks | A shed puff's extra angular throw (× its own cone angle) and drift scale. |
| `shedLife` | Shed Life | Shed lifetime | Shedding & sparks | A shed puff's life as a multiple of Life. |
| `sparks` | Sparks | Spark count | Shedding & sparks | Tiny fast bright motes torn off the stream (own stream seed·104729 + 77). |
| `sparkR` | Spark R | Spark size | Shedding & sparks | Spark radius, source px (never one pixel: 1.4 rasterises to a 9 px lump, the floor the reference sheets keep). |
| `ringN` | Ring N | Vortex ring count | Vortex rings | Vortex rings shed per loop, seen from the side as flattened O's travelling away; 0 = none. |
| `ringK` | Ring K | Ring puff count | Vortex rings (advanced) | Minimum puffs per ring; the count grows with the circumference so the ring stays closed. |
| `ringR0` | Ring R0 | Ring birth radius | Vortex rings | Ring radius at birth, source px. |
| `ringGrow` | Ring Grow | Ring growth | Vortex rings | Ring radius gained per px travelled. |
| `ringLife` | Ring Life | Ring lifetime | Vortex rings | Ring life as a multiple of Life — a ring has to OUTLIVE the stream to get clear of it. |
| `ringReach` | Ring Reach | Ring travel | Vortex rings | Ring travel as a multiple of Reach — and OUTRUN it. |
| `ringAmp` | Ring Amp | Ring brightness | Vortex rings | Ring heat relative to Strength (rings cool slower than the stream: (1 − s)^(0.3·cool)). |
| `warp0` | Warp0 | Turbulence at nozzle | Turbulence | Domain-warp amplitude at the nozzle, source px (a jet is laminar while it is fast). |
| `warp1` | Warp1 | Turbulence at reach | Turbulence | Warp amplitude added by the end of the reach, source px (it breaks up once it has slowed). |
| `warpCell` | Warp Cell | Turbulence cell size | Turbulence (advanced) | Source px per noise lattice cell — the texture scale. The downstream scroll is snapped to whole lattice periods per loop so the loop stays exact. |
| `warpOct` | Warp Oct | Turbulence octaves | Turbulence (advanced) | Noise octaves (lacunarity 2, gain 0.5). |
| `lo` | Lo | Field floor | Look | Field value at the silhouette's outer edge (lit = H·gain > lo). |
| `hi` | Hi | Field ceiling | Look | Field value at which the ramp tops out. FITTED per draw by the source's tune2 solver against two style targets (average ramp position, share of lit area in the top tenth) — not a guess; the shipped values are the contract's. |
| `curve` | Curve | Ramp bend | Look | Bends where the gradient is spent: t^curve, < 1 pushes area up the ramp (a hotter, fully developed flame). FITTED together with Hi. |
| `steps` | Steps | Colour bands | Look | Shades in the ramp: 0 = continuous; N quantises the ramp coordinate to N shades (floor, so the darkest shade reaches the edge). Never quantises the alpha. |
| `soft` | Soft | Edge softness | Look | Width of the edge falloff in field units: alpha = smoothstep((H − lo)/soft) × the ramp's opacity ceiling. |
| `gain` | Gain | Heat gain | Look | Multiplier on the heat before exposure. |
| `ramp` | Ramp | Colour ramp | Look | The heat ramp: pos 0 = the cold outer edge, pos 1 = the hottest core; each stop's alpha is its OPACITY CEILING. Interpolated in linear light through a 1024-entry table. |
| `sootRamp` | Soot Ramp | Soot ramp | Look | The second ramp the gas crosses into as its soot tint rises (gout / sputter: greasy soot; whip: a HOT orange — the head of the swung stream). Empty = no crossfade. |
| `sootLo` | Soot Lo | Soot crossfade start | Look | Tint (T/H) at which the crossfade into the second ramp starts — a WIDTH with Soot Hi, not a threshold. |
| `sootHi` | Soot Hi | Soot crossfade end | Look | Tint at which the crossfade is complete. |
