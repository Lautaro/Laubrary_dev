# ORB-DIAL-MAP — T-0235

Every dial the Orb generator declares: the label it showed before, the label it shows now, the group it lands in, and what the dial visibly does. **No serialized field name changes** — this is display names, groups and (where the old text was jargon) tooltips only.

Old labels are what `ObjectNames.NicifyVariableName` produced from the serialized name, which is why they read as "A 0", "Turb Oct Shell", "Rip R".

Sources read end to end: `Assets/Packages/Laubrary/Runtime/Pyre/Forms/Kiln/OrbForm.cs` (dials + PyreForm glue) and `Assets/Packages/Laubrary/Runtime/Pyre/Forms/Kiln/PyreOrb.cs` (the five programs). The group structure is not invented: `PyreOrb.cs:11-17` lists each variant's own components (`wake_turbulence, smear_wake, core_boiling, embers, glow, soften, rasterise` …) and the groups below are those components.

## How grouping works

Two new ZUI attributes, general to every reflected card in Laubrary (not Orb-specific):

- `[ZUILabel("Core size")]` — the display name, when the serialized name cannot be renamed.
- `[ZUIGroup("Placement & size", Tooltip = "…")]` — the box the dial lands in. `Advanced = true` sends the box to the end of the card, folded on first sight.

A group's box is created where its **first** member is declared, so declaration order still decides where a group appears. Fields carrying no group flow inline exactly as they do today, so every other reflected card in Laubrary is untouched.

---

