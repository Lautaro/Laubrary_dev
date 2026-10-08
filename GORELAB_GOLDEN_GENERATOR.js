/* GORELAB_GOLDEN_GENERATOR.js
 *
 * Produces GORELAB_GOLDEN.json (golden test vectors for the C#/Unity port of GoreLab's cut engine) by RUNNING the prototype's own
 * JavaScript (sim.js, cut.js, death.js, cut3d.js) inside a node `vm` sandbox that provides only `window`.  Nothing in the prototype is
 * modified: cut3d.js is evaluated with two extra lines appended in memory (they export two file-private helpers, vnoise3 and makeSolid,
 * so they can be sampled directly).  Nothing browser-only is needed because the engine files only touch `window.GL`.
 *
 * Run:   node "D:\Unity\Laubrary Dev - GoreLab\GORELAB_GOLDEN_GENERATOR.js"
 * Env:   GORELAB_WEB   folder holding sim.js / cut.js / death.js / cut3d.js   (default D:/CODEZ/GoreLab/web)
 *        GORELAB_OUT   output json path                                        (default GORELAB_GOLDEN.json next to this script)
 * The run is deterministic: running it twice yields byte-identical output (it asserts a few self-checks and exits non-zero if one fails).
 */
'use strict';
const vm = require('vm'), fs = require('fs'), path = require('path');
const WEB = process.env.GORELAB_WEB || 'D:/CODEZ/GoreLab/web';
const OUT = process.env.GORELAB_OUT || path.join(__dirname, 'GORELAB_GOLDEN.json');

/* ------------------------------------------------------------------ load the prototype */
const window = {};
const ctx = vm.createContext({ window, console });
const load = (f, patch) => { let src = fs.readFileSync(path.join(WEB, f), 'utf8'); if (patch) src = patch(src); vm.runInContext(src, ctx, { filename: f }); };
load('sim.js'); load('cut.js'); load('death.js');
load('cut3d.js', (s) => { const i = s.lastIndexOf('})();'); return s.slice(0, i) + '  GL._vnoise3 = vnoise3; GL._makeSolid = makeSolid;\n' + s.slice(i); });
const GL = window.GL;

/* ------------------------------------------------------------------ helpers */
const fail = (m) => { console.error('SELF-CHECK FAILED: ' + m); process.exit(1); };
const clone = (o) => JSON.parse(JSON.stringify(o));
/** FNV-1a 32 over a Uint32 array, each element fed as 4 little-endian bytes. */
const fnv = (arr) => { let h = 0x811c9dc5; for (let i = 0; i < arr.length; i++) { let v = arr[i] >>> 0; for (let k = 0; k < 4; k++) { h ^= v & 255; h = Math.imul(h, 0x01000193) >>> 0; v >>>= 8; } } return h >>> 0; };
const hex8 = (n) => (n >>> 0).toString(16).padStart(8, '0');
const rle = (arr) => { const out = []; let i = 0; while (i < arr.length) { let j = i; while (j < arr.length && arr[j] === arr[i]) j++; out.push([j - i, arr[i] >>> 0]); i = j; } return out; };
const unrle = (r, n) => { const a = new Uint32Array(n); let p = 0; for (const [c, v] of r) for (let k = 0; k < c; k++) a[p++] = v; return a; };
const r6 = (v) => Math.round(v * 1e6) / 1e6;

/* ------------------------------------------------------------------ synthetic sprites (stored as explicit RLE of 0xAABBGGRR) */
const sprites = {};
const addSprite = (id, g, note) => { sprites[id] = { w: g.w, h: g.h, note, rle: rle(g.d) }; return g; };
const spriteGrid = (id) => new GL.Grid(sprites[id].w, sprites[id].h, unrle(sprites[id].rle, sprites[id].w * sprites[id].h));
/* "imp24": 24x24. Head = ellipse centre (12,6) radii 5 x 5.5; torso = rounded block x 6..17, y 11..22; left arm x 2..5, y 11..19; right arm x 18..21, y 12..17;
 * legs x 8..10 and 13..15, y 22..23.  Colour = base colour of the region + 8 * floor(3 * hash(x, y, 9)) on each channel. Asymmetric on purpose (so a flip is visible). */
const makeImp = () => {
  const g = new GL.Grid(24, 24);
  const ell = (x, y, cx, cy, rx, ry) => ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1;
  for (let y = 0; y < 24; y++) for (let x = 0; x < 24; x++) {
    let base = null;
    if (ell(x, y, 12, 6, 5, 5.5)) base = [150, 100, 70];
    else if (x >= 6 && x <= 17 && y >= 11 && y <= 22 && !((x === 6 || x === 17) && (y === 11 || y === 22))) base = [120, 80, 60];
    else if (x >= 2 && x <= 5 && y >= 11 && y <= 19) base = [140, 95, 65];
    else if (x >= 18 && x <= 21 && y >= 12 && y <= 17) base = [140, 95, 65];
    else if ((x >= 8 && x <= 10 || x >= 13 && x <= 15) && y >= 22) base = [90, 60, 45];
    if (!base) continue;
    const v = 8 * Math.floor(3 * GL.hash(x, y, 9));
    g.d[y * 24 + x] = GL.rgb(base[0] + v, base[1] + v, base[2] + v);
  }
  return g;
};
const imp = addSprite('imp24', makeImp(), '24x24 synthetic imp (head ellipse, torso block, two arms, legs). Rebuild from rle or from the generator recipe in this file.');
const impFlip = addSprite('imp24_flipped', GL.flipGrid(imp), 'imp24 mirrored left-right (GL.flipGrid): pixel (x,y) -> (23-x, y).');
/* a copy of imp24 with baked-in blood: a 3x3 blob on the head, a blob touching nothing (a speck) at (20,3) */
const bloody = imp.clone();
for (let y = 3; y <= 5; y++) for (let x = 10; x <= 12; x++) bloody.d[y * 24 + x] = GL.rgb(170, 14, 14);
bloody.d[3 * 24 + 20] = GL.rgb(170, 14, 14);
addSprite('imp24_bloody', bloody, 'imp24 with a 3x3 blood blob at x10..12,y3..5 (inside the head) and a lone blood pixel at (20,3) on an empty cell.');

