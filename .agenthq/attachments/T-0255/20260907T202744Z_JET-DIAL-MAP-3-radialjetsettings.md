# DIAL-MAP — Radial Jet's own dials, RadialJetSettings (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `bias` | Bias | Angle bias | Arc shape | Angle-distribution power across the arc: 1 = uniform (a disc is actually filled); > 1 biases towards the aim (the base jet's fixed 1.7 gives a beam a spine). Wide arcs (Spread ≥ 60°) also draw their base angles STRATIFIED and permuted, so a thin corona has no bald patch and no rotating arm. |
| `srcR` | Src R | Burner ring radius | Arc shape | Birth radius, canvas WIDTHS of the source frame: > 0 = the gas leaves a burner RING rather than a point, and the middle stays dark. |
| `swirl` | Swirl | Swirl (fire whirl) | Arc shape | Degrees a puff is carried AROUND the source over its life — its track becomes a spiral and it is stretched along that track (a fire whirl). Applied at the puff's age, not to its birth angle. |
| `spin` | Spin | Pattern spin | Arc shape | Whole turns per loop the emission pattern rotates (integer, so the loop stays exact); a puff carries the aim it was born under, so a spinning source trails spiral arms. |
| `lobes` | Lobes | Tongue count | Arc shape | Gather the arc into N tongues with real gaps between them (the birth angles are REDISTRIBUTED by φ − depth·sin φ, never resampled, so the sheet keeps its gas). 0 = an even sheet. |
| `lobeDepth` | Lobe Depth | Tongue clumping | Arc shape | How hard the tongues clump, 0..0.95 (the remap stays crossing-free below 1). |
| `lobeKick` | Lobe Kick | Tongue length kick | Arc shape | Extra travel on a lobe axis against between them (speed × (1 − kick·(1 − cos-window))), so the tongues have length as well as density. |
| `rootK` | Root K | Root lump count | Arc shape (advanced) | With Src R > 0: the root is this many lumps laid round the birth circle (each stretched along the tangent); 0 = one lump at the centre. |
| `ringFlat` | Ring Flat | Rings face-on | Arc shape | Rings seen FACE ON — an expanding circle in the picture plane with its puffs elongated along the ring (a radial source's shockwave) instead of the base jet's edge-on flattened O. |
| `warpSpin` | Warp Spin | Turbulence spin | Arc shape (advanced) | Whole turns per loop the polar noise texture rotates around the source (integer, so the loop stays exact). |
