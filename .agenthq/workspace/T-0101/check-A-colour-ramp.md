# T-0101 Check A - colour-ramp responsiveness

Same spec, same frame, only the named gradient changed (stock fire ramp -> a blue/cyan ramp).
`repainted` = share of the generator's OWN visible pixels that changed by >2 in any channel.
That normalisation matters: Fire covers under 1% of the canvas, so a raw canvas-wide
percentage makes a total repaint look like nothing happened.

| Generator | Field edited | repainted | of canvas | max channel delta | frame | verdict |
|---|---|---|---|---|---|---|
| Fire | `layer.shapeFill.gradient` | 102.8% | 0.79% | 243 | 11 | **responds** |
| Fireball | `layer.shapeFill.gradient` | 100.8% | 7.94% | 243 | 13 | **responds** |
| Text | `layer.shapeFill.gradient` | 0.0% | 0.00% | 0 | 6 | **NO EFFECT** |
| Text | `layer.textFill.gradient` | 6.5% | 0.48% | 226 | 6 | **partial** - only some of its pixels |
| Text | `layer.textBorder.gradient` | 93.5% | 6.83% | 243 | 6 | **responds** |
| Disc (control) | `layer.shapeFill.gradient` | 103.5% | 16.58% | 233 | 6 | **responds** |
| Gem (control) | `layer.shapeFill.gradient` | 47.8% | 8.29% | 172 | 6 | **responds** |
| Torch form (control) | `layer.shapeFill.gradient` | 0.0% | 0.00% | 0 | 2 | **NO EFFECT** |
| Inferno form (control) | `layer.shapeFill.gradient` | 100.2% | 46.88% | 241 | 4 | **responds** |

**Why some rows read over 100%.** The denominator is the count of pixels visible in either render (alpha > 8). The numerator counts every pixel that changed by more than 2 in any channel, including faint pixels at the anti-aliased edge whose alpha sits at or below that threshold in both renders. Anything at or above ~100% therefore means "the whole shape was repainted", not more than all of it.

**Reading of the results.**

- **Fire and Fireball respond.** The prior source-only reading that editing a colour ramp on Fire or Fireball does nothing is **wrong**, and the images `frames/checkA_Fire_layer-shapeFill-gradient_A.png` vs `..._B.png` (and the Fireball pair beside them) show a completely recoloured flame. The earlier claim looked plausible only because Fire covers under 1% of a 96x96 canvas at stock defaults, so a canvas-wide difference metric reports a total repaint as 0.79%.
- **Text is the real case, and it is worse than "the ramp does nothing".** `layer.shapeFill` — the field a layer's ordinary Colour row edits — has **literally zero** effect on a Text layer. The colour that Text actually shows comes overwhelmingly from `layer.textBorder` (93.5% of its pixels), with `layer.textFill` reaching only 6.5%. So on Text the fill and the border have swapped importance relative to every other generator: the outline is the picture and the face is the trim.
- **Plug-in forms split cleanly.** Torch ignores `shapeFill` entirely (0.0%, `UsesFill => false`, it carries its own baked palette); Inferno honours it completely (100.2%, `UsesFill` left at the default). That is the visible consequence of the `UsesFill` flag, confirmed rather than assumed.
