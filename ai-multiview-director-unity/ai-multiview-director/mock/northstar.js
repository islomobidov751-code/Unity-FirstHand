// Mock "North Star" — the player is pulled aboard a sailing ship, ties a rope, cranks up the sail, takes the wheel,
// spots cargo with the spyglass, harpoons it, rides out a storm and lowers the diving bell.
// Scripted stand-in with the real game's event names and telemetry (games/profiles.js). The crew lines here are
// placeholders written for the mock; the real game reports its own lines through TelemetryBridge.ReportDialogue().
(() => {
"use strict";
const E = window.Mock.engine, PI = Math.PI;
const SUN = E.norm3([-0.4, 0.8, 0.5]);
const DECK = 1.0;
const CRATE_START = [9, 0, 24];
const CAPTAIN = { x: -0.9, z: -4.2, y: 1.8 }, MATE = { x: 1.1, z: 0.4, y: DECK };
const POLES = [[0, 1.0], [0, 4.6], [0, -3.6], [2.25, -1.2], [1.2, 3.4]];   // masts, wheel, crane, crank: keep cameras clear

function create() {
  // ---------------- the ship (static) ----------------
  const S = [];
  const box = (x, z, w, d, h, col, y0 = 0, extra = {}) => S.push({ x, z, w, d, h, col, y0, ...extra });
  const WOOD = [112, 76, 48], DARK = [78, 54, 36], PALE = [150, 112, 78];
  box(0, 0, 3.6, 12, 1.6, WOOD, -0.6);                // hull
  box(0, 6.8, 2.4, 1.6, 1.5, WOOD, -0.5);             // bow
  box(0, 7.9, 1.2, 0.6, 1.2, DARK, -0.2);
  box(0, 8.9, 0.14, 2.2, 0.14, DARK, 1.7);            // bowsprit
  box(0, -5.0, 3.4, 2.0, 0.8, PALE, DECK);            // quarterdeck
  box(0, -6.1, 3.4, 0.25, 1.4, DARK, DECK);           // stern rail
  for (const x of [-1.75, 1.75]) box(x, 0.5, 0.12, 11, 0.55, DARK, DECK);   // side rails
  box(0, 1.0, 0.28, 0.28, 10, DARK, DECK);            // main mast
  box(0, 1.0, 4.4, 0.16, 0.16, DARK, 4.2); box(0, 1.0, 3.6, 0.16, 0.16, DARK, 7.6);   // yards
  box(0, 4.6, 0.22, 0.22, 7.2, DARK, DECK);           // fore mast
  box(0, 4.6, 3.2, 0.14, 0.14, DARK, 3.6); box(0, 4.6, 2.6, 0.14, 0.14, DARK, 6.4);
  box(0, -3.6, 0.22, 0.22, 0.95, DARK, DECK);         // wheel pedestal
  box(0, 6.3, 0.55, 0.55, 0.75, [70, 74, 82], DECK);  // harpoon mount
  box(1.2, 3.4, 0.38, 0.38, 0.9, [70, 74, 82], DECK); // crank
  box(1.6, 2.2, 0.14, 0.32, 0.09, [60, 62, 68], DECK + 0.55);  // cleat on the rail
  box(2.25, -1.2, 0.2, 0.2, 5.5, DARK, DECK); box(2.25, -1.2, 1.4, 0.16, 0.16, DARK, 6.2);  // bell crane
  const faces = S.flatMap((b) => E.boxFaces(b, SUN));

  // ---------------- timeline ----------------
  const REST_L = [-0.24, 1.0, 0.18], REST_R = [0.24, 1.0, 0.18];
  const K = []; let pose = { t: 0, px: 0.4, pz: -1.0, yaw: PI * 0.8, L: REST_L, R: REST_R, gL: false, gR: false, spy: 0 };
  const key = (t, ch = {}) => { pose = { ...pose, ...ch, t }; K.push(pose); };
  const EV = []; const ev = (t, name, data, fx) => EV.push({ t, name, data, fx });
  const say = (t, speaker, text) => ev(t, "dialogue", { speaker, text }, (s) => s.stats.lines++);
  const faceTo = (x, z, from) => Math.atan2(x - from.px, z - from.pz);
  key(0);
  ev(0.3, "session_start");
  ev(0.8, "chapter", { name: "Rescued at sea" }, (s) => (s.chapter = "Rescued at sea"));
  key(1.2, { yaw: faceTo(CAPTAIN.x, CAPTAIN.z, pose) });
  say(1.6, "Captain", "Easy now, sailor. You're safe aboard the North Star.");
  key(6.0, { yaw: faceTo(MATE.x, MATE.z, pose) });
  say(6.5, "First Mate", "We could use another pair of hands on deck.");
  key(10); key(12.5, { px: 1.15, pz: 2.2, yaw: PI / 2 });
  key(13.2, { R: [0.05, 1.2, 0.45], L: [-0.05, 1.25, 0.4], gR: true, gL: true });
  key(14.2, { R: [0.1, 1.35, 0.42], L: [-0.1, 1.1, 0.38] });
  ev(14.5, "rope_tied", {}, (s) => { s.rope = true; s.stats.ropes++; });
  key(14.8, { R: REST_R, L: REST_L, gR: false, gL: false });
  say(16, "Captain", "Good knot! Now haul on that crank and raise the sail.");
  key(19); key(21, { px: 0.75, pz: 3.4, yaw: PI / 2 });
  key(21.5, { gR: true, gL: true });
  for (let i = 0; i < 8; i++) { const a = i * PI / 2; key(21.6 + i * 0.5, { R: [0.12 + Math.cos(a) * 0.12, 1.25 + Math.sin(a) * 0.12, 0.38], L: [0.0 + Math.cos(a) * 0.12, 1.25 + Math.sin(a) * 0.12, 0.38] }); }
  ev(21.6, "", null, (s, t) => (s.sailT = t));
  ev(25.6, "sail_raised", {}, (s) => (s.sail = 1));
  key(25.8, { R: REST_R, L: REST_L, gR: false, gL: false });
  ev(27, "chapter", { name: "Open water" }, (s) => (s.chapter = "Open water"));
  key(28); key(32, { px: 0, pz: -3.05, yaw: 0 });
  key(32.4, { R: [0.25, 1.35, 0.42], L: [-0.25, 1.35, 0.42], gR: true, gL: true });
  ev(32.5, "steering", {}, (s, t) => (s.steerT = t));
  const spyYaw = Math.atan2(CRATE_START[0] - 0, CRATE_START[2] + 3.05);
  key(35.2, { R: REST_R, gR: false, L: [-0.25, 1.35, 0.42] });
  key(36.0, { yaw: spyYaw, L: [-0.05, 1.6, 0.25], gL: true, spy: 1 });
  ev(36.2, "spyglass", {});
  say(37, "First Mate", "Cargo off the starboard bow!");
  key(40.4, { spy: 1 }); key(41, { spy: 0, L: REST_L, gL: false });
  key(45, { px: 0.2, pz: 5.6, yaw: spyYaw });
  ev(45.4, "harpoon_loaded", {});
  key(45.6, { R: [0.18, 1.5, 0.45], L: [-0.1, 1.45, 0.5], gR: true, gL: true });
  ev(47, "harpoon_fired", { hit: true }, (s, t) => { s.harpoonT = t; s.stats.harpoons++; });
  for (let i = 0; i < 12; i++) { const a = i * PI / 2; key(47.6 + i * 0.5, { R: [0.3 + Math.cos(a) * 0.1, 1.2 + Math.sin(a) * 0.1, 0.3], L: [-0.1, 1.45, 0.5] }); }
  ev(47.6, "", null, (s, t) => (s.pullT = t));
  ev(54, "cargo_retrieved", {}, (s) => { s.cargo = true; s.stats.cargo++; });
  key(54.3, { R: REST_R, L: REST_L, gR: false, gL: false });
  ev(56, "chapter", { name: "The storm" }, (s) => (s.chapter = "The storm"));
  ev(56.5, "weather", { kind: "storm" }, (s, t) => (s.stormT = t));
  say(58, "Captain", "Hold fast! Here comes the storm!");
  key(60); key(64, { px: 0, pz: -3.05, yaw: 0 });
  key(64.4, { R: [0.25, 1.35, 0.42], L: [-0.25, 1.35, 0.42], gR: true, gL: true });
  ev(64.5, "steering", {}, (s, t) => (s.steerT = t));
  say(68.5, "First Mate", "She's holding! Keep her steady!");
  key(72, { R: REST_R, L: REST_L, gR: false, gL: false });
  ev(73, "chapter", { name: "Diving bell" }, (s) => (s.chapter = "Diving bell"));
  key(73.5); key(75.5, { px: 1.3, pz: -1.2, yaw: PI / 2 });
  ev(74, "diving_bell", {}, (s, t) => (s.bellT = t));
  say(76, "Captain", "Bell's going down. Watch that line!");
  ev(81, "weather", { kind: "calm" }, (s, t) => (s.calmT = t));
  ev(86, "session_end", {}, (s) => (s.status = "ended"));
  const LOOP = 92, tr = E.track(K);
  EV.sort((a, b) => a.t - b.t);

  // ---------------- live state ----------------
  let t = 0, evIdx = 0, s, sm = {};
  function reset() {
    t = 0; evIdx = 0; sm = {};
    s = { status: "live", chapter: "", stats: { ropes: 0, harpoons: 0, cargo: 0, lines: 0 }, rope: false, sail: 0, sailT: -99, steerT: -99,
      harpoonT: -99, pullT: -99, cargo: false, stormT: -99, calmT: -99, bellT: -99 };
  }
  reset();
  const storm = () => {
    if (s.stormT < 0) return 0;
    const up = Math.min(1, Math.max(0, (t - s.stormT) / 4)), down = s.calmT > s.stormT ? Math.min(1, Math.max(0, (t - s.calmT) / 5)) : 0;
    return up * (1 - down);
  };
  const wave = (x, z, A) => A * (0.6 * Math.sin(0.22 * x + 1.3 * t) + 0.45 * Math.sin(0.23 * z - 1.1 * t + 1) + 0.25 * Math.sin(0.4 * (x + z) + 2 * t));
  const amp = () => 0.35 + storm() * 0.85;
  const sailLevel = () => (s.sail ? 1 : s.sailT > 0 ? Math.min(1, (t - s.sailT) / 4) : 0.15);
  function crate() {
    if (s.cargo) return [1.0, DECK, 4.6];
    if (s.pullT > 0) { const u = Math.min(1, (t - s.pullT) / 6.2); return [CRATE_START[0] + (1.9 - CRATE_START[0]) * u, wave(CRATE_START[0], CRATE_START[2], amp()) * (1 - u) + u * u * 1.2, CRATE_START[2] + (6.5 - CRATE_START[2]) * u]; }
    return [CRATE_START[0], wave(CRATE_START[0], CRATE_START[2], amp()) - 0.1, CRATE_START[2]];
  }

  function step(dt, emit) {
    t += dt;
    while (evIdx < EV.length && EV[evIdx].t <= t) { const e = EV[evIdx++]; e.fx && e.fx(s, t); if (e.name) emit(e.name, e.data || {}); }
    if (t >= LOOP) reset();
  }
  const hands = (p) => { const q = { ...p, py: DECK }; return { L: E.local(q, p.L), R: E.local(q, p.R) }; };

  function cams(dt) {
    const p = tr(t), k = (r) => 1 - Math.exp(-dt * r);
    const fwd = [Math.sin(p.yaw), 0, Math.cos(p.yaw)], right = [Math.cos(p.yaw), 0, -Math.sin(p.yaw)];
    const head = [p.px, DECK + 1.62, p.pz], c = crate();
    const look = p.spy > 0.5 ? c : [head[0] + fwd[0] * 4, DECK + 1.2, head[2] + fwd[2] * 4];
    sm.pov = E.smooth(sm.pov, look, k(p.spy > 0.5 ? 2 : 5));
    sm.deck = E.smooth(sm.deck, [p.px, DECK + 1.1, p.pz], k(2));
    // over the shoulder: try the right side, then the left, then straight behind; skip spots blocked by a mast
    const blocked = (cx, cz) => POLES.some(([x, z]) => {
      const vx = head[0] - cx, vz = head[2] - cz, l2 = vx * vx + vz * vz || 1, u = Math.max(0, Math.min(1, ((x - cx) * vx + (z - cz) * vz) / l2));
      return Math.hypot(cx + vx * u - x, cz + vz * u - z) < 0.45;
    });
    let op = null;
    for (const side of [1.1, -1.1, 0]) {
      const cx = head[0] - fwd[0] * 1.6 + right[0] * side, cz = head[2] - fwd[2] * 1.6 + right[2] * side;
      if (!blocked(cx, cz)) { op = [cx, DECK + 2.3, cz]; break; }
    }
    sm.op = E.smooth(sm.op, op || [head[0] - fwd[0] * 0.9, DECK + 2.5, head[2] - fwd[2] * 0.9], k(3));
    sm.ot = E.smooth(sm.ot, [head[0] + fwd[0] * 2, DECK + 1.2, head[2] + fwd[2] * 2], k(3));
    const a = t * 0.06 + 2.2;
    return [
      { pos: head, tgt: sm.pov, fov: p.spy > 0.5 ? 22 : 78, exclude: "player", pov: true, spy: p.spy > 0.5 },
      { pos: [-1.4, DECK + 5.4, -5.6], tgt: sm.deck, fov: 62 },
      { pos: sm.op, tgt: sm.ot, fov: 60 },
      { pos: [Math.sin(a) * 26, 7 + Math.sin(t * 0.3), Math.cos(a) * 26], tgt: [0, 3, 0.5], fov: 50 },
    ];
  }

  function frame() {
    const p = tr(t), h = hands(p), fr = { boxes: [], spheres: [], people: [], lines: [], polys: [], glows: [] };
    const sky = storm();
    // sails: billow toward +z, height follows how far they are raised
    const sail = (z, yTop, yBot, wTop, wBot, lvl, bulge) => {
      const yb = yTop - (yTop - yBot) * lvl, mid = (yTop + yb) / 2;
      const c = sky > 0.5 ? "rgba(196,196,190,.96)" : "rgba(238,230,210,.97)";
      fr.polys.push({ pts: [[-wTop, yTop, z], [wTop, yTop, z], [wBot, yb, z + 0.05], [0, mid, z + bulge * lvl], [-wBot, yb, z + 0.05]], fill: c, stroke: "rgba(120,100,80,.5)" });
    };
    sail(1.12, 7.5, 4.35, 1.7, 2.1, sailLevel(), 0.9); sail(1.12, 4.1, 1.9, 2.1, 2.0, sailLevel(), 0.7);
    sail(4.72, 6.3, 3.7, 1.25, 1.55, 1, 0.6);
    // ropes and rigging
    fr.lines.push({ a: [0, DECK + 9.8, 1.0], b: [0, 2.4, 8.9], col: "rgba(40,30,22,.9)", w: 0.025 }, { a: [0, DECK + 9.8, 1.0], b: [0, DECK + 1.3, -6.1], col: "rgba(40,30,22,.9)", w: 0.025 });
    for (const x of [-1.75, 1.75]) fr.lines.push({ a: [0, DECK + 7.6, 1.0], b: [x, DECK + 0.5, -0.6], col: "rgba(40,30,22,.8)", w: 0.02 });
    fr.lines.push(s.rope ? { a: [0, DECK + 4.2, 1.0], b: [1.6, DECK + 0.62, 2.2], col: "#c9a46a", w: 0.035 } : { a: [0, DECK + 4.2, 1.0], b: [0.4, DECK + 0.02, 1.8], col: "#c9a46a", w: 0.035 });
    // wheel (spins while steering)
    const spin = t - s.steerT < 6 ? (t - s.steerT) * 2 : 0, wc = [0, DECK + 1.05, -3.45];
    for (let i = 0; i < 8; i++) { const a = spin + (i * PI) / 4; fr.lines.push({ a: wc, b: [Math.cos(a) * 0.5, wc[1] + Math.sin(a) * 0.5, wc[2]], col: "#5a3c24", w: 0.045 }); }
    const rim = []; for (let i = 0; i < 20; i++) { const a = (i / 20) * 2 * PI; rim.push([Math.cos(a) * 0.42, wc[1] + Math.sin(a) * 0.42, wc[2]]); }
    for (let i = 0; i < 20; i++) fr.lines.push({ a: rim[i], b: rim[(i + 1) % 20], col: "#6b4a2c", w: 0.05 });
    // crank handle
    const ca = s.sailT > 0 && t - s.sailT < 4 ? (t - s.sailT) * 3 : s.pullT > 0 && t - s.pullT < 6.5 ? (t - s.pullT) * 3 : 0;
    fr.lines.push({ a: [1.2, DECK + 0.95, 3.4], b: [1.2 - 0.3, DECK + 0.95 + Math.sin(ca) * 0.25, 3.4 + Math.cos(ca) * 0.25], col: "#2a2e34", w: 0.05 });
    // harpoon barrel aimed at the cargo, line once fired
    const cpos = crate(), hb = [0, DECK + 0.95, 6.3], dir = E.norm3([cpos[0] - hb[0], cpos[1] - hb[1], cpos[2] - hb[2]]);
    fr.lines.push({ a: hb, b: [hb[0] + dir[0] * 1.1, hb[1] + dir[1] * 1.1 + 0.15, hb[2] + dir[2] * 1.1], col: "#3a3f46", w: 0.12 });
    if (s.harpoonT > 0 && !s.cargo) fr.lines.push({ a: [hb[0] + dir[0] * 1.1, hb[1] + 0.15, hb[2] + dir[2] * 1.1], b: cpos, col: "rgba(220,200,160,.9)", w: 0.025, min: 1.5 });
    fr.boxes.push({ x: cpos[0], z: cpos[2], w: 0.8, d: 0.8, h: 0.7, y0: cpos[1], col: [150, 106, 62], edge: "rgba(40,24,10,.6)" });
    // diving bell lowering from the crane
    if (s.bellT > 0) {
      const by = Math.max(-2.2, DECK + 3.2 - Math.max(0, t - s.bellT - 1) * 0.55);
      fr.lines.push({ a: [2.9, DECK + 6.2, -1.2], b: [2.9, by + 1.3, -1.2], col: "#2a2e34", w: 0.03 });
      if (by + 1.3 > wave(2.9, -1.2, amp()) - 0.2) fr.boxes.push({ x: 2.9, z: -1.2, w: 1.1, d: 1.1, h: 1.3, y0: by, col: [120, 110, 70] });
      fr.glows.push({ x: 2.9, y: by + 0.65, z: -0.62, r: 0.4, col: [255, 220, 140], a: 0.5 });
    }
    // lanterns
    fr.glows.push({ x: -1.5, y: DECK + 2.1, z: -5.9, r: 0.8, col: [255, 190, 110], a: 0.35 + sky * 0.25 }, { x: 1.5, y: DECK + 2.1, z: -5.9, r: 0.8, col: [255, 190, 110], a: 0.35 + sky * 0.25 });
    // crew + player
    fr.people.push({ x: CAPTAIN.x, y: CAPTAIN.y, z: CAPTAIN.z, yaw: Math.atan2(p.px - CAPTAIN.x, p.pz - CAPTAIN.z), col: [38, 50, 82], legs: [30, 30, 36], hat: [24, 24, 28] });
    fr.people.push({ x: MATE.x, y: MATE.y, z: MATE.z, yaw: Math.atan2(p.px - MATE.x, p.pz - MATE.z), col: [126, 88, 54], legs: [52, 46, 40] });
    fr.people.push({ id: "player", x: p.px, y: DECK, z: p.pz, yaw: p.yaw, col: [92, 104, 96], legs: [56, 52, 46], visor: true });
    const skin = [226, 182, 150];
    fr.spheres.push({ x: h.L[0], y: h.L[1], z: h.L[2], r: 0.055, col: skin }, { x: h.R[0], y: h.R[1], z: h.R[2], r: 0.055, col: skin });
    if (p.spy > 0.1) { const tip = E.local({ ...p, py: DECK }, [-0.05, 1.62, 0.55]); fr.lines.push({ a: h.L, b: tip, col: "#8a6a3a", w: 0.05 }); }
    return fr;
  }

  function telemetry() {
    const p = tr(t), h = hands(p);
    return {
      session: { status: s.status, elapsed: +t.toFixed(1), chapter: s.chapter, weather: storm() > 0.5 ? "storm" : "calm" },
      player: { pos: [+p.px.toFixed(2), DECK, +p.pz.toFixed(2)], yaw: +p.yaw.toFixed(3), input: "hands",
        hands: { left: { tracked: true, pos: h.L.map((v) => +v.toFixed(2)), grab: !!p.gL }, right: { tracked: true, pos: h.R.map((v) => +v.toFixed(2)), grab: !!p.gR } } },
      stats: { ...s.stats },
    };
  }

  const scene = {
    faces, sun: SUN,
    get fog() { const k = storm(); return { col: [Math.round(196 - 120 * k), Math.round(186 - 104 * k), Math.round(170 - 82 * k)], dist: 260 - 150 * k }; },
    background(V) {
      const k = storm(), ctx = V.ctx;
      const hz = V.T(V.cam.pos[0] + (V.cam.tgt[0] - V.cam.pos[0]) * 1000, V.cam.pos[1], V.cam.pos[2] + (V.cam.tgt[2] - V.cam.pos[2]) * 1000);
      const hy = hz[2] > 0 ? V.P(hz)[1] : V.H * 0.5;
      const g = ctx.createLinearGradient(0, hy - V.H, 0, hy);
      const mix = (a, b) => a.map((v, i) => Math.round(v + (b[i] - v) * k));
      g.addColorStop(0, `rgb(${mix([70, 112, 160], [34, 40, 50])})`); g.addColorStop(0.7, `rgb(${mix([150, 172, 196], [62, 70, 80])})`); g.addColorStop(1, `rgb(${mix([236, 214, 178], [84, 88, 92])})`);
      ctx.fillStyle = g; ctx.fillRect(0, 0, V.W, V.H);
      const sun = V.T(-300, 60, 500);
      if (sun[2] > 0 && k < 0.8) { const sp = V.P(sun), r = V.H * 0.3; const sg = ctx.createRadialGradient(sp[0], sp[1], 0, sp[0], sp[1], r); sg.addColorStop(0, `rgba(255,236,190,${0.9 * (1 - k)})`); sg.addColorStop(1, "rgba(255,236,190,0)"); ctx.fillStyle = sg; ctx.fillRect(sp[0] - r, sp[1] - r, r * 2, r * 2); }
      for (const [ang, w, ht] of [[0.6, 0.25, 9], [2.2, 0.18, 14], [3.9, 0.3, 7], [5.2, 0.12, 11]]) {   // distant islands
        const pts = []; for (let i = 0; i <= 8; i++) { const a = ang - w / 2 + (w * i) / 8; pts.push(V.T(Math.sin(a) * 420, Math.sin((i / 8) * PI) * ht - 1, Math.cos(a) * 420)); }
        V.poly(pts, V.shade([70, 84, 78], 1, 380));
      }
    },
    ground(V) {
      const A = amp(), k = storm(), q = [];
      const step = 8, R = 72;
      for (let x = -R; x < R; x += step) for (let z = -R; z < R; z += step) {
        if (Math.abs(x + 4) < 4 && Math.abs(z + 4) < 12) continue;   // hidden under the hull anyway
        const h00 = wave(x, z, A), h10 = wave(x + step, z, A), h11 = wave(x + step, z + step, A), h01 = wave(x, z + step, A);
        const c = V.T(x + step / 2, (h00 + h11) / 2, z + step / 2);
        if (c[2] < -step * 2) continue;
        const slope = (h10 - h00 + h11 - h01) / (2 * step), light = 0.85 + slope * 2.2 + (h00 + h11) * 0.06;
        q.push({ z: c[2], pts: [V.T(x, h00, z), V.T(x + step, h10, z), V.T(x + step, h11, z + step), V.T(x, h01, z + step)], light });
      }
      q.sort((a, b) => b.z - a.z);
      const base = [Math.round(36 - 10 * k), Math.round(92 - 30 * k), Math.round(118 - 34 * k)];
      for (const w of q) V.poly(w.pts, V.shade(base, w.light, w.z), "rgba(255,255,255,.05)");
      // wake along the hull
      V.poly([V.T(-2.2, 0.05, 7.5), V.T(2.2, 0.05, 7.5), V.T(3.4, 0.05, -8), V.T(-3.4, 0.05, -8)], "rgba(230,240,245,.22)");
    },
    overlay(ctx, W, H, cam) {
      const k = storm();
      if (k > 0.05) {   // rain + the odd lightning flash
        ctx.strokeStyle = `rgba(200,210,225,${0.35 * k})`; ctx.lineWidth = Math.max(1, W / 900);
        ctx.beginPath();
        for (let i = 0; i < 140 * k; i++) { const x = ((i * 97 + t * 900) % (W + 200)) - 100, y = ((i * 57 + t * 1400) % (H + 60)) - 30; ctx.moveTo(x, y); ctx.lineTo(x - W * 0.012, y + H * 0.05); }
        ctx.stroke();
        if (Math.sin(t * 1.7) > 0.997 || Math.sin(t * 2.9 + 1) > 0.998) { ctx.fillStyle = `rgba(255,255,255,${0.35 * k})`; ctx.fillRect(0, 0, W, H); }
      }
      if (cam.spy) {   // spyglass vignette
        const g = ctx.createRadialGradient(W / 2, H / 2, H * 0.34, W / 2, H / 2, H * 0.48);
        g.addColorStop(0, "rgba(0,0,0,0)"); g.addColorStop(1, "rgba(0,0,0,1)");
        ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
      }
    },
  };
  return { id: "northstar", step, cams, frame, telemetry, scene, reset };
}
window.Mock = Object.assign(window.Mock || {}, { scenes: Object.assign((window.Mock || {}).scenes || {}, { northstar: create }) });
})();
