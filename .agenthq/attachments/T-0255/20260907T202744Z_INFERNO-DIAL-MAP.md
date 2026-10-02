# DIAL-MAP — Inferno (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `progress` | Progress | Progress | Clock | The explosion's progress over the layer's life — a TIME REMAP as one envelope. The default straight line plays in real time; bend it to snap in and hold at full bloom, slow the smoky tail, or play sections at different speeds. A Static value freezes the explosion at that moment as a pose. |
| `mutation` | Mutation | Per-blast variation | Multiple blasts |  |
| `heatStacking` | Heat Stacking | Overlap heat stacking | Multiple blasts |  |
| `characterDrift` | Character Drift | Fire/smoke drift across blasts | Multiple blasts |  |
| `characterJitter` | Character Jitter | Fire/smoke jitter across blasts | Multiple blasts |  |
| `blastSize` | Blast Size | Blast size | Blast | Final occupied radius inside the safe zone, over the layer's life. |
| `flash` | Flash | Ignition flash | Blast | White-hot ignition flash and central punch at each blast's birth, over the layer's life. Tinted from the Fill's hot end. |
| `bangSpeed` | Bang Speed | Bang speed | Blast | How abruptly the first expansion happens — 1 is a violent snap (frames, not a bloom). |
| `punch` | Punch | Punch (drama) | Blast | DRAMA, opt-in: the bang overshoots its radius and settles back, ignition spikes the heat white-hot, and the flash blows out bigger. 0 = the calm prototype look. |
| `flashReach` | Flash Reach | Flash reach | Blast | How FAR the flash reaches out from the blast centre — its size, set independently of how bright it is. |
| `flashSoftness` | Flash Softness | Flash softness | Blast | How gradually the flash's alpha fades out into the cloud: 0 = a tight core with a crisp edge, 1 = a broad soft glow. (WHEN it fades is the Flash envelope's job.) |
| `recoil` | Recoil | Recoil | Blast | Pulls the outer shape back in after the blast. |
| `edgeSoftness` | Edge Softness | Edge softness | Containment | How far the cloud's own rim fades out, in absolute canvas terms — so the fade looks the same whether the blast is tiny or huge. Low reads as a hard-edged solid; raise it for gas. |
| `safeMargin` | Safe Margin | Safe margin | Containment | Minimum empty border around the effect, as a fraction of the canvas — nothing is drawn past it, so the effect can never touch the frame edge. |
| `frameFade` | Frame Fade | Frame-edge fade | Containment | How wide the fade-to-nothing is as the cloud nears the Safe margin — anything close to the border dissolves instead of being cut. The band grows inward, so the frame edge itself is always fully transparent. |
| `clumps` | Clumps | Clump count | Cloud shape | Large coherent lobes, merged into one cloud. |
| `clumpSpread` | Clump Spread | Clump spread | Cloud shape | Separates the hot lobes without breaking cohesion, over the layer's life. |
| `billow` | Billow | Billow strength | Cloud shape | Strength of the rolling, 3D-looking cloud pockets, over the layer's life — what keeps the cloud from reading as a flat slab. |
| `jagged` | Jagged | Jaggedness | Cloud shape | Breaks the perfect circle into torn explosive lobes, over the layer's life. |
| `coreDensity` | Core Density | Core density | Cloud shape | Keeps the MIDDLE of the cloud thick over the layer's life. The cavity and noise detail bite hardest where the cloud is thickest, which thins (or holes) the centre without this. |
| `cohesion` | Cohesion | Cohesion | Cloud shape | Higher keeps all clumps visibly connected as one mass, over the layer's life — fall to a low value late and the cloud visibly blows apart into fragments. |
| `hollow` | Hollow | Hollow core | Hollow core | Carves the cloud's core out into a cavity over the layer's life, leaving a burning shell — 0 = solid, high = a ring/torus of fire. Animate it to make the cloud bloom open into a ring. |
| `hollowRim` | Hollow Rim | Inner rim heat | Hollow core | Heat concentrated on the cavity's INNER boundary over the layer's life, so the shell visibly burns. Only acts once Hollow is raised. |
| `outerRim` | Outer Rim | Outer rim heat | Hollow core | Heat concentrated on the cloud's OUTER rim over the layer's life — a burning surface instead of an evenly lit disc. Animate it to have the shell ignite and cool. |
| `churn` | Churn | Churn | Churn & motion | Rolling internal displacement, over the layer's life. |
| `rotation` | Rotation | Rotation torque | Churn & motion | Rotational torque, either way (−1..1), over the layer's life; 0 = none. |
| `pulse` | Pulse | Secondary pulse | Churn & motion | A secondary compression wave that breathes the SAME blast in and out after the bang — it does not add a second explosion (use more swarm particles for that). |
| `fire` | Fire | Fire amount | Fire | How much of the cloud is FLAME, over the layer's life: 0 = no fire at all (pure smoke); 1 = fire fills most of the dense regions. |
| `heatPockets` | Heat Pockets | Heat pockets | Fire | Varies the heat WITHIN the fire — internal boiling regions. (Clumps shape the cloud's mass; this only changes how hot each part of it burns.) |
| `cooling` | Cooling | Cooling rate | Fire | How quickly flame turns into dark smoke over each blast's own life — the fire→smoke rate. |
| `coreGlow` | Core Glow | Core glow | Fire | An inner glow at the cloud's core over the layer's life, added after Cooling so it survives it — the smoulder left inside the smoke. Its timing is entirely this envelope's: flat glows throughout, a curve swells and dies exactly when you draw it. |
| `smoke` | Smoke | Smoke amount | Smoke | How much SOOT there is, over the layer's life — the one dial for the amount of smoke. |
| `smokeSpread` | Smoke Spread | Smoke spread | Smoke | How far the soot reaches BEYOND the fire, over the layer's life — the shell of smoke that frames the flame and feathers into the background instead of stopping at its silhouette. |
| `darkness` | Darkness | Smoke darkness | Smoke | Heavier, darker soot over the layer's life. SMOKE ONLY — burning pixels take the Fill ramp's colour outright, so this never tints the flame. |
| `linger` | Linger | Smoke lingers | Smoke | How long the smoke STAYS: 0 = fades out over the last frames; 1 = persists to the very end of the timeline. The shared Alpha envelope above also fades the tail by default — flatten it for smoke that holds to the last frame. |
| `body` | Body | Body opacity | Smoke | How OPAQUE the thick of the cloud reads, over the layer's life: 0 = ghostly gas, 1 = dense fire and smoke read as solid matter. Affects flame and soot alike. |
| `dieOut` | Die Out | Dies out early | Finish | Dissolves the whole effect to nothing over the final fraction of the timeline, so it ends on its own instead of running until the last frame cuts it off. 0 = no forced ending. |
| `embers` | Embers | Embers | Finish | Short contained sparks that arc out and fade before the border. |
| `lighting` | Lighting | Pseudo-3D lighting | Finish | Pseudo-3D shading from the cloud's own density — carves lit billows and shadowed pockets. |
| `contrast` | Contrast | Contrast | Finish | Separates hot cavities from dark billows on the heat ramp. |