/* ------------------------------------------------------------------ tags */
const HEAD = (o = {}) => GL.normHead(Object.assign({ c: [12, 6], rx: 5, ry: 5.5, n: 2, a: -Math.PI / 2, u3: [0, -1, 0], f3: [0, 0, 1] }, o));
const TORSO = (o = {}) => GL.normHead(Object.assign({ kind: 'box', c: [12, 16.5], rx: 6, ry: 6.5, rz: 4, n: 4, a: -Math.PI / 2, u3: [0, -1, 0], f3: [0, 0, 1] }, o));
const CFG = { seed: 1, jag: 1.2, jagFreq: 0.45, bone: true };                                          // app defaults for the fields cutHead3D reads
const CANVAS = { W: 32, H: 32, floorMargin: 2 };                                                      // 24x24 sprite -> placeAt gives sx=4, sy=6
const PLACE = GL.placeAt({ w: 24, h: 24 }, 32, 32, 32 - 2, 0, 0);                                      // [4,6]

/* ------------------------------------------------------------------ result description */
const describe = (r, full, W, H) => {
  const rows = [], chunkRows = []; let changed = 0, emptied = 0, recol = 0;
  for (let y = 0; y < H; y++) {
    let s = '', c = '';
    for (let x = 0; x < W; x++) {
      const i = y * W + x, a = full.d[i] >>> 0, b = r.body.d[i] >>> 0;
      let ch = '.'; if (a >>> 24) { if (!(b >>> 24)) { ch = '2'; emptied++; changed++; } else if (a !== b) { ch = '1'; recol++; changed++; } else ch = '0'; } else if (b >>> 24) { ch = '3'; changed++; }
      s += ch; c += r.capMask[i] ? '#' : '.';
    }
    rows.push(s); chunkRows.push(c);
  }
  const parts = r.parts.map((p) => { let n = 0; for (let i = 0; i < p.grid.d.length; i++) if (p.grid.d[i] >>> 24) n++; return { x: p.x, y: p.y, w: p.grid.w, h: p.grid.h, pixels: n, gridHash: hex8(fnv(p.grid.d)), out: p.out.map(r6), edgePoints: p.pts.length }; });
  return {
    stateRows: rows,                                   // '.' empty before & after, '0' untouched, '1' solid with a changed colour (cut surface / wound recolour / dark gore), '2' emptied (was solid, now see-through), '3' empty became solid (never expected)
    changedPixels: changed, emptiedPixels: emptied, recolouredPixels: recol,
    chunkRows, chunkPixels: r.capCount, chunkCentroid: r.capCentroid.map(r6),
    partSizesSorted: parts.map((p) => p.pixels).sort((a, b) => b - a), parts,
    gibCount: r.gibs.length, gibs: r.gibs.map((g) => [g.x, g.y, g.col >>> 0]),
    outAtt: r.outAtt.map(r6), attPointCount: r.attPts.length, attPointsHash: hex8(fnv(Uint32Array.from(r.attPts.flatMap((p) => [p.ix, p.iy, Math.round(p.nx * 1e4) + 20000, Math.round(p.ny * 1e4) + 20000])))),
    bodyHash: hex8(fnv(r.body.d)), bodyRle: rle(r.body.d), errors: r.errors || [], missing: !!r.missing,
  };
};

/* ------------------------------------------------------------------ the runners (each case stores its inputs verbatim; the runner only reads the stored inputs) */
const setOf = (a) => (a && a.length ? new Set(a) : null);
const runCutHead3D = (inp) => {
  const [sx, sy] = inp.place, spr = spriteGrid(inp.sprite), full = GL.placeGrid(spr, inp.canvas.W, inp.canvas.H, sx, sy);
  const r = GL.cutHead3D(full, inp.tag, sx, sy, !!inp.mirror, inp.ops, inp.cfg, setOf(inp.behindCanvas), setOf(inp.exemptCanvas), inp.lastG);
  return describe(r, full, inp.canvas.W, inp.canvas.H);
};
const frameOf = (fi) => ({ spr: spriteGrid(fi.sprite), head: fi.head || null, torso: fi.torso || null, noHead: !!fi.noHead, mirror: !!fi.mirror, ox: fi.ox || 0, oy: fi.oy || 0, behind: new Set(fi.behind || []), exempt: new Set(fi.exempt || []), tm: { behind: new Set(fi.tbehind || []), exempt: new Set(fi.texempt || []) } });
const runCutFrame = (inp) => {
  const f = frameOf(inp.frame), [sx, sy] = inp.place, full = GL.placeGrid(f.spr, inp.canvas.W, inp.canvas.H, sx, sy);
  return describe(GL.cutFrame(f, full, sx, sy, inp.canvas.W, inp.canvas.H, inp.ops, inp.cfg), full, inp.canvas.W, inp.canvas.H);
};
const runDeathBake = (inp) => {
  const set = { frames: inp.frames.map(frameOf), hit: inp.hit, ops: inp.ops };
  const b = GL.deathBake(set, inp.canvas.W, inp.canvas.H, Object.assign({ floorMargin: inp.canvas.floorMargin }, inp.cfg));
  return {
    floorY: b.floorY, errors: b.errors,
    frames: b.frames.map((rec) => ({
      placedAt: [rec.sx, rec.sy], hasHeadless: !!rec.headless, missing: rec.missing, changedPixels: rec.changed === undefined ? null : rec.changed,
      bodyHash: rec.headless ? hex8(fnv(rec.headless.d)) : null, partSizesSorted: rec.parts ? rec.parts.map((p) => { let n = 0; for (const v of p.grid.d) if (v >>> 24) n++; return n; }).sort((a, c) => c - a) : null,
      gibCount: rec.gibs.length, stumpPointCount: rec.stumpPts ? rec.stumpPts.length : null, woundCentroid: rec.at ? rec.at.map(r6) : null,
      neck: rec.neck ? { x: r6(rec.neck.x), y: r6(rec.neck.y), nx: r6(rec.neck.nx), ny: r6(rec.neck.ny) } : null,
    })),
  };
};
const scorers = { none: null, preferEast: (op) => Math.max(0, Math.min(1, (op.a[0] + 1) / 2)) };

