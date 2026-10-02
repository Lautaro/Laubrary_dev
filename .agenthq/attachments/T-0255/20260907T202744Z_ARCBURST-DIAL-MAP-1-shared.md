# DIAL-MAP — Arc Burst, shared fields (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `layout` | Layout | Arc pattern |  | Which of the ten source draws this is. Core: a translucent plasma lump with whips. Weave: a shockwave ring breaking into dashes under a rotating transparent shutter. Bolt: five huge branching trunks with a see-through gap travelling root to tip. Crown: twelve fat petals round a gear-toothed heart, ghost sets crossfading. Lattice: a hubless node net woven by depth. Terminal: a hub striking nine nodes, dying one side first. Cage: arcs over a sphere, back faces transparent. Stipple: a flash decomposing into shards then dots. Pinch: two axial jets through a translucent equatorial ring. Lichten: capillary creep hollowing from the inside. |
| `aref` | Aref | Fade-in energy | Alpha window | Energy at which a stroke's alpha reaches 1 (alpha = clip(energy / Aref)^Agamma × opacity, set at deposition). Lower = every stroke more opaque. bolt: 0.28. |
| `agamma` | Agamma | Alpha gamma | Alpha window | Gamma on the alpha ramp (below 1 lifts the faint sheath). bolt: 0.70. |
| `bloomRadius` | Bloom Radius | Bloom radius | Bloom | Radius of the energy bloom's box blur (3 passes ≈ Gaussian), in source pixels at the 128 px frame — scales with the canvas. 0 = no bloom. bolt: 3.0. |
| `bloomStrength` | Bloom Strength | Bloom strength | Bloom | How much blurred energy is ADDED back (overlapping arcs glow here, not at deposition). bolt: 0.48. |
| `bloomAlpha` | Bloom Alpha | Bloom colour reach | Bloom | The ALPHA plane's bloom as a share of the energy bloom, blurred over 0.8× the radius — colour must spread further than opacity or the glow reads as grey smoke. Source: 0.34. |
| `widthScale` | Width Scale | Stroke width | Stroke look | Multiplier on every stroke's core and sheath width (1 = the source's pixels). |
| `ampScale` | Amp Scale | Stroke energy | Stroke look | Multiplier on every stroke's and body's energy (1 = the source). Energy decides the colour band, so above 1 more of the figure goes white. |
| `keepHueFloor` | Keep Hue Floor | Ghost colour floor | Stroke look | keep_hue floor: a ghost arm's energy is multiplied by floor + (1 − floor) × its opacity, so a translucent core lands in the saturated band instead of rendering as grey string. Source: 0.36. |
| `ghostDeepLo` | Ghost Deep Lo | Deep ghost opacity (low) | Stroke look | Opacity range of the DEEP ghosts (the fifth of translucent arms that are barely there). Source: 0.05..0.13. The low end. |
| `ghostDeepHi` | Ghost Deep Hi | Deep ghost opacity (high) | Stroke look | The high end of the deep-ghost opacity range. Source: 0.13. |
| `rerollPerFrame` | Reroll Per Frame | Re-strike every frame | Stroke look | On (the source): the bolt geometry is rolled afresh every frame from random.Random(seed·m + frame), so lightning re-strikes. Off: the per-frame stream is held at frame 0 and a bolt grows along the clock instead of flickering. |
| `palette` | Palette | Colour bands | Colour | The cel palette: a free gradient sampled into N hard bands (below the first band's start, the pixel is absent — the form's own energy floor). Edit the ramp freely; the Bands count only changes the sampling resolution, so it never loses a colour you've picked. Presets: ArcBands.Ion / Violet / Acid / Plasma / Cyan / Magenta / Chroma / Steel / Crimson / Teal. |
| `swarmSize` | Swarm Size | Swarm burst size | Swarm | Size of each swarm particle's burst as a fraction of the solo burst (the swarm's own size/depth shading multiplies it). |
