| Output | Compared against | Result |
|---|---|---|
| Sheet PNG cells (3) | ShaperDocumentRenderer.RenderFrame per distinct frame | 0/14592px mismatch, all 3 cells |
| ShaperClip sprites (3) | Same renderer output, cropped via sprite.rect | 0/14592px mismatch, all 3 sprites; indexing-trap guard holds |
| AnimationClip (4 keys) | PlaybackOrder beat/time/sprite | exact match, blank beat = null key |
| GIF (independent decode) | Same renderer output | 0 opacity mismatches, 0 colour-tolerance failures / 14592px |
| ShaperClip cherry replay | ShaperBaker.PlaybackOrder(doc) | exact sequence match [0,0,0,4,6,-1,-1,-1] |
| Pixels per unit | doc.pixelsPerUnit=20 | importer + every sliced sprite = 20 |
| Overwrite guard (bake x2, GIF x2) | first-pass files | versioned _1 outputs, originals untouched |
| Destination folder | document's own asset folder | matches |