const cases = [];
const add = (id, kind, description, input, run, extra = {}) => { const inp = clone(input); cases.push({ id, kind, description, input: inp, expected: run(inp), ...extra }); return cases[cases.length - 1]; };
const baseCut = (extra) => Object.assign({ sprite: 'imp24', canvas: CANVAS, place: PLACE, cfg: CFG }, extra);

/* --- 1. ellipsoid head + plane (neck plane: normal = up, d = -0.8 as Remove head makes it) */
const neck = { t: 'plane', m: 'head', N: [0, 1, 0], d: -0.8, g: 0 };
const c1 = add('head_plane_neck', 'cutHead3D', 'Front head (ellipsoid) cut by the neck plane N=(0,1,0), d=-0.8 (what Remove head issues). Ragged edge on (jag 1.2). The plane removes the side N.p >= d, i.e. everything above the neck line: that is the head, which flies (chunk).', baseCut({ tag: HEAD(), ops: [neck] }), runCutHead3D);
if (!(c1.expected.chunkPixels > 20 && c1.expected.partSizesSorted.length >= 1)) fail('neck chunk missing');
/* --- 1b. same without jag, to isolate the geometry */
add('head_plane_neck_nojag', 'cutHead3D', 'Same as head_plane_neck with cfg.jag = 0 (perfectly straight cut); isolates the geometry from the noise.', baseCut({ tag: HEAD(), ops: [neck], cfg: Object.assign({}, CFG, { jag: 0 }) }), runCutHead3D);
/* --- 1c. tilted slice through the head, wound bone off */
const tilt = (() => { const a = 0.6, N = [Math.cos(a), Math.sin(a), 0]; return { t: 'plane', m: 'head', N, d: 0.1, g: 0 }; })();
add('head_plane_tilted_nobone', 'cutHead3D', 'Front head cut by a tilted plane N=(cos0.6,sin0.6,0), d=0.1, cfg.bone=false (no bone core in the wound colouring).', baseCut({ tag: HEAD(), ops: [tilt], cfg: Object.assign({}, CFG, { bone: false }) }), runCutHead3D);
/* --- 2. box torso + plane */
const tplane = (() => { const a = -0.35, N = [Math.sin(a), Math.cos(a), 0]; const l = Math.hypot(...N); return { t: 'plane', m: 'torso', N: N.map((v) => v / l), d: 0.15, g: 0 }; })();
const c2 = add('torso_box_plane', 'cutHead3D', 'Box torso (superellipsoid n=4, rz=4) cut by a slightly tilted horizontal plane, d=0.15.', baseCut({ tag: TORSO(), ops: [tplane] }), runCutHead3D);
if (!(c2.expected.chunkPixels > 10)) fail('torso plane chunk missing');
/* --- 3. mirrored head, same plane: east-side wound visible on one profile, hidden on the other */
const profile = HEAD({ f3: [-1, 0, 0], c: [11, 6] }), profileM = GL.mirrorHead(profile, 24);
const eastPlane = { t: 'plane', m: 'head', N: [1, 0, 0], d: 0.25, g: 0 };
const m1 = add('mirror_profile_drawn', 'cutHead3D', 'Drawn profile: face points to screen-left (forward=(-1,0,0)); its east axis (up x forward) points AWAY from the viewer. Plane removes the east side (N=(1,0,0), d=0.25): the wound is on the far side, so it must be INVISIBLE (changedPixels = 0).', baseCut({ tag: profile, ops: [eastPlane] }), runCutHead3D, { assert: 'changedPixels == 0' });
const m2 = add('mirror_profile_mirrored', 'cutHead3D', 'Mirrored direction: sprite = GL.flipGrid of the drawn one, tag = GL.mirrorHead(tag, 24): forward=(+1,0,0), so east points TOWARD the viewer. The SAME plane (wound is NOT mirrored) is now visible (changedPixels > 0).', baseCut({ sprite: 'imp24_flipped', tag: profileM, mirror: true, ops: [eastPlane] }), runCutHead3D, { assert: 'changedPixels > 0' });
if (m1.expected.changedPixels !== 0 || !(m2.expected.changedPixels > 8)) fail(`mirror visibility ${m1.expected.changedPixels}/${m2.expected.changedPixels}`);
cases.push({ id: 'mirror_head_derivation', kind: 'mirrorHead', description: 'GL.mirrorHead(tag, w=24) of the drawn profile tag, and of a torso tag; shows which fields flip (c.x -> w - c.x, a -> PI - a, u3.x and f3.x negate; n, rx, ry, rz, kind unchanged).', input: { w: 24, head: profile, torso: TORSO({ f3: [-0.7071, 0, 0.7071], c: [11, 16.5] }) }, expected: null });
{ const k = cases[cases.length - 1]; k.expected = { head: GL.mirrorHead(k.input.head, 24), torso: GL.mirrorHead(k.input.torso, 24) }; }
/* --- 4. capsule pellets: one through the head along the view axis (shallow crater), one oblique through the torso (a tunnel exiting at the side) */
const cap1 = { t: 'cap', m: 'head', a: [0.1, 0.0, 1.6], b: [0.1, 0.0, 0.15], r: 0.28, g: 0 };
const c4 = add('head_capsule_shallow', 'cutHead3D', 'One capsule (pellet) dug from the viewer side into the head to depth z=0.15 (not through): dark crater, nothing flies except the hole contents.', baseCut({ tag: HEAD(), ops: [cap1] }), runCutHead3D);
const cap2 = { t: 'cap', m: 'torso', a: [-1.2, 0.2, 1.8], b: [1.2, 0.3, -1.8], r: 0.3, g: 0 };
add('torso_capsule_through', 'cutHead3D', 'One oblique capsule tunnelling straight through the torso box from west to east (enters and exits): crater-coloured wound pixels where it exits the front surface (and see-through pixels wherever the tunnel clears the whole depth).', baseCut({ tag: TORSO(), ops: [cap2] }), runCutHead3D);
add('head_capsule_degenerate', 'cutHead3D', 'Capsule with a == b (zero length, u = null): pure sphere remover of radius 0.35 centred at depth 0.9 (front).', baseCut({ tag: HEAD(), ops: [{ t: 'cap', m: 'head', a: [0, 0, 0.9], b: [0, 0, 0.9], r: 0.35, g: 0 }] }), runCutHead3D);
/* --- 5. op generators with fixed seeds */
const sx0 = PLACE[0], sy0 = PLACE[1];
cases.push({ id: 'planeFromSwipe_cases', kind: 'planeFromSwipe', description: 'GL.planeFromSwipe(tag, sx, sy, mirror, p0, p1, flipInvert): swipe on canvas coordinates over the front head. Covers: horizontal swipe through the head (the side WITHOUT the neck flies), the same with flipInvert, a diagonal swipe, a swipe too short (null), a swipe that misses the ball (|d| >= 0.98 -> null), and a swipe through the neck point (|neckSide| <= 0.03 falls back to the normal-y rule).', input: { tag: HEAD(), sx: sx0, sy: sy0, cases: [
  { p0: [8, 12], p1: [24, 12], flipInvert: false }, { p0: [8, 12], p1: [24, 12], flipInvert: true }, { p0: [24, 12], p1: [8, 12], flipInvert: false }, { p0: [10, 8], p1: [20, 18], flipInvert: false },
  { p0: [10, 12], p1: [11, 12.5], flipInvert: false }, { p0: [8, 3], p1: [24, 3], flipInvert: false }, { p0: [8, 17.99], p1: [24, 17.99], flipInvert: false }, { p0: [9, 16.5], p1: [22, 16.5], flipInvert: false }, { p0: [16, 4], p1: [16, 20], flipInvert: false }, { p0: [16, 20], p1: [16, 4], flipInvert: true }] }, expected: null });
{ const k = cases[cases.length - 1], i = k.input; k.expected = i.cases.map((c) => GL.planeFromSwipe(i.tag, i.sx, i.sy, false, c.p0, c.p1, c.flipInvert)); }
cases.push({ id: 'shotFromSwipe_seed7', kind: 'shotFromSwipe', description: 'GL.shotFromSwipe(tag, sx, sy, p0, p1, cfg, seed): 12 pellets from a muzzle left of the head, aimed at it, cone 14 degrees, seed 7 (deterministic: GL.rng(7) draws in the order gauss(4 draws), R() for energy, R() for depth, per pellet). Misses are skipped.', input: { tag: HEAD(), sx: sx0, sy: sy0, p0: [2, 12], p1: [16, 12], seed: 7, cfg: { pellets: 12, coneDeg: 14, energy: 7, radius: 1.3, toughness: 0.9, rangeFalloff: 0.3 } }, expected: null });
{ const k = cases[cases.length - 1], i = k.input; k.expected = GL.shotFromSwipe(i.tag, i.sx, i.sy, i.p0, i.p1, i.cfg, i.seed); if (k.expected.length < 6) fail('too few pellets'); }
const addStraight = (id, descr, extra) => {
  const inp = Object.assign({ sprite: 'imp24', canvas: CANVAS, place: PLACE, tag: HEAD(), p0: [1, 12], p1: [16, 11], seed: 12345, count: 1, scorer: 'none', cfg: { coneDeg: 12, energy: 7, radius: 1.3, toughness: 0.9, rangeFalloff: 0.3, straightDepth: 0.4 } }, extra);
  const k = { id, kind: 'straightShot', description: descr, input: inp, expected: null };
  const i = clone(inp), spr = spriteGrid(i.sprite), full = GL.placeGrid(spr, i.canvas.W, i.canvas.H, i.place[0], i.place[1]);
  k.expected = GL.straightShot(i.tag, i.place[0], i.place[1], i.p0, i.p1, i.cfg, i.seed, full, i.count, scorers[i.scorer]); cases.push(k); return k;
};
const s1 = addStraight('straightShot_single_seed12345', 'One straight-on bullet: random point along the aim line over visible solid head pixels (scan t=0..300 step 0.5), capsule dug from the viewer-facing surface depth zf+3 to zf-max(0.6,pen). No scorer: the first drawn point is kept.', {});
if (s1.expected.length !== 1) fail('straightShot count');
addStraight('straightShot_scored_east', 'Same line, seed 99, 6 pellets (cone), with the test scorer preferEast(op) = clamp((op.a[0]+1)/2, 0, 1): up to 10 candidate points per pellet are tried, the best kept, stopping early at score 1.', { seed: 99, count: 6, scorer: 'preferEast' });
addStraight('straightShot_torso', 'Straight-on bullet at the torso box (tag kind box).', { tag: TORSO(), p0: [1, 16], p1: [18, 17], seed: 4242 });
/* --- 5b. Cut (knife gash): a generator of shallow capsule removers, then applied with the ordinary engine */
{
  for (const [id, descr, p0, p1, seed] of [['gash_short_torso', 'Short knife swipe (about 6.7 px) over the box torso, seed 31: chain of shallow capsules every 0.8 px along the swipe, wobbling sideways and in depth, with occasional breaks.', [12, 20], [18, 23], 31], ['gash_long_torso', 'Long diagonal knife swipe (about 20 px) crossing the whole torso, seed 5.', [6, 16], [27, 28], 5]]) {
    const inp = { sprite: 'imp24', canvas: CANVAS, place: PLACE, tag: TORSO(), p0, p1, seed, cfg: { gashDepth: 1.6, gashWidth: 0.8, gashWobble: 0.9 } };
    const i = clone(inp), full = GL.placeGrid(spriteGrid(i.sprite), i.canvas.W, i.canvas.H, i.place[0], i.place[1]);
    const ops = GL.gashFromSwipe(i.tag, i.place[0], i.place[1], i.p0, i.p1, i.cfg, i.seed, full);
    if (ops.length < 3) fail(id + ' produced too few capsules');
    cases.push({ id, kind: 'gashFromSwipe', description: descr, input: inp, expected: ops });
    add(id + '_applied', 'cutHead3D', `The capsules of ${id} applied to the torso as one group (g=0, m=torso): shallow grooves; the engine needed no change for this damage type.`, baseCut({ tag: TORSO(), ops: ops.map((o) => Object.assign({ m: 'torso', g: 0 }, o)) }), runCutHead3D);
  }
}
/* applying the straight shot ops: a full cut with them */
{
  const ops = cases.find((c) => c.id === 'straightShot_scored_east').expected.map((o) => Object.assign({ m: 'head', g: 0 }, o));
  add('head_straight_bullets_applied', 'cutHead3D', 'The six scored straight-on ops of straightShot_scored_east applied to the head as one group (g=0). Shows shallow crater wounds and the back-direction (debris leaves toward the shooter) bookkeeping in outAtt.', baseCut({ tag: HEAD(), ops }), runCutHead3D);
}
/* --- 6. stacked groups: second slice, only the newest group's pixels fly */
const sl1 = { t: 'plane', m: 'head', N: [0, 1, 0], d: 0.35, g: 0 }, sl2 = { t: 'plane', m: 'head', N: [1, 0, 0], d: 0.3, g: 1 };
const st1 = add('head_stacked_first', 'cutHead3D', 'Head with group 0 only: a horizontal slice through the upper head (d=0.35).', baseCut({ tag: HEAD(), ops: [sl1] }), runCutHead3D);
const st2 = add('head_stacked_second', 'cutHead3D', 'Head with groups 0 and 1: the first slice plus a vertical east slice (N=(1,0,0), d=0.3). The body carries BOTH cuts; the chunk/parts/gibs contain only the pixels group 1 changed (pixels already gone after group 0 are not part of the chunk).', baseCut({ tag: HEAD(), ops: [sl1, sl2] }), runCutHead3D);
if (!(st2.expected.chunkPixels > 0 && st2.expected.chunkPixels < st2.expected.emptiedPixels + st2.expected.recolouredPixels)) fail('stacked chunk is not a strict subset');
add('head_stacked_explicit_lastG', 'cutHead3D', 'Same ops as head_stacked_second but lastGIn = 0 passed explicitly (cutFrame passes the maximum g over ALL members): the group treated as newest is then group 0, so group 1 counts as "old" and the chunk is what group 0 added on top of group 1.', baseCut({ tag: HEAD(), ops: [sl1, sl2], lastG: 0 }), runCutHead3D);
/* --- 7. torso + head together: head ownership via cutFrame */
const frameBoth = { sprite: 'imp24', head: HEAD(), torso: TORSO() };
const tUpper = { t: 'plane', m: 'torso', N: [0, 1, 0], d: 0.55, g: 0 };
const o1 = add('frame_torso_slice_head_owned', 'cutFrame', 'cutFrame on a frame with head AND torso tags. The torso slice (N=(0,1,0), d=0.55: removes the top of the torso box, which overlaps the head ellipse around y 10..11.5): pixels inside the head shape belong to the head and are excluded from the torso cut (head ownership).', baseCut({ frame: frameBoth, ops: [tUpper] }), runCutFrame);
add('frame_torso_slice_no_head_tag', 'cutFrame', 'Same torso slice on the same frame but with no head tag at all (and noHead false, torso-only cut): no ownership exclusion, so the overlap pixels ARE cut. Compare with frame_torso_slice_head_owned.', baseCut({ frame: { sprite: 'imp24', torso: TORSO() }, ops: [tUpper] }), runCutFrame);
add('frame_head_and_torso_slices', 'cutFrame', 'One group (g=0) holding a head slice and a torso slice: both members are cut and merged; the torso result wins where it changed pixels, the head owns the overlap; parts of both members are listed together sorted by bounding-box area (largest first).', baseCut({ frame: frameBoth, ops: [{ t: 'plane', m: 'head', N: [0, 1, 0], d: 0.2, g: 0 }, { t: 'plane', m: 'torso', N: [0, 1, 0], d: 0.0, g: 0 }] }), runCutFrame);
add('frame_older_group_member_untouched', 'cutFrame', 'Head ops are group 0, torso ops group 1. cutFrame computes lastG = 1 over everything, so the HEAD member (all its ops older than lastG) is evaluated but contributes no chunk/parts; only the torso group-1 pixels fly. The head body still carries its group-0 wound.', baseCut({ frame: frameBoth, ops: [{ t: 'plane', m: 'head', N: [0, 1, 0], d: 0.3, g: 0 }, { t: 'plane', m: 'torso', N: [0, 1, 0], d: 0.2, g: 1 }] }), runCutFrame);
/* --- 8. paint masks (sprite-local pixel indices; index = y*24 + x) */
const behindIdx = [], exemptIdx = [];
for (let y = 0; y < 24; y++) for (let x = 0; x < 24; x++) { const i = y * 24 + x; if (!(imp.d[i] >>> 24)) continue; if (y >= 4 && y <= 8 && x >= 9 && x <= 14) behindIdx.push(i); if (y >= 5 && y <= 7 && x >= 9 && x <= 11) exemptIdx.push(i); }
const maskPlane = { t: 'plane', m: 'head', N: [0, 1, 0], d: -0.6, g: 0 };
const b1 = add('frame_behind_mask', 'cutFrame', "Head neck-ish slice (N=(0,1,0), d=-0.6) with a 'behind' paint mask over the middle of the head (sprite-local indices listed in the case): removed pixels that are in the mask stay on the body as the ORIGINAL colour mixed toward dark gore rgb(78,4,8) by 0.72 + 0.2 * hash(keyLow, keyHigh, 5) (shown as '1' in stateRows instead of '2').", baseCut({ frame: { sprite: 'imp24', head: HEAD(), behind: behindIdx }, ops: [maskPlane] }), runCutFrame);
const b0 = add('frame_no_masks_reference', 'cutFrame', 'Reference for the mask cases: the same slice with no masks (behind pixels become see-through).', baseCut({ frame: { sprite: 'imp24', head: HEAD() }, ops: [maskPlane] }), runCutFrame);
if (!(b1.expected.emptiedPixels < b0.expected.emptiedPixels)) fail('behind mask had no effect');
const e1 = add('frame_exempt_mask', 'cutFrame', "Same slice with an 'exempt' (in-front) mask of pixels x9..11,y5..7 (sprite-local): exempt pixels are never cut, never recoloured, never part of the chunk and never joined to the chunk's connected components.", baseCut({ frame: { sprite: 'imp24', head: HEAD(), exempt: exemptIdx }, ops: [maskPlane] }), runCutFrame);
if (!(e1.expected.chunkPixels < b0.expected.chunkPixels)) fail('exempt mask had no effect');
add('frame_torso_masks', 'cutFrame', "Torso masks ride in tbehind / texempt (the head's in behind / exempt); here a torso slice with a texempt patch (the same 9 pixels) and a tbehind patch.", baseCut({ frame: { sprite: 'imp24', torso: TORSO(), tbehind: behindIdx.filter((i) => i >= 24 * 12), texempt: exemptIdx }, ops: [{ t: 'plane', m: 'torso', N: [0, 1, 0], d: 0.0, g: 0 }] }), runCutFrame);
/* --- 9. noHead frame and missing-tag frame */
const nh = add('frame_noHead', 'cutFrame', "A frame flagged noHead (the lying death frame): head ops are skipped without error and the result is the intact frame (changedPixels 0, errors empty, missing false).", baseCut({ frame: { sprite: 'imp24', noHead: true }, ops: [neck] }), runCutFrame);
if (nh.expected.changedPixels !== 0 || nh.expected.missing) fail('noHead');
const ms = add('frame_missing_tag', 'cutFrame', "A frame with NO head tag (and noHead false) asked to cut the head: 'not set up' -> returns the intact frame with missing = true and no chunk.", baseCut({ frame: { sprite: 'imp24' }, ops: [neck] }), runCutFrame);
if (!ms.expected.missing) fail('missing');
add('frame_head_ops_torso_tag_only', 'cutFrame', 'Head ops on a frame that has only a torso tag: missing (head member required).', baseCut({ frame: { sprite: 'imp24', torso: TORSO() }, ops: [neck] }), runCutFrame);
add('frame_no_ops', 'cutFrame', 'No removers at all: no members, intact frame, not missing.', baseCut({ frame: frameBoth, ops: [] }), runCutFrame);
/* --- 10. death bake across several frames incl. noHead and missing */
const fr = (extra) => Object.assign({ sprite: 'imp24' }, extra);
add('deathBake_flow', 'deathBake', 'GL.deathBake: 4 frames, hit = 1 (frame 0 is never cut: headless = null). Frame 1 head+torso tagged, frame 2 is noHead (intact copy), frame 3 has no head tag (missing). Frames carry ox/oy nudges (frame 1 ox=1, oy=-1) which only move the placement. Ops: the neck plane (g 0).', { canvas: CANVAS, cfg: CFG, hit: 1, ops: [neck], frames: [fr({ head: HEAD() }), fr({ head: HEAD(), torso: TORSO(), ox: 1, oy: -1 }), fr({ noHead: true }), fr({ torso: TORSO() })] }, runDeathBake);
add('deathBake_no_ops_error', 'deathBake', 'No ops: every frame from the hit frame on reports "Frame N needs a cut: draw a slice in Play" in errors.', { canvas: CANVAS, cfg: CFG, hit: 0, ops: [], frames: [fr({ head: HEAD() }), fr({ head: HEAD() })] }, runDeathBake);
/* --- 11. walk mirror derivation (what syncMirror does) */
{
  const w = 24, flipIdx = (a) => a.map((i) => ((i / w) | 0) * w + (w - 1 - (i % w)));
  const src = { head: HEAD({ f3: [-0.7071, 0, 0.7071] }), torso: TORSO({ f3: [-0.7071, 0, 0.7071] }), ox: 2, oy: 1, noHead: false, behind: behindIdx, exempt: exemptIdx, tbehind: [], texempt: [] };
  const mir = { head: GL.mirrorHead(src.head, w), torso: GL.mirrorHead(src.torso, w), ox: -src.ox, oy: src.oy, noHead: src.noHead, behind: flipIdx(src.behind), exempt: flipIdx(src.exempt), tbehind: flipIdx(src.tbehind), texempt: flipIdx(src.texempt) };
  cases.push({ id: 'mirror_frame_derivation', kind: 'mirrorFrame', description: "Deriving a mirrored walk direction's frame from its drawn partner (deathlab syncMirror): tags through mirrorHead(tag, spriteWidth), ox negated, oy kept, every mask index (x,y) -> (w-1-x, y), sprite = flipGrid(cleaned sprite). The wound plane itself is NOT mirrored.", input: { spriteWidth: w, source: src }, expected: mir });
  const sf = frameOf(Object.assign({ sprite: 'imp24' }, src)); sf.mirror = false;
  const mf = frameOf(Object.assign({ sprite: 'imp24_flipped' }, mir)); mf.mirror = true;
  const pl = { t: 'plane', m: 'head', N: [1, 0, 0], d: 0.2, g: 0 };
  for (const [nm, f] of [['drawn', sf], ['mirrored', mf]]) { const [sx, sy] = GL.placeAt(f.spr, 32, 32, 30, f.ox, f.oy), full = GL.placeGrid(f.spr, 32, 32, sx, sy); const r = GL.cutFrame(f, full, sx, sy, 32, 32, [pl], CFG);
    cases.push({ id: 'mirror_frame_cut_' + nm, kind: 'cutFrame', description: `East-side slice on the ${nm} three-quarter frame (forward=(-0.7071,0,0.7071) drawn, mirrored forward x negated). Placement uses ox/oy (placeAt).`, input: { sprite: nm === 'drawn' ? 'imp24' : 'imp24_flipped', canvas: CANVAS, place: [sx, sy], cfg: CFG, ops: [pl], frame: nm === 'drawn' ? Object.assign({ sprite: 'imp24' }, src) : Object.assign({ sprite: 'imp24_flipped' }, mir) }, expected: describe(r, full, 32, 32) }); }
}
/* --- 12. solid intersection vectors */
cases.push({ id: 'solid_intersections', kind: 'makeSolid', description: 'The line-of-sight/solid intersection (zf = depth of the front surface toward the viewer, zb = back surface, Pf = front surface point, in unit coordinates). For O given in head coordinates at depth 0 (O = unit-space position of the pixel centre with z = 0). Ellipsoid (head, tilted), torso box n=4 (upright), a rotated box n=3.5, plus MISS rows (closest-approach fallback: zf = zb).', input: { tags: { head: HEAD({ u3: GL.normHead({ u3: [0.2, -0.95, 0.2], f3: [0, 0, 1], c: [0, 0], rx: 1, ry: 1 }).u3, f3: [0.1, 0.2, 0.95] }), torso: TORSO(), torso_rot: TORSO({ n: 3.5, u3: [0.3, -0.9, 0.3], f3: [-0.2, 0.3, 0.9], rx: 5, ry: 7, rz: 3 }) },
  O: [[0, 0, 0], [0.3, -0.4, 0], [-0.7, 0.2, 0], [0.9, 0.9, 0], [1.2, 0, 0], [0, 1.5, 0], [0.5, 0.5, 0], [-0.95, -0.2, 0], [0.99, 0.0, 0], [0.2, -0.99, 0], [1.05, 0.2, 0], [-0.4, 0.8, 0]] }, expected: null });
{ const k = cases[cases.length - 1], i = k.input; k.expected = {}; for (const name of Object.keys(i.tags)) { const h = i.tags[name], B = GL.headBasis(h), solid = GL._makeSolid(h, B); k.expected[name] = i.O.map((O) => { const r = solid(O); return { zf: r.zf, zb: r.zb, Pf: r.Pf }; }); } }
/* --- 13. blood cleanup */
cases.push({ id: 'cleanBlood_head', kind: 'cleanBlood', description: "GL.cleanBlood(sprite, headTag, 'head', skip=null): a pixel is blood when alpha>0, r>=60, g<0.35r, b<0.35r. Inside the head shape grown (centre moved 0.35*ry along up, rx*1.3, ry*1.5) bloody pixels are repainted from their clean 8-neighbours (hash-picked), working inward in up to 12 passes (>=2 clean neighbours for the first 6 passes, >=1 afterwards); leftovers are deleted. The lone speck at (20,3) is OUTSIDE the grown shape and therefore kept (mode 'head').", input: { sprite: 'imp24_bloody', tag: HEAD(), mode: 'head' }, expected: null });
{ const k = cases[cases.length - 1], i = k.input, g = spriteGrid(i.sprite), o = GL.cleanBlood(g, i.tag, i.mode, null); k.expected = { hash: hex8(fnv(o.d)), rle: rle(o.d) };
  const k2 = { id: 'cleanBlood_all', kind: 'cleanBlood', description: "Same sprite, mode 'all': the lone speck is also removed (no clean neighbour => deleted).", input: { sprite: 'imp24_bloody', tag: HEAD(), mode: 'all' }, expected: null }; const o2 = GL.cleanBlood(g, k2.input.tag, 'all', null); k2.expected = { hash: hex8(fnv(o2.d)), rle: rle(o2.d) }; cases.push(k2); }
