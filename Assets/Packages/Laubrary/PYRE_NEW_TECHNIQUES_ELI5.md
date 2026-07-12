# Pyre's new toys — an ELI5 explainer

Everything added/fixed to Pyre in today's session, explained from scratch. First the underlying ideas
(the "never heard of this" stuff), then what each new feature actually does with them.

---

## Part 1 — The underlying ideas

### "Noise" (as in Perlin noise / value noise)

Not TV static. Random static is *harsh* — every pixel is unrelated to its neighbor. "Noise" in graphics
means **smooth randomness** — like a random hill-and-valley landscape where nearby points are similar and
it gradually rolls from high to low. Think of clouds, marble, or terrain height-maps: random, but flowing.

The trick under the hood: scatter random values on a grid, then blend *between* grid points instead of
jumping straight from one to the next. That blending is what makes it look like rolling hills instead of
static.

### "Octaves" — mixing noise at different sizes

One layer of noise looks like big smooth blobs. If you add a *second*, smaller/finer layer of noise on top
(at higher frequency, lower strength), you get big rolling shapes with small detail riding on them — like
big ocean swells with small ripples on the surface. Most of the new noise-based effects below use two
layers ("octaves") for exactly this reason: one for the overall shape, one for texture.

### "Domain warping" — noise that distorts itself

Normally you sample noise at a fixed grid position — pixel (10, 20) always reads the same noise value.
**Domain warping** means: before you read the noise, you *nudge where you're reading it from*, using
*another* noise field. Imagine looking at a photo through wavy shower glass — the glass itself doesn't
move, but it bends the image passing through it, so what you see churns and swirls. That's exactly what
makes the new effects look like they're **actively rolling/churning** instead of just sitting there as a
static blob.

### Voronoi / "Worley" noise — cracked, cellular patterns

A completely different family of noise from the smooth kind above. Instead of blending between grid
points, you scatter **random seed points** across the image, and every pixel just asks "which seed point
is closest to me?" The result looks like cracked mud, a giraffe's spots, cells under a microscope, or
stained glass — hard-edged cells with visible seams between them, not smooth blobs. Sharp and faceted
instead of soft and rolling.

### Bayer / "ordered" dithering

Old 16-bit game consoles couldn't display smooth color gradients — too few colors available. Their trick:
instead of blending, stipple a very specific, repeating checkerboard-like dot pattern (the "Bayer matrix")
so that from a normal viewing distance your eye blends the dots into an averaged shade. It's a *fixed,
orderly* pattern — not random speckle — which is what gives old pixel art its distinctive crosshatched
shading look, different from "random specks fading out."

### Posterize / color banding

Take a smooth gradient (say, black fading to white through 256 shades) and squash it down to just 4 or 5
flat steps. The result reads as **hand-painted bands of shading** instead of a smooth computer-y gradient
— the classic cel-shaded / retro pixel-art look.

### Premultiplied vs. "straight" alpha (relevant to the bloom bug fix)

