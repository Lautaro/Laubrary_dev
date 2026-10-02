# DIAL-MAP — Torch, shared placement (T-0255)

| serialized | old label | new label | group | effect |
|---|---|---|---|---|
| `variant` | Variant | Flame type |  | Which of the five published flames this is, calm → violent. Each is its own settings box below; switching keeps the shared placement dials. |
| `axisX` | Axis X | Flame position across | Placement | Where the flame axis sits across the canvas, as a fraction of the width. |
| `ground` | Ground | Fuel bed height | Placement | Height of the fuel bed (the flame's root) above the canvas bottom, as a fraction of the canvas height. Nothing burns below it. Source: barbs' bed is 10 px up a 118 px frame. |
| `height` | Height | Flame reach | Placement | Reach of the flame (the variant's h_flame) as a fraction of the canvas height; every length inside the variant scales with it. Source: barbs reaches 74 px on its 118 px frame. |
| `swarmSize` | Swarm Size | Swarm flame size | Placement | Height of each swarm particle's flame as a fraction of the solo Height (the swarm's own size / depth shading multiplies it). |
