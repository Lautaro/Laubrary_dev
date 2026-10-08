# GoreLab algorithm specification (for the C# / Unity port)

This document describes the technique prototyped in the GoreLab browser lab so that an engineer who has never seen the JavaScript can implement it. Names of source functions appear in parentheses only so the original can be located; they are never the explanation. Companion files in the same folder: `GORELAB_GOLDEN.json` (45 test cases captured by running the prototype), `GORELAB_GOLDEN_GENERATOR.js` (re-creates them), `GORELAB_USER_TAGS.json` (the user's real tags for the retail imp), `GORELAB_USER_TAGS_EXPORT.js` (re-creates that). The golden vectors are the acceptance test: where this text and a vector disagree, the vector wins and the text is a bug.

## 0. The idea in one page

A 2D pixel-art character is made of body parts ("members"): a head (an ellipsoid) and a torso (a rounded box). On every animation frame the author marks each member with a 3D tag: where it is on the sprite, how big it is in three dimensions, and which way it faces. A wound is not stored as pixels. It is stored once, in the member's own coordinate system, as a list of "removers": flat planes (a slice) and capsules (a pellet, a bullet hole, a knife groove). To draw any frame, every pixel of the member sends a line of sight straight into the screen, finds where that line passes through the member's 3D solid, takes out the stretch each remover removes, and looks at what is left: the original pixel (untouched), a freshly exposed cut surface (painted as wound), or nothing (see-through). Because removers live in the member's own coordinates, the same wound lands on the right spot of every frame and every walking direction, and stays on its true side as the character turns. The pixels that the newest swipe changed leave as a flying chunk.

Everything below is deterministic given the tags, the sprite, the removers and a handful of configuration numbers. No part of the cutting algorithm uses a random generator at cut time; randomness appears only when a swipe is turned into removers (shotgun, bullet, knife) and then it is seeded.

## 1. Conventions and data model

### 1.1 Coordinates

- Pixels: x grows to the right, y grows downwards, integer cell (x, y) covers the area [x, x+1) x [y, y+1); its centre is (x+0.5, y+0.5). A pixel index is y*width + x.
- Colours: 32-bit 0xAABBGGRR (red is the lowest byte, alpha 255 in the top byte). Alpha 0 means empty (see-through); any non-zero alpha counts as solid. Palette entries are opaque.
- Sprite-local coordinates are relative to the top-left of a frame's own image. Tags and paint masks are stored in sprite-local coordinates so they survive canvas resizes. The canvas is the stage the character stands on (default 64 x 64). A sprite of size (w, h) is placed on a canvas of (W, H) with floor margin m at: x0 = floor((W - w) / 2) + ox, y0 = (H - m) - h + oy, where ox, oy are the frame's author nudges (default 0). (placeAt)
- Screen space for 3D vectors: x right, y down, z toward the viewer.

### 1.2 Member tag

A tag describes one member on one frame:

| field | meaning |
|---|---|
| c = (cx, cy) | centre, sprite-local pixels |
| rx | half size across the member (pixels). For the head also the depth |
| ry | half size along "up" (pixels) |
| rz | half depth toward the viewer, torso only. For the head depth equals rx |
| n | squareness exponent of the 2D outline and, for a box, of the 3D solid. Head default 2 (oval), torso default 4 |
| kind | "box" for a torso, absent for a head |
| a | 2D angle of up on screen, radians, atan2(uy, ux) of the up vector's screen part |
| u3 | up unit vector (screen space, 3 components) |
| f3 | forward unit vector (the way the face / chest points). Always kept perpendicular to u3 |

Derived: east (the member's right) = u3 x f3 (cross product). West = -east, south = -forward. (rightVec)

Normalisation applied to every tag when it is loaded (normHead, fixFwd): if rx is missing use r for both rx and ry (old saves); if n is missing use 2; if u3 is missing use (cos a, sin a, 0), else normalise it; if f3 is missing use (0,0,1), else normalise it; then make f3 perpendicular to u3: f = f - u*(f.u), normalise; if the result is degenerate (length < 1e-3) use (-u.y, u.x, 0) normalised, and if that is also degenerate use (1,0,0). Finally, only if the xy-length of u3 exceeds 0.2, set a = atan2(u3.y, u3.x). (Otherwise a keeps its stored value.) Golden case `normHead_samples` covers this.

Two different "up" notions exist and must not be confused: the 2D outline test below uses the angle a (cos a, sin a), while the 3D solid uses u3. They normally agree. When u3 points mostly toward or away from the viewer a is not updated, so the outline and the solid can disagree; the prototype lives with that.

### 1.3 The 2D outline test (insideHead)

A sprite-local point p is inside a member's outline when, with u = (cos a, sin a), d = p - c, along = d.u, across = -d.x*u.y + d.y*u.x: |across/rx|^n + |along/ry|^n <= 1. It limits which pixels may ever be touched by that member's cuts, and it is also the basis of all editor hit-testing, blood clean-up and the "head owns the pixel" rule.

### 1.4 Paint masks

Per frame, per member, two sets of sprite-local pixel indices:

- behind: the body is behind the member at this pixel (for example the back of the neck behind a head). If a cut removes such a pixel it is not made see-through; it stays on the body as dark gore (section 8.5).
- exempt ("in front"): the pixel belongs to something drawn in front of the member (an arm in front of the torso, shoulders in front of a head seen from behind). It is never cut, never recoloured, never part of the chunk, never joined to a chunk's connected components, and the clean-up of baked blood skips it.

The head's masks are stored as behind / exempt on the frame; the torso's as tbehind / texempt (the engine reads them through one accessor that returns the pair for a member).

### 1.5 Removers, groups, members

A remover lives in the member's unit-ball coordinates (section 3): east, up, forward, each scaled by the matching half size so the member is the unit ball (or unit superellipsoid).

- Plane: { N = (e, u, f) unit normal, d }. It removes everything with N.p >= d.
- Capsule: { a, b, r } two end points and a radius, all in unit-ball coordinates. It removes the union of the two end spheres and the cylinder between them. Optional field `back` = a 2D screen direction (see 7.4).
- Every remover has a group number g (one per swipe/shot; later swipes get a higher number) and a member name m ("head" or "torso"; default head).
- A frame's wound is the member solid minus all its removers. The newest group is the one with the highest g over all removers of all members; only the pixels that group changes become the flying chunk.

### 1.6 Config values the engine reads

Cut engine: `seed` (1), `jag` (1.2, ragged amplitude, for a 6-pixel head), `jagFreq` (0.45), `bone` (true). Bake: `floorMargin` (3). Swipe-to-remover generators and effects: see section 10 for the full defaults table.

## 2. Mirroring

Eight walk directions exist; five are drawn (front, front-side, side, back-side, back) and three are the same imp turned the other way (direction 5 mirrors 3, 6 mirrors 2, 7 mirrors 1). A mirrored frame is derived, never authored (syncMirror):

- sprite = horizontal flip (pixel x -> w-1-x, w = sprite width), taken after blood clean-up;
- tags through mirrorHead(tag, w): c = (w - c.x, c.y); a = PI - a; u3 = (-u.x, u.y, u.z); f3 = (-f.x, f.y, f.z); rx, ry, rz, n, kind unchanged;
- every mask index (x, y) -> (w-1-x, y); ox -> -ox; oy unchanged; noHead copied.

Crucially, the WOUND is not mirrored. A removed plane or capsule keeps its member-space position; a mirrored direction is the same imp seen from the other side, so only the tag vectors change. Consequence (verified in the goldens `mirror_profile_drawn` / `mirror_profile_mirrored`): a wound on the member's east side is invisible on the profile where east points away from the viewer and visible on the mirrored profile where it points toward the viewer. No special mirror code exists inside the cut itself; the `mirror` argument of the cut is accepted but unused.

## 3. The member solid and the line of sight

For a pixel (x, y) of a member with tag h placed at canvas offset (sx, sy), the pixel centre relative to the member centre is X = x+0.5 - (c.x+sx), Y = y+0.5 - (c.y+sy). The member in unit coordinates: P = (E.(X,Y,Z)/rx, U.(X,Y,Z)/ry, F.(X,Y,Z)/rz) where E, U, F are the east, up, forward vectors, Z is the depth toward the viewer in pixels, and rz = rx for a head without rz (toUnit). A line of sight is the set of points O + z*Dv where O is the unit-space position of (X, Y, 0) and Dv = toUnit(0,0,1) = (E.z/rx, U.z/ry, F.z/rz) is the unit-space direction of "one pixel toward the viewer". z grows toward the viewer, so the FRONT surface is the larger root and the back surface the smaller.

### 3.1 Ellipsoid (head)

Solve |O + z*Dv|^2 = 1: qa = Dv.Dv, qb = O.Dv, disc = qb^2 - qa*(O.O - 1). If disc >= 0 and qa > 1e-9: zf = (-qb + sqrt(disc))/qa, zb = (-qb - sqrt(disc))/qa, Pf = O + zf*Dv. Otherwise (the line misses: pixel at the very edge of the marked outline) zf = zb = -qb/qa (closest approach; 0 if qa tiny) and Pf = normalise(O + z*Dv). (makeSolid)

### 3.2 Superellipsoid (box, torso)

The solid is { P : |Px|^p + |Py|^p + |Pz|^p <= 1 } with p = clamp(n, 2, 12) (n default 4; a larger p is boxier). Define g(z) = |Px|^p + |Py|^p + |Pz|^p - 1 along the line; g is convex in z. The method:

1. Clip the line to the cube [-1,1]^3 per axis (slab method): for each component c with direction d = Dv[c], origin o = O[c]: if |d| < 1e-9 then if |o| > 1 the line misses; otherwise t0 = (-1-o)/d, t1 = (1-o)/d, swapped to be ordered, z0 = max of t0s, z1 = min of t1s. If missing, or z0 > z1, or non-finite: use the fallback below.
2. Ternary search for the lowest point of g in [z0, z1]: 22 iterations, m1 = lo + (hi-lo)/3, m2 = hi - (hi-lo)/3, if g(m1) < g(m2) then hi = m2 else lo = m1. zm = (lo+hi)/2, gm = g(zm).
3. If gm > 0 the line misses the solid (closest approach): zf = zb = zm and Pf = (O + zm*Dv) scaled by 1/(gm+1)^(1/p), which projects the near point onto the surface. (This keeps pixels at the rounded corners of the outline well-defined; they are treated as the rim.)
4. Otherwise bisect for the two roots: front root in [zm, z1] with 22 iterations (m = (a+b)/2; if g(m) <= 0 then a = m else b = m) giving zf = (a+b)/2; back root in [z0, zm] (if g(m) <= 0 then b = m else a = m) giving zb. Pf = O + zf*Dv.
5. Fallback for a missing line: zf = zb = -O.Dv/qa (0 if qa tiny), Pf = normalise(O + zf*Dv).

Both solids return the same triple { zf, zb, Pf }. Golden case `solid_intersections` records it for ellipsoid and box samples including misses; compare to ~1e-6.

## 4. Removers evaluated along a line of sight

For each pixel the removers are evaluated at the line O + z*Dv (member unit space). Each remover yields at most one removed z-interval [lo, hi] (cap: the hull of end-sphere and cylinder intervals; plane: a half-line). Set up once per cut:

- Plane: e1 = normalise(N x (|N.y| > 0.9 ? (1,0,0) : (0,1,0))), e2 = N x e1, rc = sqrt(max(1e-4, 1 - d^2)). Pixels of the wound surface get their colour from the in-plane coordinates of the surface point (section 5).
- Capsule: ab = b - a, L = |ab|, u = ab/L if L > 1e-6 else none (pure sphere).

### 4.1 Plane remover (removal)

g0 = N.O - d, den = N.Dv. If |den| < 1e-6 (line parallel to the plane): removed everywhere on the line if g0 >= 0 (interval (-inf, +inf)), else nothing. Otherwise z0 = -g0/den is where the line meets the plane; Q = O + z0*Dv; the ragged edge shifts the crossing: zs = z0 - jaggedShift/den where jaggedShift = ja * (vnoise3(Q.x*jf + 7, Q.y*jf + 11, Q.z*jf + 13, seed + k*17) * 2 - 1), ja = jag/6, jf = jagFreq*6, seed = cfg.seed*7 + 3 (all integer arithmetic) and k = the index of this remover in the list of removers handed to the cut for that member (not the global index). If ja is 0 there is no shift. If den > 0 the removed interval is [zs, +inf) (the side N.p >= d lies toward larger z), else (-inf, zs]. The noise is evaluated at the point ON the plane, so the ragged edge is the same shape on every frame and view, just placed and scaled with the member. The amplitude is expressed in unit-ball units, so it scales with the member (jag is specified for a 6-pixel head).

Noise primitives (all arithmetic exact in 32-bit; port these bit-exactly; golden case `primitives` lists raw values):

```csharp
// uint32 hash -> [0,1)
static double Hash(int x, int y, int s) {
    unchecked {
        int h = x * 374761393 ^ y * 668265263 ^ s * 1274126177;   // Math.imul semantics = wrapping int multiply
        h = (h ^ (int)((uint)h >> 13)) * 1274126177;
        return ((uint)(h ^ (int)((uint)h >> 16))) / 4294967296.0;
    }
}
// Seeded generator (mulberry32). seed is converted to uint first (seed >>> 0).
sealed class Rng { uint a; public Rng(int seed){ a = (uint)seed; }
    public double Next() { unchecked {
        a += 0x6D2B79F5u; uint t = a;
        t = (t ^ (t >> 15)) * (t | 1u);
        t ^= t + (t ^ (t >> 7)) * (t | 61u);
        return (t ^ (t >> 14)) / 4294967296.0; } } }
// 3D value noise, smoothstep-interpolated, hash lattice
static double VNoise3(double x, double y, double z, int seed) {
    int ix=(int)Math.Floor(x), iy=(int)Math.Floor(y), iz=(int)Math.Floor(z);
    double fx=x-ix, fy=y-iy, fz=z-iz;
    double ux=fx*fx*(3-2*fx), uy=fy*fy*(3-2*fy), uz=fz*fz*(3-2*fz);
    double H(int a,int b,int c)=>Hash(a + 131*c, b, seed);
    double L(double a,double b,double t)=>a+(b-a)*t;
    double c00=L(H(ix,iy,iz),H(ix+1,iy,iz),ux),   c10=L(H(ix,iy+1,iz),H(ix+1,iy+1,iz),ux);
    double c01=L(H(ix,iy,iz+1),H(ix+1,iy,iz+1),ux), c11=L(H(ix,iy+1,iz+1),H(ix+1,iy+1,iz+1),ux);
    return L(L(c00,c10,uy), L(c01,c11,uy), uz);
}
// 1D value noise used by the free Cut tab only: vnoise1(x,s) = lerp(Hash(i,0,s), Hash(i+1,0,s), smoothstep(f))
```

Notes for bit-exactness: in the JavaScript, `x|0` truncates toward zero for hash arguments (the arguments are always integers or floored values in practice, but the test inputs include fractions and negatives); a double hash argument is truncated with the JS ToInt32 rule (modulo 2^32 for large values). The RNG seed goes through `>>> 0` (modulo 2^32). The hash/rng goldens include negative, fractional and huge inputs. The Hash result uses an unsigned right shift on the final xor; `(h ^ (h >>> 16)) >>> 0` is the value divided by 2^32.

### 4.2 Capsule remover

Let w = O - a. The removed interval is the hull (lowest start, highest end) of up to three intervals along the line:

- sphere around a with radius r and sphere around b with radius r: for a sphere centre c: Bq = (O-c).Dv, C = |O-c|^2 - r^2, disc = Bq^2 - qa*C, if disc >= 0 interval [(-Bq - sqrt(disc))/qa, (-Bq + sqrt(disc))/qa] (qa = Dv.Dv);
- the straight part (only if L > 1e-6): an infinite cylinder around the axis u, clipped to the segment. With wu = w.u, Du = Dv.u, wp = w - wu*u, Dp = Dv - Du*u: A = Dp.Dp, Bq = wp.Dp, C = wp.wp - r^2. If A < 1e-9 (line parallel to the axis): inside the cylinder if C <= 0 (interval (-inf, +inf)) else none. Else disc = Bq^2 - A*C; if disc >= 0 the interval is [(-Bq - sqrt)/A, (-Bq + sqrt)/A]. Then clip to the segment: if |Du| < 1e-9 the interval is dropped when wu < 0 or wu > L; otherwise s1 = -wu/Du, s2 = (L - wu)/Du and the interval becomes [max(z1, min(s1,s2)), min(z2, max(s1,s2))]; keep it when non-empty.

Because the hull of the three intervals is used (not the exact union), the tiny gap between disjoint pieces is also removed; this is a harmless property of the prototype that the port must keep.

## 5. The visibility loop and the pixel result

Per cut, for the member tag h with outline test: for every canvas pixel in the square of half side R = 1.7*max(rx, ry) around the member centre (clipped to the canvas) that is solid, not exempt, and inside the member's outline (the 2D test applied to sprite-local x+0.5-sx, y+0.5-sy), compute O, and {zf, zb, Pf} from section 3. The "key" of the pixel is a position hash: key = (floor(Pf.x*9)+40)*10007 + (floor(Pf.y*9)+40)*101 + floor(Pf.z*9)+40 (int arithmetic); the wound-recolour of the rim uses its low and high 16 bits, key & 0xffff and key >> 16.

The state of a pixel under a set of active removers (evalOps): collect each active remover's removed interval [lo, hi] for this line. Start with z = zf (the front surface) and no owner. Repeat at most (number of removers + 1) times: among intervals with lo < z - 1e-9 and hi >= z - 1e-9 (the interval swallows the current front) pick the one with the smallest lo; if none, stop; otherwise set z = lo, owner = that remover; if z <= zb + 1e-9 stop (the whole line of sight is gone). Result:

- no owner: untouched (the original pixel colour stays);
- z <= zb + 1e-9: empty (see-through), owner recorded;
- otherwise: cut surface, owner = the remover whose boundary now forms the front. Colour from the owner at the surface point Q = O + z*Dv (colour functions below).

Everything the member does is evaluated from the INTACT sprite, so each frame carries all removers at once and no state is accumulated pixel by pixel. The body result is evaluated with all removers; a second evaluation without the newest group gives what the pixel showed "before".

### 5.1 Wound colours

Palette constants (0xAABBGGRR values are in the golden file `palette`): flesh = [rgb(110,12,14), rgb(150,22,22), rgb(190,40,34), rgb(224,94,72)]; bone = [rgb(232,222,196), rgb(196,184,152)]; blood = [rgb(70,0,0), rgb(120,6,6), rgb(170,14,14), rgb(214,34,28), rgb(244,78,56)]; crater = rgb(92,6,10); dark gore = rgb(78,4,8); scorch = rgb(28,20,16). mix(c1, c2, t) mixes per channel linearly and truncates each channel to an integer (`(int)`), opaque result.

Plane surface colour at point Q: s = Q.e1, t = Q.e2, rad = sqrt(s^2+t^2)/rc, hv = Hash(floor(s*9)+500, floor(t*9)+500, 21). If cfg.bone and rad < 0.3: bone[(int)(hv*2)]; else if rad > 0.82: flesh[0] (dark rim); else flesh[(int)(hv*3.2)]. (So the bone core is the middle of the cut face by position on the plane, independent of view.)

Capsule surface colour at Q: mix(flesh[(int)(Hash(floor(Q.x*11)+500, floor(Q.y*11)+500, floor(Q.z*11)+21)*3.2)], crater, 0.45).

### 5.2 Body

Start from a copy of the placed sprite. For each processed pixel: state cut surface -> body pixel = that colour. State empty -> body pixel = 0, except if the pixel is in the member's behind mask: it becomes mix(original, darkGore, 0.72 + 0.2*Hash(key & 0xffff, key >> 16, 5)) (dark gore keeps the sprite texture). Untouched pixels stay.

Rim recolour: any solid body pixel that was NOT processed as cut surface or empty (state 0), is not exempt, and has at least one of its four neighbours that is state "empty and now alpha 0", is repainted flesh[(int)(Hash(key & 0xffff, key >> 16, 12)*3.2)]: the raw edge of the body beside a removed part looks like a wound. (Pixels outside the processed area have key 0.)

Bleed points ("attPts", wound points where blood leaves): every cut-surface pixel is one, with normal = its owner's bodyOut (7.4); every rim-recoloured pixel is one with normal = the normalised sum of the offsets to its empty 4-neighbours. Each point is unique per pixel (first writer wins).

## 6. The flying chunk and gibs

Chunk pixels (the newest group): a pixel whose "before" colour (the pixel as the older groups left it: untouched original if the older evaluation says state 0, 0 if empty, the cut-surface colour if it was already a surface) has alpha > 0, and whose result is empty, or is a cut surface that differs from before (different state, or the owner changed). The chunk grid holds the "before" colours of chunk pixels. If no older groups exist the "before" is simply the intact sprite. Then each chunk pixel that has a 4-neighbour which is solid-before, not exempt, and not itself chunk (the cut line toward the body) is recoloured flesh[(int)(Hash(key & 0xffff, key >> 16, 13)*3.2)]: the edge facing the body looks like a wound.

Connected components (4-connectivity, scan order, solid = alpha > 0; component ids in order of first pixel in row-major scan) of the chunk grid: area < 1 impossible; area < 3 -> each pixel becomes a gib { x+0.5, y+0.5, colour }; area >= 3 -> a physics piece: the tightly cropped grid (extract), its top-left (x, y), `out` (the launch direction, 7.4) and its edge points: every piece pixel with at least one 4-neighbour that is not chunk but solid-before and not exempt, with normal = normalised sum of those offsets, stored relative to the cropped grid. Pieces are sorted by bounding box area, largest first (stable). capCount = number of chunk pixels, capCentroid = mean of their centres (x+0.5, y+0.5), or the member centre if empty. The result holds { body, attPts, outAtt, parts, gibs, capMask, capCount, capCentroid }.

### 6.1 Directions (chunkDir, bodyOut, outAtt)

Per remover: plane: gn = the plane normal mapped to screen (sum of N.e*E/rx etc.; i.e. Vx = N.e*sE.x + N.u*sU.x + N.f*sF.x with sE = E/rx, sU = U/ry, sF = F/rz, same for y and z); if the screen part |gn.xy|/|gn| > 0.25 then chunkDir = normalise(gn.xy) else (the plane faces the viewer) if the remover belongs to the newest group chunkDir = direction from the member centre to the chunk centroid (fallback (0,-1)) else (0,-1); bodyOut = chunkDir. Straight-on capsule (has `back`): chunkDir = bodyOut = normalised `back` (fallback (-1,0)): debris and blood leave back toward the shooter. Other capsule: s = normalise(screen projection of b-a); chunkDir = s, bodyOut = -s. outAtt = the chunkDir of the first remover of the newest group (or (0,-1)); it is used as the default launch direction of the pieces.

## 7. Frame level: cutFrame

Cut one animation frame with a list of removers (cutFrame). Let lastG = max g over all removers. Members involved = those of ["head","torso"] that have at least one remover (m default "head").

1. If any involved member has no tag on this frame, except a head on a frame flagged noHead, the frame is "not set up": return the intact frame with missing = true (no wound, no chunk). The view marks such frames with a red border.
2. If the torso is involved and the frame has a head tag and is not noHead: compute the set headOwned = every solid, non-head-exempt canvas pixel inside the head's outline (square of 1.7*max(rx,ry) around the head). Where head and torso overlap on screen the head owns the pixel.
3. For each involved member in order head, torso: if its tag is absent (noHead head) skip. Convert its two masks to canvas index sets. For the torso the exempt set additionally contains headOwned. Run the member cut (cutHead3D) with only that member's removers and lastG passed explicitly as the global newest group (so a member whose removers are all older than the newest group contributes a body but an EMPTY chunk, case `frame_older_group_member_untouched`).
4. Merge: out.body = intact frame, then every pixel where a member result differs from the intact frame is copied (head first, torso second so the torso result wins; they cannot collide because the torso ignores head-owned pixels); capMasks OR-ed; attPts, parts, gibs concatenated; parts re-sorted by bounding box area; capCount summed; capCentroid is the count-weighted mean; outAtt is the one of the member with the larger chunk.

A frame with noHead true and head removers is returned intact and NOT missing (the lying death frame whose head is not separable).

## 8. Bake and play flow

### 8.1 deathBake (and walk bake)

For frame i of a sequence with start frame `hit` (0 for walking): place the sprite on the canvas (1.1). If i < hit the frame is kept as drawn (no wound). Otherwise if there are no removers an error "Frame N needs a cut" is reported; else cutFrame gives: headless (the body with all wounds), stump points (bleed points), parts, gibs, wound centroid (`at`), neck (the mean position and normalised mean normal of the bleed points, or none), missing flag, and `changed` (number of pixels differing from the intact frame; 0 means the cut lies on a side this frame does not show). floorY = H - floorMargin. Golden: `deathBake_flow`, `deathBake_no_ops_error`.

Walking: all eight directions are baked with the same removers (every direction must have the tags it needs, otherwise the first missing one is named in an error toast, but in the current editor missing frames just play uncut with a red border, see 11.5).

### 8.2 Play

Death plays frames in order from the start frame, each for frameDur (0.14 s). At the hit frame the pieces and gibs are launched (7.5) and the stump bleeds from the neck points frame by frame; the stump inherits the body's motion (velocity from the movement of the neck between frames). Walk loops four frames, walkDur 0.16 s per frame, in place; the walker stays headless and keeps walking until the walkReset timer (8 s, 0 = never) or Reset. A new swipe on an already cut imp ADDS removers with a higher group number, re-bakes every frame (each frame now carries all removers) and launches only the newest chunk from the frame on screen. Reset or auto reset clears all removers.

### 8.3 Pieces and gibs launch (minimum viable, see section 12)

Piece launch: direction = shot*0.85 + part.out*0.15 minus lift in y (headLift 0.35), speed = headKick (90) * (0.8 + 0.4*rand) (x1.3 for the second and later pieces), spin from the off-centre kick: w = clamp((rx*dy - ry*dx) * m * speed / I * headSpin + (rand-0.5)*2, -14, 14) where (rx, ry) is the offset of the edge-point centroid from the piece's centre of mass. Rotation is snapped to steps of cfg.snapDeg (15 degrees).

## 9. Swipe to removers

### 9.1 Slice: planeFromSwipe

Input: member tag h, its canvas offset (sx, sy), swipe p0 to p1 in canvas coordinates, flipInvert. cx = h.c.x+sx, cy = h.c.y+sy, d = p1-p0, l = |d|; if l < 2 return nothing. Screen-plane normal of the swipe ns = (-dy/l, dx/l); o = ns.(p0 - (cx,cy)). The plane contains the line of sight, so in member coordinates its normal is m = ( rx*(ns.E.xy), ry*(ns.U.xy), rz*(ns.F.xy) ) (dot products of ns with the screen xy parts of E, U, F), ml = |m|; if ml < 1e-6 return nothing. N = m/ml, d = o/ml; if |d| >= 0.98 the plane misses the ball -> nothing. Which side flies: the neck of the member is at (0,-1,0) in member coordinates; neckSide = -N.y - d. The flying side is the one WITHOUT the neck: if |neckSide| > 0.03 then flip (N, d) -> (-N, -d) when neckSide > 0; else (the cut passes through the neck point) flip when N.y < 0. Finally if flipInvert flip again. The returned plane removes N.p >= d. Golden: `planeFromSwipe_cases`.

### 9.2 Remove head (neck plane)

A single plane with N = (0,1,0) and d = -neckDepth (neckDepth 0.8) in the HEAD's coordinates, group = next group, member head. It removes everything above 0.8 head-radii below the centre, i.e. the head. Only issued if no neck plane exists yet. Remove head always targets the head.

### 9.3 Shotgun: shotFromSwipe

Muzzle p0, aim toward p1. base = atan2(p1-p0); cone = coneDeg in radians; mean = (rx+ry)/2; dc = distance from muzzle to member centre; fall = max(0.35, 1 - rangeFalloff * max(0, dc-10)/100). Random generator: GL.rng(seed) (seed = cfg.seed*131 + shotCounter, +7919 for the torso). For each pellet i < pellets, in order: off = clamp(gauss*0.55, -1, 1) where gauss = (R()+R()+R()+R()-2)/0.58 (four draws); E = energy*(0.55 + 0.9*R())*(1 - 0.45*|off|); ang = base + off*cone; direction (dx,dy) = (cos, sin). tc = (centre - p0).dir, lat = |cross((centre-p0), dir)|; z = (R()*2-1)*0.9*rz (depth, a fourth/fifth draw: note the order, gauss four, E one, z one). Skip the pellet if lat > mean*1.15 or tc < -mean. entry = tc - sqrt(max(0, mean^2 - lat^2 - z^2*0.5)); pen = (E*fall/max(0.2, toughness))*1.1; skip if pen <= 0.3; rpx = radius*(0.55 + 0.75*min(1.2, (E/energy)*fall)). Capsule from t0 = max(entry, -mean) - 4 to t1 = max(entry, 0) + pen along the line at depth z: a = toUnit(p0 + dir*t0 - centre, z), b = toUnit(p0 + dir*t1 - centre, z), r = max(0.05, rpx/mean). Pellets fly in the screen plane at their own random depth, so the same blast is right whichever way the member later turns. Golden: `shotFromSwipe_seed7`.

### 9.4 Straight-on bullet: straightShot

Each pellet (count 1 = a single bullet) takes one random point on the part of its aim line that crosses the member's visible solid pixels, then digs a shallow capsule straight into the screen from the surface facing the viewer. Per pellet i < count: off = (count > 1 ? clamp(gauss*0.55,-1,1) : 0) (gauss only drawn when count > 1), E as above (one draw), dir = (cos, sin)(base + off*cone). hits = all t = 0, 0.5, ... < 300 such that point p0 + dir*t is inside the member's 2D outline AND (if the placed sprite is known) on a solid pixel. If hits is empty skip the pellet. pen = (E*fall/max(0.2, toughness))*1.1*straightDepth (default 0.4), rpx as in 9.3. make(t): (X,Y) = point - member centre; O = toUnit(X,Y,0); zf = front-surface depth (section 3) of that pixel; capsule a = toUnit(X,Y, zf+3), b = toUnit(X,Y, zf - max(0.6, pen)), r = max(0.05, rpx/mean), back = (-dir.x, -dir.y). Candidate selection: without a scorer, one candidate index (int)(R()*hits.length). With a scorer (score(op) in 0..1, "on how many frames of the animation would this hole be visible"): up to min(10, hits.length) candidates are drawn one by one (each by one R() draw), the best score kept (first of equals), stopping early when a candidate scores 1. Golden: `straightShot_*`; the test scorers are described in the file.

In the editor, the scorer for a member counts the frames of the animation being played (walk: the four frames of the current direction; death: from the start frame on) that carry this member's tag where the hole's screen position (the capsule axis midpoint projected to the screen, opScreenPoint) falls on a solid sprite pixel inside the member's outline, not painted exempt, and for the torso not under the head (unless exempt). Score = visible frames / tagged frames (1 if none are tagged).

### 9.5 Cut (knife slash): gashFromSwipe

A damage type is a GENERATOR of removers: a pure function from (swipe, tag, seed) to a list of removers. The Cut damage type (a shallow knife slash) needed no change in the cut engine at all; it only produces capsules of the existing kind, exactly like a bullet. The port should follow this pattern: any new damage type is a new generator, never a new branch in the per-pixel loop.

Input: member tag h at canvas offset (sx, sy), swipe p0 to p1, cfg {gashDepth 1.6, gashWidth 0.8, gashWobble 0.9}, integer seed, the placed sprite `full` (optional; when given pixels must be solid). len = |p1-p0|; if len < 1 return an empty list. dir = (p1-p0)/len, n = (-dir.y, dir.x) (the left normal). depthPx = gashDepth, rpx = gashWidth (a radius in pixels), amp = gashWobble (pixels), mean = (rx+ry)/2 (for the head ry could be used; both members use (rx+ry)/2).

Noise: vn(x, k) = lerp(Hash(floor(x), seed, k), Hash(floor(x)+1, seed, k), smoothstep(frac(x))) with smoothstep(f) = f*f*(3-2f); the arguments of Hash are (cell index, seed, channel k), so there are three independent smooth 1D noises in [0,1): channel 91 decides breaks, 17 the sideways wobble, 33 the depth.

blood direction: up = n if n.y < 0 else -n (the perpendicular that points upward on screen). Blood and crumbs leave sideways/upward from the groove: this is stored as the capsule's `back` direction.

Chain: for t = 0, 0.8, 1.6, ... while t <= len (step 0.8 pixels; accumulate by repeated addition of 0.8 as the prototype's loop does, or compute t = k*0.8 - they agree to the last bit only if you add repeatedly, so add repeatedly):
1. break rule: if vn(t*0.22, 91) > 0.9 skip this sample (the line "breaks for a moment"; about 10 percent of positions, in runs of several samples);
2. wobble: w = (vn(t*0.35, 17)*2 - 1)*amp; the sample point P = p0 + dir*t + n*w;
3. the sample is dropped unless P is inside the member's 2D outline (in sprite-local coordinates P - (sx,sy)) and, when `full` is given, the pixel floor(P.x), floor(P.y) is solid;
4. (X,Y) = P - member centre; zf = front-surface depth of O = toUnit(X,Y,0) (section 3); dep = max(0.6, depthPx*(0.55 + 0.9*vn(t*0.5, 33))) (depth varies between 0.55 and 1.45 times gashDepth, never less than 0.6 pixel);
5. emit the capsule { a = toUnit(X, Y, zf+3), b = toUnit(X, Y, zf - dep), r = max(0.05, rpx/mean), back = up }.

The capsules overlap (spacing 0.8 px, radius about 0.8 px), forming a continuous groove with a ragged width and depth; all belong to the same group. Because the capsules are stored in member coordinates the groove stays on the same spot of the body as the character turns, and is invisible on frames that do not show that side. With the default Target "Auto" the generator runs for every tagged member the swipe touches (each member gets its own seed, +7919 for the torso). Golden: `gash_short_torso`, `gash_long_torso` (the generated capsules) and `*_applied` (the cut result).

### 9.6 Choosing members and seeds

Target = Auto | Head | Torso (Remove head ignores it). For a slice with target Auto the plane goes to the member the swipe line crosses most: for each available member count the samples t = -150..150 step 0.5 along the infinite swipe line that lie inside the member's outline and on a solid pixel; pick the highest (ties: first). For blast, bullet and cut, Auto = every tagged member (bullet: one member is picked at random among those that produce a hole: index floor(Hash(shotCounter, 3, cfg.seed)*count) % count). The shot seed is cfg.seed*131 + shotCounter (+7919 for the torso); shotCounter increments after every fired swipe. A "frame not set up" (no tags needed by the effect) gets a toast and nothing is cut; a swipe that merely misses cuts nothing and says nothing; and if the cut changes no pixel of the frame shown, nothing is fired.

## 10. Defaults (app.js GROUPS and DEF)

Intensity: gore 0.6. Blood: P0 0.8, tau 2.6, bpm 95, pulseDepth 0.85, pulseSharp 5, vmax 120, qmax 220, cone 12, vCling 22, dripMass 0.9, crawlInterval 0.07, drag 0.2, splashSpeed 110. Timing and launch: frameDur 0.14, headKick 90, headLift 0.35, headSpin 1, walkDur 0.16, walkReset 8, autoReset 2.5. World: gravity 220, floorMargin 3, restitution 0.25, friction 0.6, snapDeg 15. Cut (ragged): jag 1.2, jagFreq 0.45, fling 60, flingSpread 0.5, flingLift 0.25, spin 1, pieceP 0.35, pieceTau 0.9. Blast: pellets 30, coneDeg 12, energy 7, radius 1.3, toughness 0.9, rangeFalloff 0.3, chunkSize 3, chunkSpeed 120, backSpatter 0.12, bloodPerPixel 0.22, goreTint 0.22, scorch 0.4, pieceSpeed 80. Others: seed 1, bone true, neckDepth 0.8, neckHalf 1.25 (legacy), straightOn false, straightDepth 0.4, gashDepth 1.6, gashWidth 0.8, gashWobble 0.9, shapeAlpha 1, paintAlpha 1. Canvas 64 x 64 (settable to 256). Clean-blood mode default `head`.

## 11. Authoring (what the editor lets the author do)

The tool for the author is the "Member tagging" screen. A Unity port needs an equivalent authoring tool or an import of the data in `GORELAB_USER_TAGS.json`.

### 11.1 Data being authored

Animations: Death (one direction, five frames, start frame selectable, default 0) and Walk (four frames in each of eight directions; directions 0..4 are drawn, 5..7 are derived mirrors, read-only). Per drawn frame: head tag, torso tag, ox, oy (placement nudge in pixels), noHead flag, four masks (head behind/exempt, torso behind/exempt). Per animation: settings saved with the tags: effect, target, flipInvert, clean mode, start frame.

### 11.2 Member chooser and tabs

A Head / Torso chooser selects the active member for every tag editor. Tabs under the preview (the same on desktop and phone; they move into the toolbar in full screen):

- Shape: draw, move, resize the member's outline. A new shape is dragged out corner to corner like a selection marquee (axis-aligned; centre = midpoint, half sizes = half the drag, minimum 3 each; a torso gets rz = max(3, 0.6*rx)); a tap makes nothing. Once a shape exists a drag starting outside it and off its edge is ignored (a one-time hint) - it never replaces the shape; Clear shape removes it. Dragging inside moves; dragging near the edge (band = min(max(4 px, 26 screen px), 35 percent of the smaller half size)) resizes ANCHORED: the grabbed side follows the pointer, the far side stays put (per axis: r' = (sign*q + r0)/(1+|e|), centre shift = sign*(r'-r0), e = the grabbed point's normalised coordinate). Controls: squareness n (head 2 oval; torso box default 4), depth rz for the torso (Frames fold slider).
- Rotate: only the U and F dots (up and forward) are draggable. U follows the pointer over the sphere's near half; crossing the rim and coming back continues on the far half (trackball behaviour); F slides along the equator perpendicular to up; a tap on a dot flips it to the far half; Turn -45 / +45 / 180 degrees rotate forward about up (f' = f cos + east sin). Default (this frame) / Default all (this direction) set forward to the direction's expected forward (the table below) and re-orthogonalise; Hide far side (hold) hides far-side drawing.
- Paint: paint the Behind or In front (exempt) layer for the active member with a round brush (1..6, only solid sprite pixels inside the member's outline); Paint | Erase; right-click drag erases; Fill shape; Clear layer. A pixel belongs to at most one layer. Painting exempt re-runs blood clean-up.
- Frame: previous / next frame; Carry (move the shape to the next frame by matching its pixels: search shifts within a radius and rotations -60..60 degrees in 10-degree steps, scoring +3 for equal colour, +1 for solid, -1 for empty, minus 0.1*|shift| and 0.4*|angle|; the u3/f3 vectors are rotated by the in-plane rotation found, the masks are carried with inverse mapping); Carry all; Same shape on all frames; nudge ox/oy; Start frame (Death); Auto-tag (guess): puts a rough head (the top of the sprite: ry = max(4, 0.115*H), rx = 0.95*ry) and a rough torso box (centre 2.5 head-heights below the head, rx 1.9x head rx, ry 1.9x head ry, rz 1.2x head rx, n 3.5) on every untagged frame.
- Play: choose the Effect (Remove head | Slice | Shotgun blast | Bullet | Cut) and Target, Flip side (persistent inversion of the automatic fly side), Reset, Pause, Step, speed; drag a swipe on the live imp; while dragging an amber preview of exactly what would fly off and an arrow of its direction are shown (the preview uses the same functions with the same seed, so preview = fired). Turn the walker with arrows.

### 11.3 Default orientation per direction

Expected forward (screen space x right, y down, z toward viewer): Front (0,0,1); Front-side (-0.7071,0,0.7071); Side (-1,0,0); Back-side (-0.7071,0,-0.7071); Back (0,0,-1). New tags get the direction's forward and up (0,-1,0). A stored head forward of exactly (0,0,1) on a non-front direction is treated as the placeholder an earlier save wrote and is replaced by the direction's default at load. Torsos get no such substitution.

### 11.4 Orientation sanity warning

If the active member's forward has a negative dot product with the direction's expected forward the Setup view shows an amber "faces away from where this direction looks" warning; if head and torso forward vectors have a negative dot product, "Head and torso face opposite ways". Cause in practice: a stray tap on the F dot. A tag facing away makes a bullet enter the box's back face and vanish from neighbouring directions (the engine is right, the tags were wrong).

### 11.5 Frame status in Play

Red border + "Frame N is not set up: no wound shown": the frame lacks the tag the current effect needs (it plays as drawn). Amber border + "the cut is on a side this frame does not show": the frame is set up, a cut exists, and the baked frame equals the intact frame (changed = 0). Otherwise no border. A frame flagged noHead is deliberately skipped, not missing.

### 11.6 Undo / redo

Undo/redo covers the ASSET: every source frame's head and torso tag, all four paint masks, nudges, noHead flags and the start frame (one JSON snapshot). Not covered: view settings (zoom, tab, opacities, selection) and effect parameters. Every edit that saves also notes a history step; edits within 450 ms of each other (a slider drag, a paint stroke) are merged into one step; a new edit after an undo drops the redo branch. Shortcuts Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z in Setup. Restoring re-derives mirrors, blood clean-up and thumbnails. Any future editing feature must record a history step and be tested with undo and redo.

### 11.7 Blood clean-up of baked sprites (cleanBlood)

Source sprites contain painted blood. Mode off | head | all (default head). A pixel is "blood" when alpha > 0, r >= 60, g < 0.35*r and b < 0.35*r. In mode head only pixels inside the head outline grown (centre moved 0.35*ry along up, rx*1.3, ry*1.5) are candidates; exempt pixels are skipped. Up to 12 passes: a bloody pixel is repainted with a clean 8-neighbour colour picked by Hash(x, y, 77)*neighbourCount when it has at least 2 clean neighbours (1 from pass 6 on); repainted pixels become clean for the next pass. Leftover bloody pixels (no clean neighbour ever) are deleted. Golden: `cleanBlood_head`, `cleanBlood_all`.

## 12. Blood and flying pieces: minimum viable behaviour for Unity

The cut engine's output is exactly: the new body of each frame, a set of bleed points (position, outward normal), pieces (cropped sprite, position, launch direction, edge points) and gibs (position, colour). The prototype then runs a small pixel physics world. For the first Unity version implement only this:

1. Pieces: each piece becomes a rigid sprite body (its own texture, rotation snapped to 15 degrees steps if pixel-art style is wanted), launched as in 8.3, bouncing with restitution 0.25 and friction 0.6, falling under gravity (220 px/s^2 at 64 px canvas scale), coming to rest after 0.35 s of near stillness on the floor.
2. Stump and piece bleeding: pick bleed points of the stump (per frame, they move with the animation) and emit blood droplets from them along the point normal with half-angle cone 12 degrees, speed up to vmax 120 px/s scaled by a pressure value that starts at P0 0.8 and decays exponentially with time constant 2.6 s (pieces: pressure x0.35, time constant 0.9 s), pulsing with the heartbeat (95 bpm slowing as pressure falls). Droplets are 1-pixel blood-palette particles under gravity that leave a stain pixel (and a floor pool) where they land.
3. Gibs: each loose crumb becomes a 1-pixel chunk, mixed 22 percent toward blood-palette colour, flung along the shot direction at chunkSpeed 120 with random spread +-0.45 rad, with 22 percent x gore level chance of a droplet each.
4. Gore level scales the amount of droplets and gibs (0 = clean holes).

Everything else about blood (creeping rivulets over the body surface, drip beads, floor pooling, the exact regimes gush/squirt/pour/trickle) can be postponed. The cut engine is independent of all of it.

## 13. Things that are prototype-only and need NOT be ported

- All browser UI mechanics: the stage canvas and integer scaling, full-screen overlay, pinch and wheel zoom, toolbars, tabs, toasts, drawing of the sphere and box wireframes, live reload, the Kit view, the service worker, server persistence of state.json, the poll-safe rendering.
- The Cut tab (free slice of a flat sprite by a line with the 1D-noise ragged edge, `cutAnalyse`/`cutBake`) and the Blast tab (2D pellet-cone blast with energy lost per pixel, scorch ring, creeping blast wounds). Only the member-based pipeline of this document is the product.
- The pixel-physics blood simulation beyond the minimum in section 12: creeping crawlers, drip beads, hanging beads, pool shaping, droplet splash model, the pressure regime labels. Their parameters are in the defaults table only for reference.
- Editor conveniences that exist only to make tagging faster: Auto-tag guess, Carry matching, Same shape on all, the Hide-far-side hold, thumbnail strips, the status overlay text.
- The older exports (Export bake of neck lines and death.json) and the legacy neck-line, circle and 2D slice-line formats (only normalisation of old circle saves, `r` -> rx = ry, is kept in normHead).

## 14. Ambiguities, quirks and probable bugs found while specifying

1. The head tag's 2D outline uses the angle `a`, the 3D solid uses u3; they can disagree when u3 points mostly along z (a is then not updated). The user's data does not hit this (warnings list is empty for it) but the port should keep both fields.
2. The `mirror` flag passed to the cut and to the basis builder is unused. Mirroring is purely in the tags/sprites. Do not add a mirror branch.
3. The torso's jag noise index k is the remover's position within the member's own list, not the global list; the head and torso therefore get independent ragged edges. Easy to get wrong.
4. In the capsule remover the interval is the HULL of two spheres and a cylinder, not their union; the gap between disjoint pieces would be removed too. Normally the three overlap, so it rarely matters.
5. For a line of sight that misses the solid (outline corner pixels of the box and the very rim of the head) zf = zb, so any plane in front of that point makes the pixel see-through ("2") and a plane behind leaves it untouched; combined with the rim recolour this produces the slightly irregular wound border seen in the goldens.
6. `GL.upVec` is used by the neck/line code of the legacy path, the editor sphere and clean-up; the cut engine itself never calls it.
7. `deathPlay` ignores earlier frames and always starts on the hit frame; `deathBake` still bakes frames after hit only. If the stage is later changed so the death can be hit mid-sequence, check the `changed` counter logic.
8. Walk frames are placed bottom-aligned and centred with no per-frame foot alignment: sprite bounding boxes differ per frame, so the character can jitter by a pixel or two; the nudges ox/oy exist to fix this by hand.
9. The two cfg names `neckHalf` and `keepSide` remain in the defaults but nothing reads them in the member pipeline.
10. The retail walk sprites for direction 3 and 4 (back-side, back) have no torso tags in the user's data, so a torso-targeted cut on those directions plays uncut with a red border; the export lists this under warnings. Walk head tags carry non-zero ox/oy nowhere (all 0).
11. A bullet wound is only about 7 pixels on a 64 pixel canvas; tests that look for it by eye often miss it. The golden `straightShot_*` cases carry the exact capsules.
12. floating-point: the 22-iteration ternary and bisection loops are deterministic but depend on Math.Pow and sqrt being correctly rounded; expect rare 1-ulp differences. The goldens' integer outputs should still match; if one pixel differs, check it is a borderline case before suspecting the algorithm.