Every transparent pixel has two things: a **color** and an **opacity** (0% = invisible, 100% = solid).
There are two different conventions for *storing* that combination:
- **Straight alpha**: store the color as if it were fully solid, plus a separate opacity number. ("This
  pixel IS bright orange, and it's 30% visible.")
- **Premultiplied alpha**: store the color *already dimmed* by its own opacity. ("This pixel, once you
  account for its 30% visibility, actually contributes this dim, faded orange.")

These aren't interchangeable — mixing the two conventions up in the same calculation causes real,
visible bugs (see the Bloom fix below: exactly this mix-up made a glow effect look like a dark smudge
instead of a bright halo).

---

## Part 2 — What was actually built/fixed today

### 1. Alpha Mask gets a new "Noise" shape
The Alpha Mask modifier already had geometric wipe shapes (circle, wedge, left-right swipe...). Added a
new option: instead of a clean geometric edge, it reveals/hides using **domain-warped noise** (see above)
— giving a ragged, cloud-like edge instead of a clean line. Good for anything that should look like it's
made of billowing gas or dust rather than a solid object.

### 2. `TurbulenceModifier` — the churn/roll engine
This is the big one for "things that look alive." It **domain-warps** whatever shape it's applied to,
and the warp itself can *spin in place* and *drift* over the explosion's lifetime. Put it on a fireball
and the fire visibly churns; put it on a mushroom-cloud-shaped blast and the cap visibly rolls, the way
real mushroom clouds do.

### 3. `PosterizeModifier` — hand-painted color bands
Applies the "posterize" idea (above) to squash a shape's smooth color gradient into a handful of flat
shading bands — the single biggest lever for making a procedural explosion look hand-drawn instead of
like a smooth 3D-shader gradient.

### 4. `OrderedDitherModifier` — real retro stipple
Applies genuine Bayer-matrix dithering (above) to a shape's edge, turning a smooth transparency fade into
the actual crosshatched dot pattern classic 16/32-bit pixel art uses, instead of the random speckle the
existing "Dissolve" modifier makes.

### 5. `NoiseField` — a whole new shape type (dust cloud / gas cloud / energy field)
Until today, Pyre's shapes were geometric (circle, bars, sprite, fused blobs...). This is a genuinely new
one: a shape that IS a noise field — a single churning, billowing cloud with no hard edge, controlled by
its own zoom/spin/drift knobs. This is the direct, dedicated tool for "make an animated dust cloud."

### 6. `RingWaveModifier` — a shockwave ripple
A ripple that travels *outward from the center* over time (as opposed to the existing "Wobble" modifier,
which is just a fixed side-to-side wiggle). Animate its timing and you get a visible ring of distortion
expanding outward through a shape — a shockwave passing through.

### 7. `SunburstModifier` — charging-energy rays
Alternating bright and dim rays radiating out from the center, like a classic cartoon sunburst or an
energy weapon charging up. Spin it for a "winding up" effect.

### 8. `PulseRingsModifier` — sonar-ping rings
Concentric rings of brightness traveling outward across a shape as it lives out its lifetime — like a
sonar ping or a pulsing energy blast.

### 9. `VoronoiCrackModifier` — shattered crystal / lightning cracks
Applies the Voronoi/cellular idea (above) to crack a shape into faceted cells with visible seams —
shattered glass/crystal, cracked earth, or (with a bright tint instead of a dark one) an electric
lightning-crackle look. This is a completely different *family* of pattern from all the noise-based
effects above — faceted and linear instead of smooth and blobby, so it's worth trying when the others
all feel too "soft."

### 10. `ChromaticAberrationModifier` — RGB color-fringe glitch
Splits the red and blue color channels slightly apart (green stays put), which is the classic "energy
impact" or camera-lens-distortion color-fringe look you see in a lot of modern game VFX.

### 11. Bug fix: Bloom/Glow was making things *darker*
You reported the existing "Bloom (glow)" modifier only ever produced a dark smudge. Root cause: it was
mixing up **premultiplied and straight alpha** (explained above) in its final math step. Concretely: a
glow spreading into previously-empty space around a shape was ending up stored as "color ≈ 0.3, opacity ≈
0.3" — and because of how straight-alpha colors get displayed (color × opacity), that showed up as
0.3 × 0.3 ≈ 0.09: dim and dark, not bright. Fixed by doing the math in the other (premultiplied)
convention and converting back properly — the same way the rest of Pyre's rendering code already does it
elsewhere. Should now produce an actual bright halo instead of a smudge.

---

## Quick reference: where to find each one

All are opt-in — add them via the **"+ Add modifier"** button on a layer or on the blast's global modifier
list, except `NoiseField`, which is a **layer shape** (pick it from the shape radio at the top of a layer,
same place as Disc/Bars/MetaBlob).