/* --- 14. normHead */
cases.push({ id: 'normHead_samples', kind: 'normHead', description: "GL.normHead on raw saved tags: defaults n=2, r -> rx=ry, u3 derived from the angle a when absent, f3 default (0,0,1), f3 made perpendicular to u3 (fixFwd), a re-derived from u3 if |u3.xy| > 0.2.", input: [{ c: [5, 5], r: 4, a: -1.2 }, { c: [5, 5], rx: 3, ry: 4, a: 0.5, u3: [0.5, -0.5, 0.7], f3: [0, 0, 1] }, { c: [5, 5], rx: 3, ry: 4, n: 3, u3: [0, 0, 1], f3: [0, 0, 1], a: 1 }, { kind: 'box', c: [9, 9], rx: 6, ry: 6, rz: 4, n: 4, a: -1.5707963267948966, u3: [0, -1, 0], f3: [0, 0, -1] }], expected: null });
{ const k = cases[cases.length - 1]; k.expected = clone(k.input).map((t) => GL.normHead(t)); }
/* --- 15. raw primitives */
const prim = { hash: [], rng: [], vnoise3: [], vnoise1: [], mix: [] };
{
  const hin = [[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1], [1, 2, 3], [-1, -2, -3], [500, 500, 21], [12, 34, 56], [1000, -1000, 1], [65535, 65536, 7], [2147483647, -2147483648, 3], [3.7, 4.2, 5.9], [-3.7, 4.2, -5.9], [131, 262, 393], [40, 41, 12], [7, 8, 13], [99999, 3, 5], [123456789, 987654321, 555], [0, 0, 77], [5, 5, 5], [9, 9, 9], [-500, 500, 21], [17, -17, 0], [255, 255, 255], [256, 256, 256], [1e6, 1e6, 1e6], [-1, -1, -1], [2, 4, 8], [3, 6, 9], [100, 200, 300], [8, 8, 2], [27, 64, 125], [14, 15, 16], [33, 33, 33], [1, 1, 1], [0, 1, 1], [1, 0, 1], [10, 20, 30], [-10, -20, -30], [42, 42, 42]];
  for (const [x, y, s] of hin) { const v = GL.hash(x, y, s); prim.hash.push({ x, y, s, value: v, uint32: Math.round(v * 4294967296) }); }
  const seeds = [0, 1, 7, 12345, 4294967295, 2147483648, 1.5e9 | 0, -1];
  for (const sd of seeds) { const R = GL.rng(sd), vals = []; for (let k = 0; k < 6; k++) { const v = R(); vals.push({ value: v, uint32: Math.round(v * 4294967296) }); } prim.rng.push({ seed: sd, seedAsUint32: sd >>> 0, first6: vals }); }
  const vin = []; for (let k = 0; k < 40; k++) { const R = GL.rng(1000 + k); vin.push([(R() - 0.5) * 40, (R() - 0.5) * 40, (R() - 0.5) * 40, Math.floor(R() * 100000) - 20000]); }
  vin.push([0, 0, 0, 0], [1, 1, 1, 1], [0.5, 0.5, 0.5, 3]);
  for (const [x, y, z, s] of vin) prim.vnoise3.push({ x, y, z, seed: s, value: GL._vnoise3(x, y, z, s) });
  for (const [x, s] of [[0, 0], [0.5, 1], [3.14, 3], [-2.2, 9], [10.75, 7]]) prim.vnoise1.push({ x, seed: s, value: GL.vnoise1(x, s) });
  for (const [a, b, t] of [[GL.rgb(10, 20, 30), GL.rgb(250, 240, 230), 0.5], [GL.rgb(150, 22, 22), GL.rgb(92, 6, 10), 0.45], [GL.rgb(100, 90, 80), GL.rgb(78, 4, 8), 0.8], [GL.rgb(255, 255, 255), GL.rgb(0, 0, 0), 0.999]]) prim.mix.push({ c1: a >>> 0, c2: b >>> 0, t, result: GL.mix(a, b, t) >>> 0 });
}
cases.push({ id: 'primitives', kind: 'primitives', description: 'Raw values: hash (40 inputs), rng (8 seeds x first 6 draws), vnoise3 (43 inputs), vnoise1 (5), mix (4). hash/rng values are exact doubles = uint32 / 4294967296; vnoise3/mix follow from them.', input: null, expected: prim });

