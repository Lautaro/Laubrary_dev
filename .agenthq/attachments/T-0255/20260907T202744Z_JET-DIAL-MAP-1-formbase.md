# DIAL-MAP — Jet family shared base, JetFormBase (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `anchorX` | Anchor X | Nozzle across frame | Placement & size | Where the nozzle (or, for a radial jet, the centre) sits across the canvas, as a fraction of the width. |
| `anchorY` | Anchor Y | Nozzle down frame | Placement & size | Where the nozzle (or centre) sits down the canvas, as a fraction of the height from the TOP (the source's y-down frame: a positive Aim points down). |
| `scale` | Scale | Jet scale | Placement & size | Scale of the jet: the variant's source frame width as a fraction of the canvas width; every length inside the variant scales with it (1 = the source frame spans the canvas). |
| `swarmSize` | Swarm Size | Swarm jet size | Placement & size | Scale of each swarm particle's jet as a fraction of the solo Scale (the swarm's own size / depth shading multiplies it). |
