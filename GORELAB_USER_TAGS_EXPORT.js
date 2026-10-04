/* Exports the user's real tags from D:\CODEZ\GoreLab\data\state.json (read-only) to GORELAB_USER_TAGS.json.
 * Run: node GORELAB_USER_TAGS_EXPORT.js     Tags are written AS STORED ("raw") and AFTER the prototype's own load-time normalisation ("tag"). */
'use strict';
const vm = require('vm'), fs = require('fs'), path = require('path');
const ROOT = process.env.GORELAB_ROOT || 'D:/CODEZ/GoreLab';
const window = {}; const ctx = vm.createContext({ window, console });
for (const f of ['sim.js', 'cut.js', 'death.js']) vm.runInContext(fs.readFileSync(path.join(ROOT, 'web', f), 'utf8'), ctx, { filename: f });
const GL = window.GL, clone = (o) => JSON.parse(JSON.stringify(o));
const state = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/state.json'), 'utf8'));
const retail = JSON.parse(fs.readFileSync(path.join(ROOT, 'web/assets/retail.json'), 'utf8')), meta = Object.fromEntries(retail.map((m) => [m.name, m]));
const FWD = [[0, 0, 1], [-0.7071, 0, 0.7071], [-1, 0, 0], [-0.7071, 0, -0.7071], [0, 0, -1]], DIRS = ['Front', 'Front-side', 'Side', 'Back-side', 'Back', 'Back-side (mirror of 3)', 'Side (mirror of 2)', 'Front-side (mirror of 1)'];
const warnings = [], warn = (m) => warnings.push(m), dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const used = new Set();
const sprite = (name) => { used.add(name); const m = meta[name]; if (!m) warn(`sprite ${name} is not in retail.json`); return name; };
/* same loading rule as the prototype's mk(): normalise; a stored head forward of exactly (0,0,1) on a non-front direction is a placeholder -> direction default */
const norm = (t) => (t ? GL.normHead(clone(t)) : null);
const frameOut = (spriteName, raw, di, label) => {
  const m = meta[spriteName], head = norm(raw.head), torso = norm(raw.torso);
  if (raw.head && di !== undefined) {
    const fs_ = raw.head.f3, had = !!fs_ && !(di > 0 && Math.abs(fs_[0]) < 1e-6 && Math.abs(fs_[1]) < 1e-6 && Math.abs(fs_[2] - 1) < 1e-6);
    if (!had) { head.f3 = FWD[di].slice(); GL.fixFwd(head); warn(`${label}: head forward was missing or the (0,0,1) placeholder; the prototype substitutes the direction default ${JSON.stringify(FWD[di])} at load - exported value already reflects that`); }
  }
  const chk = (n, t, raw_) => {
    if (!t) return;
    if (!raw_.u3) warn(`${label}: ${n} has no stored u3 (derived from angle a at load)`);
    if (!raw_.f3) warn(`${label}: ${n} has no stored f3 (defaults to (0,0,1) at load${n === 'torso' && di > 0 ? ' - the prototype does NOT apply the direction default to torsos' : ''})`);
    if (di !== undefined && di <= 4 && dot(t.f3, FWD[di]) < 0) warn(`${label}: ${n} forward ${t.f3.map((v) => +v.toFixed(2))} faces away from the direction's expected forward ${JSON.stringify(FWD[di])}`);
    const l = Math.hypot(t.u3[0], t.u3[1]); if (l > 0.2 && Math.abs(Math.atan2(t.u3[1], t.u3[0]) - t.a) > 0.05) warn(`${label}: ${n} 2D angle a (${t.a.toFixed(2)}) disagrees with u3 (${Math.atan2(t.u3[1], t.u3[0]).toFixed(2)}); the 2D outline uses a, the 3D solid uses u3`);
    if (Math.abs(dot(t.u3, t.f3)) > 1e-3) warn(`${label}: ${n} u3 and f3 not perpendicular after load`);
    if (t.c[0] - t.rx < -1 || t.c[0] + t.rx > m.w + 1 || t.c[1] - t.ry < -1 || t.c[1] + t.ry > m.h + 1) warn(`${label}: ${n} outline extends beyond the ${m.w}x${m.h} sprite`);
    if (n === 'torso' && t.kind !== 'box') warn(`${label}: torso tag lacks kind 'box'`);
    if (n === 'torso' && !(t.rz > 0)) warn(`${label}: torso has no depth rz`);
  };
  chk('head', head, raw.head || {}); chk('torso', torso, raw.torso || {});
  if (head && torso && dot(head.f3, torso.f3) < 0) warn(`${label}: head and torso face opposite ways`);
  if (!head && !raw.noHead) warn(`${label}: no head tag (and not flagged noHead)`);
  if (!torso) warn(`${label}: no torso tag`);
  for (const k of ['behind', 'exempt', 'tbehind', 'texempt']) for (const i of raw[k] || []) if (i < 0 || i >= m.w * m.h) { warn(`${label}: ${k} index ${i} outside the ${m.w}x${m.h} sprite`); break; }
  return { sprite: sprite(spriteName), head, torso, ox: raw.ox || 0, oy: raw.oy || 0, noHead: !!raw.noHead, headMasks: { behind: raw.behind || [], exempt: raw.exempt || [] }, torsoMasks: { behind: raw.tbehind || [], exempt: raw.texempt || [] }, raw: { head: raw.head || null, torso: raw.torso || null } };
};
const walk = state.walk_tags.frames, dirs = [];
for (let d = 0; d < 8; d++) {
  if (d < 5) dirs.push({ name: DIRS[d], index: d, drawn: true, expectedForward: FWD[d], frames: [0, 1, 2, 3].map((i) => frameOut(`r${i}_${d}`, walk[d][i] || {}, d, `walk ${DIRS[d]} frame ${i + 1}`)) });
  else { const src = { 5: 3, 6: 2, 7: 1 }[d]; dirs.push({ name: DIRS[d], index: d, drawn: false, mirrorOf: src, note: 'Derived at load time from the drawn direction: sprite flipped left-right, tags through mirrorHead(tag, spriteWidth), ox negated, masks flipped. No stored data.', frames: [0, 1, 2, 3].map((i) => ({ sprite: sprite(`r${i}_${src}`), mirroredFromSprite: `r${i}_${src}` })) }); }
}
const dthF = state.death_retail.frames.map((f, i) => frameOut(`r8_${i}`, f, undefined, `death frame ${i + 1}`));
const out = {
  README: 'The user\'s real tags exported from data/state.json (keys walk_tags and death_retail) by GORELAB_USER_TAGS_EXPORT.js; state.json was only read. For every frame "head"/"torso" are the tags as the prototype holds them after load (normalised: u3, f3, n defaults; f3 made perpendicular to u3), "raw" holds the same tags exactly as stored. Coordinates are sprite-local pixels (origin top-left of the PNG, y down); u3/f3 are unit vectors in screen space (x right, y down, z toward the viewer). Mask lists are sprite-local pixel indices y*width+x. ox/oy are per-frame placement nudges (placeAt adds them). Nothing was corrected: see warnings.',
  source: { stateJson: path.join(ROOT, 'data/state.json'), spriteFolder: path.join(ROOT, 'web/assets/retail') },
  sprites: null,
  walk: { directions: dirs, mirrorOf: { 5: 3, 6: 2, 7: 1 }, frameCount: 4 },
  death: { hit: state.death_retail.hit, frames: dthF, settings: { effect: state.death_retail.effect, target: state.death_retail.target, flipInvert: state.death_retail.flipInvert, clean: state.death_retail.clean, anim: state.death_retail.anim } },
  warnings,
};
out.sprites = [...used].sort().map((n) => ({ name: n, file: path.join(ROOT, 'web/assets/retail', n + '.png').split('/').join(String.fromCharCode(92)), width: meta[n].w, height: meta[n].h, row: meta[n].row, col: meta[n].col }));
fs.writeFileSync(path.join(__dirname, 'GORELAB_USER_TAGS.json'), JSON.stringify(out, null, 1));
console.log(out.sprites.length + ' sprites, ' + warnings.length + ' warnings'); warnings.forEach((w) => console.log(' - ' + w));