## Orb — shared dials (`OrbForm`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `variant` | Variant | Orb type | *(none — top of card)* | Which of the five programs draws. Each has its own settings box. |
| `radius` | Radius | Core size | Placement & size | The ball's radius as a fraction of frame width; every length in the variant scales with it. |
| `noseX` | Nose X | Nose X | Placement & size | Where the ball sits left↔right inside its own frame; the trail fills the space to its left. |
| `axisY` | Axis Y | Travel line | Placement & size | Which height down the frame the whole thing is built on. |
| `wake` | Wake | Trail length | Placement & size | How far behind the ball the trail/shells/tail reach, in core radii. |
| `swarmSize` | Swarm Size | Swarm size | Placement & size | Each swarm particle's orb as a fraction of Core size. |
| `emberdrift` … `voltcore` | Emberdrift … Voltcore | *(unchanged)* | *(none — the variant's own box)* | Only the active variant's box is shown. |
| `softenPasses` | Soften Passes | Blur passes | Cleanup **(advanced)** | Blurs the summed energy before colouring; 1 fuses the seams between shapes, 2 softens the head away. |
| `floor` | Floor | Hide alpha | Cleanup **(advanced)** | Pixels fainter than this are dropped outright. |
| `despeckle` | Despeckle | De-speck | Cleanup **(advanced)** | Drops faint lone pixels — the sampled edge of a falloff, not artwork. |
| `despeckleBelow` | Despeckle Below | Speck alpha | Cleanup **(advanced)** | How faint a lone pixel has to be before it counts as a speck. |

## Every variant — shared look dials (`StyleSettings`)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `ramp` | Ramp | Colour ramp | Colour | The whole colour scheme: left = faintest energy (halo, trail), right = white-hot centre. |
| `gain` | Gain | Brightness | Colour | Pushes the whole picture further up the ramp — the one dial that rescales everything at once. |
| `a0` | A 0 | Fade-in point | Opacity | How dim a pixel can be and still show at all. |
| `a1` | A 1 | Solid point | Opacity | **The hardness dial.** Near 1 the orb reads as fog with a bright patch; lower and it becomes a solid nucleus with a gradient rim. |
| `acurve` | Acurve | Fade shape | Opacity | Below 1 puts a visible edge on the head without drawing a line. |
| `amax` | Amax | Max alpha | Opacity | Ceiling — below 1 nothing is ever fully opaque (Membrane's whole subject). |
| `noseSquash` | Nose Squash | Front flatten | Body & halo | Squashes the leading half of the ball, so it is blunter in front than behind. |
| `glowWide` | Glow Wide | Halo width | Body & halo | How far the light the ball sits in reaches, in core radii. |
| `glowTail` | Glow Tail | Halo trail | Body & halo | How much of that halo is dragged out behind the ball. |
| `glowAmp` | Glow Amp | Halo glow | Body & halo | How strong the halo is against the ball. |

## Emberdrift — boiling body, torn smeared wake, embers

| serialized | old label | new label | group |
|---|---|---|---|
| `turbScale` | Turb Scale | Tear size | Wake |
| `turbW` | Turb W | Tear strength | Wake |
| `turbScale2` | Turb Scale 2 | Tear size | Wake |
| `turbW2` | Turb W 2 | Tear amount | Wake |
| `smearTaps` | Smear Taps | Wake smear | Wake |
| `smearDecay` | Smear Decay | Wake falloff | Wake |
| `smearNorm` | Smear Norm | Motion blur | Wake |
| `turbScaleBody` | Turb Scale Body | Boil size | Body |
| `coreWarp` | Core Warp | Boil depth | Body |
| `coreAmp` | Core Amp | Core glow | Body |
| `coreP` | Core P | Core softness | Body |
| `emitPeriod` | Emit Period | Burst every | Embers |
| `emitLife` | Emit Life | Ember life | Embers |
| `embersPerPiece` | Embers Per Piece | Ember count | Embers |
| `emberAmp` | Ember Amp | Ember glow | Embers |
| `emberFadeP` | Ember Fade P | Ember fade | Embers |
| `emberElong` | Ember Elong | Ember streak | Embers |
| `turbOct` | Turb Oct | Tear detail | Noise detail **(advanced)** |
| `turbAniso` | Turb Aniso | Tear stretch | Noise detail **(advanced)** |
| `turbSeedOff` | Turb Seed Off | Tear seed | Noise detail **(advanced)** |
| `turbOct2` | Turb Oct 2 | Tear detail | Noise detail **(advanced)** |
| `turbAniso2` | Turb Aniso 2 | Tear stretch | Noise detail **(advanced)** |
| `turbSeedOff2` | Turb Seed Off 2 | Tear seed | Noise detail **(advanced)** |
| `turbOctBody` | Turb Oct Body | Boil detail | Noise detail **(advanced)** |
| `turbAnisoBody` | Turb Aniso Body | Boil stretch | Noise detail **(advanced)** |
| `turbSeedOffBody` | Turb Seed Off Body | Boil seed | Noise detail **(advanced)** |

Effects: *Tear size* sets how big the torn tongues in the wake are; *Tear strength* how much of the wake is torn at all; the *Fine* pair is a second, smaller texture mixed in so the wake cannot comb into stripes. *Wake smear* is the exposure streak that makes a still frame read as moving — the ball is deliberately not smeared, which is what gives it the crisp-nose / torn-tail asymmetry. *Boil* dials warp the ball's own radius, so the whole body boils rather than growing a noisy outline.

## Wisp — nucleus in a cloud, snaking persistence trail

| serialized | old label | new label | group |
|---|---|---|---|
| `tubeStamps` | Tube Stamps | Trail smooth | Trail |
| `tubeP` | Tube P | Trail edge | Trail |
| `tubeXstretch` | Tube Xstretch | Trail stretch | Trail |
| `smearTaps` | Smear Taps | Trail smear | Trail |
| `smearDecay` | Smear Decay | Trail falloff | Trail |
| `smearNorm` | Smear Norm | Motion blur | Trail |
| `smearPostScale` | Smear Post Scale | Trail glow | Trail |
| `cloudAmp` | Cloud Amp | Cloud glow | Head |
| `cloudP` | Cloud P | Cloud soft | Head |
| `nucleusAmp` | Nucleus Amp | Core glow | Head |
| `nucleusFlat` | Nucleus Flat | Core flat top | Head |
| `nucleusP` | Nucleus P | Core softness | Head |
| `nucleus2Amp` | Nucleus 2 Amp | Core glow 2 | Head |
| `veilAmp` | Veil Amp | Veil strength | Veils |
| `turbScaleVeil` | Turb Scale Veil | Veil size | Veils |
| `turbOctVeil` | Turb Oct Veil | Veil detail | Noise detail **(advanced)** |
| `turbAnisoVeil` | Turb Aniso Veil | Veil stretch | Noise detail **(advanced)** |
| `turbSeedOffVeil` | Turb Seed Off Veil | Veil seed | Noise detail **(advanced)** |

## Coronal — granulated star, prominences bent back into a fan

| serialized | old label | new label | group |
|---|---|---|---|
| `prominences` | Prominences | Flare count | Prominences |
| `prominenceSegments` | Prominence Segments | Flare length | Prominences |
| `prominenceAmp` | Prominence Amp | Flare glow | Prominences |
| `prominenceP` | Prominence P | Flare soft | Prominences |
| `smearTaps` | Smear Taps | Flare smear | Prominences |
| `smearDecay` | Smear Decay | Flare falloff | Prominences |
| `smearNorm` | Smear Norm | Motion blur | Prominences |
| `coreAmp` | Core Amp | Star glow | Star body |
| `coreP` | Core P | Star softness | Star body |
| `coreGranulation` | Core Granulation | Granule depth | Star body |
| `turbScaleBody` | Turb Scale Body | Granule size | Star body |
| `limbAmp` | Limb Amp | Rim glow | Rim |
| `limbR` | Limb R | Rim position | Rim |
| `limbW` | Limb W | Rim thickness | Rim |
| `limbP` | Limb P | Rim softness | Rim |
| `turbOctBody` | Turb Oct Body | Granule det. | Noise detail **(advanced)** |
| `turbAnisoBody` | Turb Aniso Body | Granule str. | Noise detail **(advanced)** |
| `turbSeedOffBody` | Turb Seed Off Body | Granule seed | Noise detail **(advanced)** |

## Membrane — see-through bubble shedding a dissolving veil and motes

| serialized | old label | new label | group |
|---|---|---|---|
| `emitPeriod` | Emit Period | Shell every | Shed shells |
| `emitLife` | Emit Life | Shell life | Shed shells |
| `shellAmp` | Shell Amp | Shell glow | Shed shells |
| `shellP` | Shell P | Shell soft | Shed shells |
| `turbScaleShell` | Turb Scale Shell | Break-up size | Shed shells |
| `smearTaps` | Smear Taps | Shell smear | Shed shells |
| `smearDecay` | Smear Decay | Shell falloff | Shed shells |
| `smearNorm` | Smear Norm | Motion blur | Shed shells |
| `emitPeriodMotes` | Emit Period Motes | Mote every | Motes |
| `emitLifeMotes` | Emit Life Motes | Mote lifetime | Motes |
| `motesPerPiece` | Motes Per Piece | Mote count | Motes |
| `moteAmp` | Mote Amp | Mote glow | Motes |
| `moteElong` | Mote Elong | Mote streak | Motes |
| `windowAmp` | Window Amp | Interior glow | Bubble |
| `windowP` | Window P | Interior soft | Bubble |
| `skinAmp` | Skin Amp | Skin glow | Bubble |
| `skinR` | Skin R | Skin position | Bubble |
| `skinW` | Skin W | Skin thick | Bubble |
| `skinP` | Skin P | Skin softness | Bubble |
| `ripAmp` | Rip Amp | Ripple amt. | Ripples |
| `ripOrder` | Rip Order | Ripple count | Ripples |
| `ripDepth` | Rip Depth | Ripple depth | Ripples |
| `ripR` | Rip R | Ripple pos. | Ripples |
| `ripW` | Rip W | Ripple thick | Ripples |
| `ripP` | Rip P | Ripple soft | Ripples |
| `nucleusAmp` | Nucleus Amp | Knot glow | Inner knot |
| `nucleusFlat` | Nucleus Flat | Knot flat top | Inner knot |
| `nucleusP` | Nucleus P | Knot softness | Inner knot |
| `turbOctShell` | Turb Oct Shell | Break det. | Noise detail **(advanced)** |
| `turbAnisoShell` | Turb Aniso Shell | Break str. | Noise detail **(advanced)** |
| `turbSeedOffShell` | Turb Seed Off Shell | Break-up seed | Noise detail **(advanced)** |

## Voltcore — hyper-bright bead, filaments and a plasma tail

| serialized | old label | new label | group |
|---|---|---|---|
| `emitPeriod` | Emit Period | Fil. every | Filaments |
| `emitLife` | Emit Life | Fil. life | Filaments |
| `branchesPerPiece` | Branches Per Piece | Fil. count | Filaments |
| `filamentSteps` | Filament Steps | Fil. length | Filaments |
| `filamentAmp` | Filament Amp | Fil. glow | Filaments |
| `filamentElong` | Filament Elong | Fil. streak | Filaments |
| `smearTaps` | Smear Taps | Fil. smear | Filaments |
| `smearDecay` | Smear Decay | Fil. falloff | Filaments |
| `smearNorm` | Smear Norm | Motion blur | Filaments |
| `tailStamps` | Tail Stamps | Tail smooth | Plasma tail |
| `tailP` | Tail P | Tail edge | Plasma tail |
| `tailSmearTaps` | Tail Smear Taps | Tail smear | Plasma tail |
| `tailSmearDecay` | Tail Smear Decay | Tail falloff | Plasma tail |
| `tailSmearNorm` | Tail Smear Norm | Tail m.blur | Plasma tail |
| `tailPostScale` | Tail Post Scale | Tail glow | Plasma tail |
| `envelopeAmp` | Envelope Amp | Haze glow | Haze & charge |
| `envelopeP` | Envelope P | Haze softness | Haze & charge |
| `chargeAmp` | Charge Amp | Charge amt. | Haze & charge |
| `turbScaleCharge` | Turb Scale Charge | Charge size | Haze & charge |
| `beadAmp` | Bead Amp | Bead glow | Bead & ball |
| `beadFlat` | Bead Flat | Bead flat top | Bead & ball |
| `beadP` | Bead P | Bead softness | Bead & ball |
| `bead2Amp` | Bead 2 Amp | Bead halo | Bead & ball |
| `ballAmp` | Ball Amp | Ball glow | Bead & ball |
| `ballP` | Ball P | Ball softness | Bead & ball |
| `turbOctCharge` | Turb Oct Charge | Charge detail | Noise detail **(advanced)** |
| `turbAnisoCharge` | Turb Aniso Charge | Charge str. | Noise detail **(advanced)** |
| `turbSeedOffCharge` | Turb Seed Off Charge | Charge seed | Noise detail **(advanced)** |

---

## Naming rules applied

- **`…Amp` → "… brightness".** Every amplitude in this generator feeds an additive energy field read through the tone map, so pushing one makes that part brighter and hotter up the ramp. That is what the author sees.
- **`…P` → "… softness".** A falloff exponent: above 1 the shape leaves its plateau slowly and reaches zero with no boundary anywhere (soft); below 1 it is a flat mid-tone with a hard edge. "Softness" is the visible axis; the tooltip keeps the exact mechanism.
- **`…R` / `…W` → "… position" / "… thickness".** A ring's radius and half-width as fractions of the core radius.
- **`turbScale…` → "… size", `turbOct…` → "… detail", `turbAniso…` → "… stretch", `turbSeedOff…` → "… seed"**, each prefixed by the *thing* that noise makes (tears, boil, granules, break-up, veil, charge) rather than by "turbulence".
- **`smearTaps`/`smearDecay` → "… smear length" / "… smear falloff"**, and `smearNorm` → **"Smear as motion blur"** — on means the energy is redistributed (a motion blur), off means it accumulates (a light streak that gets brighter). That on/off distinction is the visible difference and it is now what the label says.
- **`emitPeriod`/`emitLife` → "… shed every" / "… lifetime"**, named for what is being shed in that variant (ember burst, shell, mote, filament), because a card can carry two emitters (Membrane sheds shells *and* motes) and "Emit Period" twice tells the author nothing.
- **The alpha window keeps four dials but stops speaking in symbols** — `a0`/`a1`/`acurve`/`amax` become Fade-in point / Solid point / Fade shape / Maximum opacity, and they live in one box called Opacity.

## Plasma Bloom — the piece populations (`PlasmaPopulation`)

Done as well (step 4). **One** settings class draws Plasma Bloom's **Chunks, Embers and Motes** cards, so this pass fixes all three at once. Their boxes fold independently because a group's fold state is keyed by where the object sits, not by its type.

| serialized | old label | new label | group |
|---|---|---|---|
| `n` | N | Count | Population |
| `amp` | Amp | Brightness | Population |
| `rhoLo` | Rho Lo | Inner radius | Population |
| `rhoHi` | Rho Hi | Outer radius | Population |
| `clusters` | Clusters | Angle groups | Population |
| `clusterW` | Cluster W | Group width | Population |
| `size` | Size | Piece size | Piece shape |
| `grow` | Grow | Growth | Piece shape |
| `stretch` | Stretch | Birth stretch | Piece shape |
| `streak` | Streak | Fly stretch | Piece shape |
| `tilt` | Tilt | Smear tilt | Piece shape |
| `spdBase` | Spd Base | Base speed | Flight |
| `spread` | Spread | Speed spread | Flight |
| `lat` | Lat | Side drift | Flight |
| `ease` | Ease | Slowdown | Flight |
| `lin` | Lin | Travel share | Flight |
| `farFade` | Far Fade | Fastest fade | Flight |
| `swirl` | Swirl | Swirl follow | Flight |
| `spin` | Spin | Spin rate | Flight |
| `t0` | T 0 | First birth | Timing |
| `t1` | T 1 | Last birth | Timing |
| `life` | Life | Piece life | Timing |
| `ramp` | Ramp | Fade-in | Timing |
| `fade` | Fade | Fade shape | Timing |

Three tooltips referred to dials by their old captions ("between Rho lo and Rho hi", "around Spd base", "(Spd base − Spread)") and now name them by the new ones — a tooltip pointing at a label that no longer exists is worse than no tooltip. The same was done in Orb ("one orb at Nose X / Axis Y", "sized by Swarm Size", "alpha under Despeckle Below").

## Tooltips

Most of the Orb's tooltips were already written as the visible effect and are kept verbatim. The ones rewritten are those that only restated the jargon label (`"Octaves of the veil turbulence."`, `"Anisotropy of the primary wake turbulence."`, `"Nucleus falloff exponent."`, `"Normalise the shell smear."`, `"Limb falloff exponent."`, and their siblings) — they now say what moving the dial does to the picture, and every one keeps its `[Range]`, which is what puts a sensible range on the slider.
