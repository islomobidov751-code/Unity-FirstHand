// Mock "First Hand" — a clock-tower workshop where the player calls a lift, enters a keypad code,
// assembles a glove schematic, builds the gloves from 4 parts, then tests palm blasts, a shield and a rock squeeze.
// It is a scripted stand-in: same event names, telemetry and cameras the real game sends (games/profiles.js).
(() => {
"use strict";
const E = window.Mock.engine, PI = Math.PI;
const SUN = E.norm3([0.35, 1, -0.45]);
const PART_Z = [-1.05, -0.35, 0.35, 1.05], PART_NAMES = ["Wrist ring", "Palm plate", "Knuckle guard", "Power cell"];
const SLOTS = [[-0.42, 0.97, 0.3], [-0.22, 0.97, 0.3], [0.22, 0.97, 0.3], [0.42, 0.97, 0.3]];
const TARGETS = [[-1.3, 1.85, 3.4], [0.15, 2.25, 3.7], [1.4, 1.65, 3.3]];
const ORANGE = [255, 150, 70], TEAL = [95, 212, 230];

function create() {
  // ---------------- static room ----------------
  const S = [];
  const box = (x, z, w, d, h, col, y0 = 0, extra = {}) => S.push({ x, z, w, d, h, col, y0, ...extra });
  box(0, 4.7, 9.6, 0.2, 3.8, [74, 70, 78]); box(0, -4.7, 9.6, 0.2, 3.8, [74, 70, 78]);
  box(-4.7, 0, 0.2, 9.6, 3.8, [68, 66, 76]); box(4.7, 0, 0.2, 9.6, 3.8, [68, 66, 76]);
  for (const x of [-4.4, 4.4]) for (const z of [-4.4, 4.4]) box(x, z, 0.4, 0.4, 3.8, [92, 80, 64]);     // corner pillars
  box(0, 0.35, 2.2, 0.8, 0.92, [118, 92, 66], 0, { edge: "rgba(30,20,10,.5)" });                         // workbench
  box(-0.32, 0.3, 0.36, 0.3, 0.05, [52, 56, 64], 0.92); box(0.32, 0.3, 0.36, 0.3, 0.05, [52, 56, 64], 0.92);  // glove frames
  box(-3.05, 0, 0.6, 3, 1.0, [100, 82, 62], 0, { edge: "rgba(30,20,10,.5)" });                          // parts shelf
  box(4.52, -0.2, 0.08, 0.38, 0.5, [36, 40, 48], 1.1);                                                    // keypad
  for (const x of [2.65, 3.95]) box(x, 2.8, 0.12, 0.12, 3.6, [120, 104, 80]);                            // lift rails
  box(0, 0.55, 1.6, 0.06, 0.06, [80, 84, 92], 2.1);                                                       // hologram projector bar
  const faces = S.flatMap((b) => E.boxFaces(b, SUN));

  // ---------------- timeline ----------------
  const REST_L = [-0.2, 1.0, 0.25], REST_R = [0.2, 1.0, 0.25];
  const BENCH = { px: 0, pz: -0.45, yaw: 0 };
  const K = []; let pose = { t: 0, ...BENCH, L: REST_L, R: REST_R, gL: false, gR: false };
  const key = (t, ch = {}) => { pose = { ...pose, ...ch, t }; K.push(pose); };
  const EV = []; const ev = (t, name, data, fx) => EV.push({ t, name, data, fx });
  key(0);
  ev(0.3, "session_start");
  ev(0.8, "chapter", { name: "Clock Tower" }, (s) => (s.chapter = "Clock Tower"));
  key(2.2); key(4.6, { px: 3.75, pz: -0.2, yaw: PI / 2 });
  key(5.3, { R: [0.05, 1.5, 0.62] });
  ev(5.5, "poke", { name: "Lift call" }, (s, t) => { s.stats.pokes++; s.keyGlow = t; });
  ev(5.8, "lift", {}, (s, t) => (s.liftT = t));
  key(6.1, { R: REST_R });
  [8.0, 8.8, 9.6, 10.4].forEach((tp, i) => {
    const hand = i % 2 ? "L" : "R";
    key(tp - 0.35, { [hand]: [i % 2 ? -0.04 : 0.04, 1.3 + (i % 3) * 0.04, 0.63] });
    ev(tp, "poke", { name: "Keypad" }, (s, t) => { s.stats.pokes++; s.keyGlow = t; });
    key(tp + 0.3, { [hand]: hand === "L" ? REST_L : REST_R });
  });
  ev(11.2, "keypad_solved", {}, (s, t) => (s.keyOk = t));
  key(12.0); key(14.6, BENCH);
  ev(15.2, "chapter", { name: "Glove schematic" }, (s) => (s.chapter = "Glove schematic"));
  [0, 1, 2].forEach((j) => {
    const tj = 17.2 + j * 3;
    key(tj - 1.3, { R: [-0.55, 1.4, 1.0] });
    key(tj - 0.95, { gR: true });
    ev(tj - 0.9, "grab", { object: "Schematic piece" }, (s) => s.stats.grabs++);
    key(tj, { R: [-0.42 + j * 0.42, 1.68, 1.08] });
    ev(tj, "schematic_placed", { n: j + 1, total: 3 }, (s) => s.schem++);
    key(tj + 0.15, { gR: false }); key(tj + 0.7, { R: REST_R });
  });
  ev(26, "chapter", { name: "Glove assembly" }, (s) => (s.chapter = "Glove assembly"));
  PART_Z.forEach((z, i) => {
    const T0 = 27 + i * 7.2, slot = SLOTS[i];
    key(T0); key(T0 + 2, { px: -2.35, pz: z, yaw: -PI / 2 });
    key(T0 + 2.5, { R: [0, 1.1, 0.62] });
    key(T0 + 2.7, { gR: true });
    ev(T0 + 2.7, "grab", { object: PART_NAMES[i] }, (s) => { s.carry = i; s.stats.grabs++; });
    key(T0 + 3.1, { R: [0.16, 1.15, 0.34] });
    key(T0 + 5.1, BENCH);
    key(T0 + 5.8, { R: [slot[0], slot[1] + 0.06, slot[2] + 0.45] });
    ev(T0 + 6.0, "glove_piece", { n: i + 1, total: 4, part: PART_NAMES[i] }, (s) => { s.parts[i] = "attached"; s.carry = null; s.stats.pieces++; });
    key(T0 + 6.05, { gR: false }); key(T0 + 6.6, { R: REST_R });
  });
  ev(56, "gloves_complete", {}, (s, t) => { s.gloves = true; s.glovesT = t; });
  ev(58, "chapter", { name: "Glove powers" }, (s) => (s.chapter = "Glove powers"));
  TARGETS.forEach((tg, k) => {
    const tb = 60.5 + k * 3, yaw = Math.atan2(tg[0] - BENCH.px, tg[2] - BENCH.pz);
    key(tb - 0.7, { yaw, R: [0.16, 1.45, 0.52] });
    ev(tb, "palm_blast", { target: k + 1 }, (s, t) => { s.targets[k] = false; s.stats.blasts++; s.beams.push({ k, t }); s.fx.push({ p: tg, t, life: 0.7, col: ORANGE, burst: true }); });
    key(tb + 0.8, { R: REST_R });
  });
  key(69.2, { yaw: 0 }); key(69.6, { L: [-0.13, 1.4, 0.45], R: [0.13, 1.4, 0.45] });
  ev(69.8, "shield", {}, (s, t) => (s.shieldUntil = t + 2.4));
  key(72.3, { L: REST_L, R: REST_R });
  key(73.4, { R: [0.75, 1.07, 0.76] }); key(73.6, { gR: true });
  ev(73.6, "grab", { object: "Rock" }, (s) => s.stats.grabs++);
  key(74.3, { R: [0.73, 1.1, 0.7] });
  ev(74.6, "rock_crushed", {}, (s, t) => { s.rock = false; s.fx.push({ p: [0.75, 1.1, 0.3], t, life: 0.9, col: [150, 150, 158], burst: true, debris: true }); });
  key(74.8, { gR: false }); key(75.3, { R: REST_R });
  ev(78, "session_end", {}, (s) => (s.status = "ended"));
  const LOOP = 84, tr = E.track(K);
  EV.sort((a, b) => a.t - b.t);

  // ---------------- live state ----------------
  let t = 0, evIdx = 0, s, sm = {};
  function reset() {
    t = 0; evIdx = 0; sm = {};
    s = { status: "live", chapter: "", stats: { grabs: 0, pokes: 0, pieces: 0, blasts: 0 }, parts: ["shelf", "shelf", "shelf", "shelf"], carry: null,
      schem: 0, liftT: -99, keyGlow: -99, keyOk: -99, gloves: false, glovesT: -99, targets: [true, true, true], rock: true, beams: [], fx: [], shieldUntil: -1 };
  }
  reset();

  function step(dt, emit) {
    t += dt;
    while (evIdx < EV.length && EV[evIdx].t <= t) { const e = EV[evIdx++]; e.fx && e.fx(s, t); emit(e.name, e.data || {}); }
    if (t >= LOOP) { reset(); }
  }
  const pose_ = () => tr(t);
  const hands = (p) => ({ L: E.local(p, p.L), R: E.local(p, p.R) });

  function cams(dt) {
    const p = pose_(), h = hands(p), k = (r) => 1 - Math.exp(-dt * r);
    const fwd = [Math.sin(p.yaw), 0, Math.cos(p.yaw)], right = [Math.cos(p.yaw), 0, -Math.sin(p.yaw)];
    const head = [p.px, 1.62, p.pz];
    const mid = [(h.L[0] + h.R[0]) / 2, (h.L[1] + h.R[1]) / 2, (h.L[2] + h.R[2]) / 2];
    const ahead = [head[0] + fwd[0] * 2, 1.3, head[2] + fwd[2] * 2];
    sm.pov = E.smooth(sm.pov, [(ahead[0] + mid[0]) / 2, (ahead[1] + mid[1]) / 2, (ahead[2] + mid[2]) / 2], k(5));
    sm.hp = E.smooth(sm.hp, [mid[0] + right[0] * 0.75 + fwd[0] * 0.95, mid[1] + 0.32, mid[2] + right[2] * 0.75 + fwd[2] * 0.95], k(3));
    sm.ht = E.smooth(sm.ht, mid, k(6));
    sm.op = E.smooth(sm.op, [head[0] - fwd[0] * 1.25 + right[0] * 0.35, 1.95, head[2] - fwd[2] * 1.25 + right[2] * 0.35], k(3));
    sm.ot = E.smooth(sm.ot, [head[0] + fwd[0] * 1.6, 1.15, head[2] + fwd[2] * 1.6], k(3));
    sm.wt = E.smooth(sm.wt, [p.px * 0.5, 1.2, p.pz * 0.5 + 0.8], k(1.2));
    return [
      { pos: [head[0] + fwd[0] * 0.08, head[1], head[2] + fwd[2] * 0.08], tgt: sm.pov, fov: 82, exclude: "player", pov: true },
      { pos: sm.hp, tgt: sm.ht, fov: 50 },
      { pos: sm.op, tgt: sm.ot, fov: 60 },
      { pos: [-4.15, 3.2, -4.15], tgt: sm.wt, fov: 68 },
    ];
  }

  function frame() {
    const p = pose_(), h = hands(p), fr = { boxes: [], spheres: [], people: [], lines: [], polys: [], glows: [] };
    // lift goes up and comes back down
    const lt = t - s.liftT, liftY = lt < 0 ? 0 : lt < 4 ? 2.6 * Math.min(1, lt / 3) : lt < 9 ? 2.6 * Math.max(0, 1 - (lt - 6) / 3) : 0;
    fr.boxes.push({ x: 3.3, z: 2.8, w: 1.2, d: 1.2, h: 0.1, y0: liftY, col: [140, 120, 90] }, { x: 3.3, z: 2.8, w: 0.5, d: 0.5, h: 0.5, y0: liftY + 0.1, col: [150, 106, 62] });
    // glove parts
    s.parts.forEach((st, i) => {
      let pos = st === "attached" ? SLOTS[i] : [-3.0, 1.0, PART_Z[i]];
      if (s.carry === i) pos = [h.R[0], h.R[1] - 0.05, h.R[2]];
      fr.boxes.push({ x: pos[0], z: pos[2], w: 0.14, d: 0.14, h: 0.09, y0: pos[1], col: i % 2 ? TEAL : ORANGE, glow: s.gloves });
    });
    if (s.gloves) for (const x of [-0.32, 0.32]) fr.glows.push({ x, y: 1.0, z: 0.3, r: 0.35, col: ORANGE, a: 0.35 + 0.15 * Math.sin(t * 4) });
    // hologram schematic
    const hz = 0.72, holoA = 0.1 + 0.05 * Math.sin(t * 2) + (s.schem >= 3 ? 0.12 : 0);
    fr.polys.push({ pts: [[-0.75, 1.25, hz], [0.75, 1.25, hz], [0.75, 2.05, hz], [-0.75, 2.05, hz]], fill: `rgba(95,212,230,${holoA.toFixed(2)})`, stroke: "rgba(95,212,230,.55)" });
    for (let j = 0; j < 3; j++) {
      const x = -0.42 + j * 0.42, on = j < s.schem;
      fr.polys.push({ pts: [[x - 0.15, 1.5, hz - 0.01], [x + 0.15, 1.5, hz - 0.01], [x + 0.15, 1.85, hz - 0.01], [x - 0.15, 1.85, hz - 0.01]],
        fill: on ? "rgba(255,190,110,.55)" : "rgba(95,212,230,.08)", stroke: on ? "rgba(255,210,150,.9)" : "rgba(95,212,230,.35)", bias: -0.02 });
    }
    fr.glows.push({ x: 0, y: 1.65, z: hz, r: 1.1, col: TEAL, a: 0.18 });
    // keypad + lift call lights
    const kg = t - s.keyGlow < 0.35, ok = t - s.keyOk < 3;
    fr.glows.push({ x: 4.46, y: 1.35, z: -0.2, r: kg ? 0.35 : 0.18, col: ok ? [80, 230, 140] : kg ? [255, 220, 140] : [255, 120, 80], a: 0.7 });
    // big clock on the back wall
    const cz = 4.58, cy = 2.4, rr = 1.05, ring = [];
    for (let i = 0; i < 32; i++) { const a = (i / 32) * 2 * PI; ring.push([Math.cos(a) * rr, cy + Math.sin(a) * rr, cz]); }
    fr.polys.push({ pts: ring, fill: "rgba(255,206,130,.22)", stroke: "rgba(255,214,150,.75)", bias: -0.3 });
    for (let i = 0; i < 12; i++) { const a = (i / 12) * 2 * PI; fr.lines.push({ a: [Math.cos(a) * rr * 0.85, cy + Math.sin(a) * rr * 0.85, cz - 0.01], b: [Math.cos(a) * rr * 0.97, cy + Math.sin(a) * rr * 0.97, cz - 0.01], col: "rgba(255,220,160,.9)", w: 0.03, bias: -0.35 }); }
    const ah = PI / 2 - t * 0.08, am = PI / 2 - t * 0.9;
    fr.lines.push({ a: [0, cy, cz - 0.02], b: [Math.cos(ah) * 0.5, cy + Math.sin(ah) * 0.5, cz - 0.02], col: "#3a2a18", w: 0.07, bias: -0.4 });
    fr.lines.push({ a: [0, cy, cz - 0.02], b: [Math.cos(am) * 0.85, cy + Math.sin(am) * 0.85, cz - 0.02], col: "#3a2a18", w: 0.045, bias: -0.4 });
    fr.glows.push({ x: 0, y: cy, z: cz - 0.05, r: 2.2, col: [255, 200, 120], a: 0.18 });
    // windows on the side walls
    for (const x of [-4.58, 4.58]) for (const z of [-2.6, 2.0]) fr.polys.push({ pts: [[x, 1.3, z - 0.5], [x, 1.3, z + 0.5], [x, 2.9, z + 0.5], [x, 2.9, z - 0.5]], fill: "rgba(150,190,230,.28)", stroke: "rgba(190,215,240,.5)", bias: -0.2 });
    // targets, beams, shield, rock
    TARGETS.forEach((tg, k) => {
      if (!s.targets[k]) return;
      const y = tg[1] + Math.sin(t * 2 + k) * 0.08;
      fr.spheres.push({ x: tg[0], y, z: tg[2], r: 0.16, col: [255, 110, 80], glow: true });
      fr.glows.push({ x: tg[0], y, z: tg[2], r: 0.5, col: [255, 120, 80], a: 0.35 });
    });
    s.beams = s.beams.filter((b) => t - b.t < 0.3);
    for (const b of s.beams) { fr.lines.push({ a: h.R, b: TARGETS[b.k], col: "rgba(130,230,255,.95)", w: 0.05, min: 2 }); fr.glows.push({ x: h.R[0], y: h.R[1], z: h.R[2], r: 0.3, col: [130, 230, 255], a: 0.8 }); }
    if (t < s.shieldUntil) {
      const c = E.local(p, [0, 1.38, 0.62]), right = [Math.cos(p.yaw), 0, -Math.sin(p.yaw)], disc = [];
      for (let i = 0; i < 24; i++) { const a = (i / 24) * 2 * PI, rx = Math.cos(a) * 0.5, ry = Math.sin(a) * 0.5; disc.push([c[0] + right[0] * rx, c[1] + ry, c[2] + right[2] * rx]); }
      fr.polys.push({ pts: disc, fill: "rgba(95,212,230,.22)", stroke: "rgba(150,235,255,.9)", bias: -0.1 });
    }
    if (s.rock) fr.spheres.push(s.carryRock ? { x: h.R[0], y: h.R[1], z: h.R[2], r: 0.1, col: [120, 118, 124] } : { x: 0.75, y: 1.02, z: 0.3, r: 0.1, col: [120, 118, 124] });
    s.fx = s.fx.filter((f) => t - f.t < f.life);
    for (const f of s.fx) {
      const u = (t - f.t) / f.life;
      fr.glows.push({ x: f.p[0], y: f.p[1], z: f.p[2], r: 0.3 + u * 0.9, col: f.col, a: 0.7 * (1 - u) });
      if (f.debris) for (let i = 0; i < 7; i++) { const a = i * 0.9; fr.spheres.push({ x: f.p[0] + Math.cos(a) * u * 0.5, y: f.p[1] + u * 0.3 - u * u * 0.9, z: f.p[2] + Math.sin(a) * u * 0.5, r: 0.03, col: f.col }); }
    }
    // the player: body, headset, hands (gloved once built)
    fr.people.push({ id: "player", x: p.px, y: 0, z: p.pz, yaw: p.yaw, col: [62, 84, 108], legs: [36, 40, 50], visor: true });
    const hc = s.gloves ? ORANGE : [226, 182, 150];
    fr.spheres.push({ x: h.L[0], y: h.L[1], z: h.L[2], r: 0.055, col: hc, glow: s.gloves }, { x: h.R[0], y: h.R[1], z: h.R[2], r: 0.055, col: hc, glow: s.gloves });
    return fr;
  }

  function telemetry() {
    const p = pose_(), h = hands(p);
    return {
      session: { status: s.status, elapsed: +t.toFixed(1), chapter: s.chapter },
      player: { pos: [+p.px.toFixed(2), 0, +p.pz.toFixed(2)], yaw: +p.yaw.toFixed(3), input: "hands",
        hands: { left: { tracked: true, pos: h.L.map((v) => +v.toFixed(2)), grab: !!p.gL }, right: { tracked: true, pos: h.R.map((v) => +v.toFixed(2)), grab: !!p.gR } } },
      stats: { ...s.stats },
    };
  }

  const scene = {
    faces, sun: SUN, fog: { col: [28, 26, 30], dist: 40 },
    background(V) {
      const g = V.ctx.createLinearGradient(0, 0, 0, V.H);
      g.addColorStop(0, "#15141a"); g.addColorStop(1, "#2a2520");
      V.ctx.fillStyle = g; V.ctx.fillRect(0, 0, V.W, V.H);
    },
    ground(V) {
      for (let x = -4.6; x < 4.6; x += 1.15) for (let z = -4.6; z < 4.6; z += 1.15) {
        const odd = (Math.round((x + z) / 1.15) & 1) === 1, d = V.T(x + 0.57, 0, z + 0.57)[2];
        V.poly([V.T(x, 0, z), V.T(x + 1.15, 0, z), V.T(x + 1.15, 0, z + 1.15), V.T(x, 0, z + 1.15)], V.shade(odd ? [88, 74, 60] : [78, 66, 54], 1, d), "rgba(0,0,0,.18)");
      }
      V.poly([V.T(-1.6, 0.005, -1.4), V.T(1.6, 0.005, -1.4), V.T(1.6, 0.005, 1.2), V.T(-1.6, 0.005, 1.2)], "rgba(40,90,110,.35)", "rgba(95,212,230,.3)");
    },
  };
  return { id: "firsthand", step, cams, frame: () => frame(), telemetry, scene, reset };
}
window.Mock = Object.assign(window.Mock || {}, { scenes: Object.assign((window.Mock || {}).scenes || {}, { firsthand: create }) });
})();