/* ------------------------------------------------------------------ write */
const palette = { blood: GL.PAL.blood.map((v) => v >>> 0), flesh: GL.PAL.flesh.map((v) => v >>> 0), bone: GL.PAL.bone.map((v) => v >>> 0), scorch: GL.PAL.scorch >>> 0, crater: GL.rgb(92, 6, 10) >>> 0, darkGore: GL.rgb(78, 4, 8) >>> 0 };
const out = {
  README: {
    what: 'Golden test vectors for the C#/Unity port of the GoreLab 3D cut engine. Every EXPECTED value was produced by running the prototype JavaScript, never by hand.',
    producedBy: 'D:\\Unity\\Laubrary Dev - GoreLab\\GORELAB_GOLDEN_GENERATOR.js (node vm harness; loads D:\\CODEZ\\GoreLab\\web\\sim.js, cut.js, death.js, cut3d.js; run: node GORELAB_GOLDEN_GENERATOR.js).',
    generatedOnNode: process.version,
    colourFormat: 'All colours are uint32 0xAABBGGRR as numbers (red in the lowest byte, alpha 255 in the top byte; alpha 0 = empty). rle arrays are [runLength, colour] pairs in row-major order (index = y*width + x).',
    hash32: 'bodyHash/gridHash/hash = FNV-1a 32-bit over the uint32 array, each element fed as 4 bytes LITTLE-ENDIAN (offset basis 0x811c9dc5, prime 0x01000193), printed as 8 hex digits.',
    canvas: 'Cases use a 32x32 canvas with the 24x24 synthetic sprite placed at (4,6) = placeAt(sprite, 32, 32, floorY = 32 - floorMargin(2), ox, oy). stateRows are canvas rows.',
    stateLetters: "'.' empty before and after; '0' untouched solid; '1' solid whose colour changed (cut surface, wound recolour of the rim, dark gore of a 'behind' pixel); '2' was solid, now see-through; '3' empty became solid (never expected).",
    tags: 'Tags are stored AFTER GL.normHead (u3/f3 present, f3 perpendicular to u3, n default). Tag fields: c [x,y] sprite-local, rx, ry, rz (box only), n, kind ("box" for torso), a (2D angle of up), u3, f3 (unit vectors, x right, y down, z toward viewer).',
    ops: 'Removers: plane {t:"plane", N:[east,up,forward], d, g, m} removes the side N.p >= d; capsule {t:"cap", a, b, r, g, m, back?}; g = group number (the highest g is the "newest" group whose changed pixels fly), m = member ("head" default | "torso").',
    masks: 'Case masks are sprite-local pixel indices (y*spriteWidth + x) for cutFrame/deathBake cases (behind/exempt = head; tbehind/texempt = torso) and CANVAS indices for the direct cutHead3D cases (behindCanvas/exemptCanvas).',
    tolerances: 'Integer outputs (stateRows, chunkRows, counts, part sizes, hashes, colours) are expected to match EXACTLY. Floating outputs (centroids, intersection depths, ops, noise) should match to 1e-9 relative except where a transcendental (Math.pow/sin/cos/atan2) is involved - allow 1e-6 there, and report any integer mismatch: a borderline pixel flipping because of 1-ulp differences is possible in principle and must then be investigated, not tolerated silently.',
    cfgFieldsUsed: { cutHead3D: ['seed', 'jag', 'jagFreq', 'bone'], cutFrame: 'same as cutHead3D', deathBake: ['floorMargin'], shotFromSwipe: ['pellets', 'coneDeg', 'energy', 'radius', 'toughness', 'rangeFalloff'], gashFromSwipe: ['gashDepth', 'gashWidth', 'gashWobble'], straightShot: ['coneDeg', 'energy', 'radius', 'toughness', 'rangeFalloff', 'straightDepth'] },
    cfgDefaultsFromAppJs: { seed: 1, jag: 1.2, jagFreq: 0.45, bone: true, neckDepth: 0.8, pellets: 30, coneDeg: 12, energy: 7, radius: 1.3, toughness: 0.9, rangeFalloff: 0.3, straightOn: false, straightDepth: 0.4, gashDepth: 1.6, gashWidth: 0.8, gashWobble: 0.9, floorMargin: 3 },
    caseKinds: { cutHead3D: 'GL.cutHead3D(full, tag, sx, sy, mirror, ops, cfg, behindCanvas, exemptCanvas, lastG?)', cutFrame: 'GL.cutFrame(frame, full, sx, sy, W, H, ops, cfg)', deathBake: 'GL.deathBake(set, W, H, cfg)', planeFromSwipe: 'GL.planeFromSwipe', shotFromSwipe: 'GL.shotFromSwipe', gashFromSwipe: 'GL.gashFromSwipe(tag, sx, sy, p0, p1, cfg{gashDepth,gashWidth,gashWobble}, seed, full)', straightShot: 'GL.straightShot (scorer none | preferEast(op)=clamp((op.a[0]+1)/2,0,1))', makeSolid: 'file-private solid intersection', cleanBlood: 'GL.cleanBlood', normHead: 'GL.normHead', mirrorHead: 'GL.mirrorHead', mirrorFrame: 'syncMirror derivation', primitives: 'hash / rng / vnoise3 / vnoise1 / mix' },
  },
  palette, sprites, cases,
};
fs.writeFileSync(OUT, JSON.stringify(out, null, 1));
console.log(`wrote ${OUT}: ${cases.length} cases, ${(fs.statSync(OUT).size / 1024).toFixed(0)} KB`);
