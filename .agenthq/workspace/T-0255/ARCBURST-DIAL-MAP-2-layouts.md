# DIAL-MAP — Arc Burst, per-layout fields (10 settings classes; same field name = same concept across layouts) (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `whips` | Whips | Whip count | Shape | Whips lashing off the body (source: 13). |
| `curl` | Curl | Whip sweep | Shape | How far a whip's tip sweeps sideways over the clip, radians (each whip picks ±this once; source 0.75). |
| `reachLo` | Reach Lo | Min reach | Shape | Shortest root reach (source 0.78). |
| `reachHi` | Reach Hi | Longest reach | Shape | Longest root reach (source 1.0). |
| `widthLo` | Width Lo | Min width | Shape | Thinnest lobe width multiplier (source 0.72). |
| `widthHi` | Width Hi | Fattest width | Shape | Fattest lobe width multiplier (source 1.22). |
| `tilt` | Tilt | Tilt amount | Shape | Random tilt of the whole pinch, radians (±this; source 0.22). |
| `axisRatio` | Axis Ratio | Ring squash | Shape | Ring squash: its radius along the axis as a share of its radius across (source 0.40). |
| `outer` | Outer | Rim nodes | Shape | Rim nodes (source 12). |
| `inner` | Inner | Inner nodes | Shape | Interior nodes (source 3). |
| `nodes` | Nodes | Node count | Shape | Nodes round the hub (source 9). |
| `ribs` | Ribs | Rib count | Shape | Ribs — partial great circles (source 9). |
| `shards` | Shards | Shard count | Shape | Shards (source 24). |
| `roots` | Roots | Root trees | Shape | Root trees (source 9). |
| `trunks` | Trunks | Trunk count | Shape | Trunks (source 5 — at 128 px a branching trunk needs room to branch). |
| `lobes` | Lobes | Lobe count | Shape | Lobes (source 12; the core's gear teeth follow the count). |
| `spanLo` | Span Lo | Shortest span | Shape | Shortest rib, radians of its great circle (source 1.8). |
| `spanHi` | Span Hi | Longest span | Shape | Longest rib (source 4.0). |
| `ring` | Ring | Ring arcs | Shape | Arcs in the equatorial ring (source 14). |
| `ringSpan` | Ring Span | Ring arc span | Shape | Span of each ring arc, radians (source 0.52). |
| `snapLo` | Snap Lo | Earliest snap | Shape | Earliest rib snap (source 0.40). |
| `snapHi` | Snap Hi | Latest snap | Shape | Latest rib snap (source 0.74). |
| `speedLo` | Speed Lo | Slowest speed | Shape | Slowest shard (source 0.60). |
| `speedHi` | Speed Hi | Fastest speed | Shape | Fastest shard (source 1.18). |
| `lenLo` | Len Lo | Min length | Shape | Shortest shard length multiplier (source 0.55). |
| `lenHi` | Len Hi | Max length | Shape | Longest shard length multiplier (source 1.55). |
| `chords` | Chords | Chord count | Shape | Chords across the rim (source 7). |
| `spurs` | Spurs | Spur count | Shape | Spurs shot outward off the shell by the end (5 at the start + this; source 9). |
| `breakouts` | Breakouts | Break arcs | Shape | Breakout arcs discharging THROUGH the surface by the end (source 15). |
| `sparks` | Sparks | Spark count | Shape | Sparks at the advancing tips at the start (source 20, halving by the end). |
| `arcs` | Arcs | Arc count | Shape | Early branching discharges while the body lives (source 6). |
| `rough` | Rough | Roughness | Detail & texture (advanced) | Capillary roughness (source 0.24). |
| `detail` | Detail | Path detail | Detail & texture (advanced) | Midpoint-displacement levels per path (source 5). |
| `jitter` | Jitter | Jitter amount | Detail & texture | Jitter amplitude of a rib at the start, px (grows with t and with snapping; source 3.8). |
| `forkP` | Fork P | Fork chance | Detail & texture | Base chance a whip throws a fork (rises by 0.3 over the clip; source 0.5). |
| `crawlers` | Crawlers | Crawler count | Detail & texture | Crawlers — short arcs skating over the body's skin at the start (source 7, thinning with t). |
| `hairP` | Hair P | Hair chance | Detail & texture | Chance a live shard throws a hair (source 0.22, falling with age). |
| `crossP` | Cross P | Cross-talk | Detail & texture | Chance of an arc jumping to the next shard round the ring, mid-clip (source 0.32). |
| `veilP` | Veil P | Veil chance | Detail & texture | Chance a path gets a translucent stretch (source 0.45). |
| `jetSpread` | Jet Spread | Tip scatter | Detail & texture | Sideways scatter of a jet's tip, px σ (source 3.0). |
| `ghostP` | Ghost P | Ghost share | Ghosts | Share of roots that are translucent (source 0.52). |
| `ghostLo` | Ghost Lo | Ghost min | Ghosts | Faintest ghost root (source 0.16). |
| `ghostHi` | Ghost Hi | Ghost max | Ghosts | Most opaque ghost root (source 0.48). |
| `ghostDeepP` | Ghost Deep P | Deep ghost | Ghosts | Share of ghosts that are deep (source 0.18). |
| `ghostSpan` | Ghost Span | Ghost range | Ghosts | Random span added to the ghost floor (source 0.16 → ghosts at 18–34 %). |
| `ghostFloor` | Ghost Floor | Interior min | Ghosts | Opacity of the deepest interior edge (source 0.12; nearer edges climb toward 1 as depth^1.4). |
| `rimFloor` | Rim Floor | Rim floor | Ghosts | Opacity floor of the RIM (source 0.30 — at 0.12 the back of the ring stopped being a ring). |
| `backOpa` | Back Opa | Far alpha | Ghosts | Opacity of the far side of the sphere (source 0.08). |
| `depthGamma` | Depth Gamma | Depth shape | Ghosts (advanced) | Gamma on the front-to-back opacity (source 1.5: most of the back hemisphere is a faint trace). |
| `ballOpa` | Ball Opa | Ball opacity | Ghosts | Opacity of the ball behind the ribs (source 0.42). |
| `lensOpa` | Lens Opa | Lens opacity | Ghosts | Opacity of the lens at the start (source 0.66, falling to 0.04). |
| `branchMin` | Branch Min | Min branches | Branching | Fewest branches per path (source 2). |
| `branchMax` | Branch Max | Most branches | Branching | Most branches per path (source 3). |
| `branchScale` | Branch Scale | Branch length | Branching | Branch length share (source 0.44). |
| `branchSpread` | Branch Spread | Branch angle | Branching | Widest branch angle, radians (source 1.25). |
| `branchAmp` | Branch Amp | Branch energy | Branching | Energy and width a generation keeps (source 0.72 — the trunk is barely thicker than its twigs). |
| `depth` | Depth | Branch gens | Branching | Branch generations (source 4 — capillary density is the point). |
| `dissolveStart` | Dissolve Start | Fade start | Timing | Where the global alpha fade begins (source 0.66). |
| `coolStart` | Cool Start | Cool start | Timing | Where the energy starts cooling (source 0.86). |
| `dieLo` | Die Lo | Die-off lo | Timing | Earliest moment an arc starts fading out on its own (source 0.54). |
| `dieHi` | Die Hi | Die-off hi | Timing | Latest moment an arc starts fading (above 1 = it outlives the clip; source 1.12). |
| `blowLo` | Blow Lo | Blow-out lo | Timing | Earliest moment a trunk's blow-out gap starts travelling from the root (source 0.42). |
| `blowHi` | Blow Hi | Blow-out hi | Timing | Latest blow-out start (source 0.68 — each trunk fails at its own moment). |
| `swapStart` | Swap Start | Ghost swap lo | Timing | When the ghost set starts crossfading to the other half (source 0.30). |
| `swapEnd` | Swap End | Ghost swap hi | Timing | When the crossfade completes (source 0.72 — slow enough that no frame is the moment). |
| `dissolveK` | Dissolve K | Fade shape | Timing (advanced) | Exponent of that fade (source 1.15). |
| `burstStart` | Burst Start | Net breaks at | Timing | When the net lets go and the nodes fly (source 0.60). |
| `flyLo` | Fly Lo | Escape lo | Timing | Slowest escaping node (source 0.7). |
| `flyHi` | Fly Hi | Escape hi | Timing | Fastest escaping node (source 1.25). |
| `fireLo` | Fire Lo | Strike lo | Timing | Earliest strike moment (source 0.01). |
| `fireHi` | Fire Hi | Latest strike | Timing | Latest strike moment (source 0.20 — compressed so the silhouette is there by frame 4). |
| `sweepStart` | Sweep Start | Sweep from | Timing | When the transparent sector starts eating the wheel (source 0.40). |
| `breakLo` | Break Lo | Break lo | Timing | Earliest moment a shard breaks off (source 0.02). |
| `breakHi` | Break Hi | Break hi | Timing | Latest break-off (source 0.34). |
| `ringCoolStart` | Ring Cool Start | Ring cool | Timing | Where the RING's energy starts cooling (source 0.92 — it outlives the jets). |
| `hollowStart` | Hollow Start | Hollow start | Timing | When the transparency front starts growing from the centre (source 0.34). |
| `coolK` | Cool K | Cooling shape | Timing (advanced) | Exponent of that cooling (source 1.4). |
| `bodyAmp` | Body Amp | Body glow | Body & energy | Peak energy of the body (source 1.9). |
| `bodyOpa` | Body Opa | Body opacity | Body & energy | Opacity of the body at the start — it is a body of light you see the far whips THROUGH (source 0.88, falling to 0.46). |
| `lobeAmp` | Lobe Amp | Lobe glow | Body & energy | Lobe energy — kept under the pale band's 0.58 threshold so petals are COLOURED and only the filament is white (source 0.54). |
| `crackleP` | Crackle P | Crackle | Body & energy | Chance of a tooth-to-tooth crackle per lobe while the core has charge (source 0.55). |
| `flashAmp` | Flash Amp | Flash energy | Body & energy | Energy of the flash the trunks are rooted in, gone by frame 7 (source 1.95). |
| `fadeAmt` | Fade Amt | Fade amount | Body & energy | How much the whole web fades evenly over the back half, outside the hole (source 0.72). |
| `shutterStart` | Shutter Start | Shutter open | Body & energy | When the transparent shutter sector starts opening (source 0.34 — a shockwave must close before it comes apart). |
| `shutterDepth` | Shutter Depth | Shutter alpha | Body & energy | How transparent the shutter sector gets (source 0.76). |
| `sweepDepth` | Sweep Depth | Sweep alpha | Body & energy | How transparent the eaten side gets (source 0.96). |
