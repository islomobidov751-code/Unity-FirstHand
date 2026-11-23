// Tiny canvas 3D renderer shared by the mock scenes (no WebGL needed, so it runs everywhere).
// Painter's algorithm: everything is turned into items with a depth, sorted far → near, then drawn.
// Scenes provide: background(V, fr), ground(V, fr), faces (static box faces), overlay(ctx, W, H, cam, fr)
// and per-frame drawables in fr: boxes, spheres, people, lines, polys, glows.
(() => {
"use strict";
const NEAR = 0.06;

function boxFaces(b, sun) {
  const x0 = b.x - b.w / 2, x1 = b.x + b.w / 2, z0 = b.z - b.d / 2, z1 = b.z + b.d / 2, y0 = b.y0 || 0, y1 = y0 + b.h, ym = (y0 + y1) / 2;
  const F = [
    [[-1, 0, 0], [[x0, y0, z1], [x0, y0, z0], [x0, y1, z0], [x0, y1, z1]], [x0, ym, b.z]],
    [[1, 0, 0], [[x1, y0, z0], [x1, y0, z1], [x1, y1, z1], [x1, y1, z0]], [x1, ym, b.z]],
    [[0, 0, -1], [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]], [b.x, ym, z0]],
    [[0, 0, 1], [[x1, y0, z1], [x0, y0, z1], [x0, y1, z1], [x1, y1, z1]], [b.x, ym, z1]],
    [[0, 1, 0], [[x0, y1, z0], [x1, y1, z0], [x1, y1, z1], [x0, y1, z1]], [b.x, y1, b.z]],
    [[0, -1, 0], [[x0, y0, z0], [x0, y0, z1], [x1, y0, z1], [x1, y0, z0]], [b.x, y0, b.z]],
  ];
  return F.map(([n, v, c], i) => ({ n, v, c, col: b.col, top: i === 4, edge: b.edge,
    l: (i === 5 ? 0.35 : 0.45) + 0.55 * Math.max(0, n[0] * sun[0] + n[1] * sun[1] + n[2] * sun[2]) * (b.glow ? 0 : 1) + (b.glow ? 0.55 : 0) }));
}
const norm3 = (v) => { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; };

function render(ctx, W, H, cam, scene, fr) {
  const [px, py, pz] = cam.pos;
  let fx = cam.tgt[0] - px, fy = cam.tgt[1] - py, fz = cam.tgt[2] - pz;
  const fl = Math.hypot(fx, fy, fz) || 1; fx /= fl; fy /= fl; fz /= fl;
  let rx = -fz, rz = fx; const rl = Math.hypot(rx, rz) || 1; rx /= rl; rz /= rl;
  const ux = -rz * fy, uy = rz * fx - rx * fz, uz = rx * fy;
  const focal = H / 2 / Math.tan((cam.fov * Math.PI) / 360);
  const fog = scene.fog || { col: [30, 36, 44], dist: 120 };
  const T = (x, y, z) => { const dx = x - px, dy = y - py, dz = z - pz; return [dx * rx + dz * rz, dx * ux + dy * uy + dz * uz, dx * fx + dy * fy + dz * fz]; };
  const P = (c) => [W / 2 + (c[0] * focal) / c[2], H / 2 - (c[1] * focal) / c[2]];
  const clipZ = (a, b) => { const t = (a[2] - NEAR) / (a[2] - b[2]); return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, NEAR]; };
  const lw = Math.max(1, W / 900);
  const shade = (c, light, depth) => {
    const f = Math.min(1, Math.max(0, depth) / fog.dist) * 0.8;
    return `rgb(${(Math.min(255, c[0] * light) * (1 - f) + fog.col[0] * f) | 0},${(Math.min(255, c[1] * light) * (1 - f) + fog.col[1] * f) | 0},${(Math.min(255, c[2] * light) * (1 - f) + fog.col[2] * f) | 0})`;
  };
  function poly(pts, fill, stroke) {
    const out = [];
    for (let i = 0; i < pts.length; i++) {
      const a = pts[i], b = pts[(i + 1) % pts.length], ain = a[2] >= NEAR, bin = b[2] >= NEAR;
      if (ain) out.push(a);
      if (ain !== bin) out.push(ain ? clipZ(a, b) : clipZ(b, a));
    }
    if (out.length < 3) return null;
    const sc = out.map(P);
    ctx.beginPath(); sc.forEach((s, i) => (i ? ctx.lineTo(s[0], s[1]) : ctx.moveTo(s[0], s[1]))); ctx.closePath();
    ctx.fillStyle = fill; ctx.fill();
    if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = lw; ctx.stroke(); }
    return sc;
  }
  function line(a, b, color, width) {
    if (a[2] < NEAR && b[2] < NEAR) return;
    if (a[2] < NEAR) a = clipZ(b, a); else if (b[2] < NEAR) b = clipZ(a, b);
    const sa = P(a), sb = P(b);
    ctx.beginPath(); ctx.moveTo(sa[0], sa[1]); ctx.lineTo(sb[0], sb[1]); ctx.strokeStyle = color; ctx.lineWidth = width; ctx.stroke();
  }
  const V = { T, P, poly, line, shade, focal, W, H, cam, lw, ctx };

  scene.background(V, fr);
  scene.ground(V, fr);

  const items = [];
  const addFace = (f) => {
    if (f.n[0] * (px - f.c[0]) + f.n[1] * (py - f.c[1]) + f.n[2] * (pz - f.c[2]) <= 0) return;
    const tc = T(f.c[0], f.c[1], f.c[2]);
    if (tc[2] < -3) return;
    items.push({ z: tc[2] - (f.top ? 0.005 : 0), draw: () => poly(f.v.map((v) => T(v[0], v[1], v[2])), shade(f.col, f.l, tc[2]), f.top ? "rgba(255,255,255,.07)" : f.edge || "rgba(0,0,0,.18)") });
  };
  for (const f of scene.faces) addFace(f);
  for (const b of fr.boxes || []) for (const f of boxFaces(b, scene.sun)) addFace(f);
  for (const s of fr.spheres || []) {
    const c = T(s.x, s.y, s.z); if (c[2] < NEAR + s.r) continue;
    items.push({ z: c[2], draw: () => {
      const p = P(c), r = (s.r * focal) / c[2];
      const g = ctx.createRadialGradient(p[0] - r * 0.35, p[1] - r * 0.4, r * 0.1, p[0], p[1], r);
      g.addColorStop(0, shade(s.col, s.glow ? 1.6 : 1.25, c[2])); g.addColorStop(1, shade(s.col, s.glow ? 1 : 0.55, c[2]));
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(p[0], p[1], Math.max(1, r), 0, 6.283); ctx.fill();
    } });
  }
  for (const pe of fr.people || []) {
    if (pe.id && pe.id === cam.exclude) continue;
    const c0 = T(pe.x, pe.y, pe.z); if (c0[2] < 0.3) continue;
    items.push({ z: c0[2], draw: () => drawPerson(V, pe, c0) });
  }
  for (const l of fr.lines || []) {
    const a = T(...l.a), b = T(...l.b); if (a[2] < NEAR && b[2] < NEAR) continue;
    const z = Math.max(NEAR, (a[2] + b[2]) / 2);
    items.push({ z: z + (l.bias || 0), draw: () => line(a, b, l.col, Math.max(l.min || 1, ((l.w || 0.02) * focal) / Math.max(z, 0.3))) });
  }
  for (const q of fr.polys || []) {
    const pts = q.pts.map((p) => T(p[0], p[1], p[2])), z = pts.reduce((s, p) => s + p[2], 0) / pts.length;
    if (z < NEAR) continue;
    items.push({ z: z + (q.bias || 0), draw: () => poly(pts, q.fill, q.stroke) });
  }
  for (const g of fr.glows || []) {
    const c = T(g.x, g.y, g.z); if (c[2] < NEAR) continue;
    items.push({ z: c[2] - 0.01, draw: () => {
      const p = P(c), r = (g.r * focal) / c[2];
      const gr = ctx.createRadialGradient(p[0], p[1], 0, p[0], p[1], r);
      gr.addColorStop(0, `rgba(${g.col},${g.a ?? 0.6})`); gr.addColorStop(1, `rgba(${g.col},0)`);
      ctx.fillStyle = gr; ctx.beginPath(); ctx.arc(p[0], p[1], r, 0, 6.283); ctx.fill();
    } });
  }
  items.sort((a, b) => b.z - a.z);
  for (const it of items) it.draw();
  if (scene.overlay) scene.overlay(ctx, W, H, cam, fr);
  const vg = ctx.createRadialGradient(W / 2, H / 2, H * 0.4, W / 2, H / 2, H);
  vg.addColorStop(0, "rgba(0,0,0,0)"); vg.addColorStop(1, "rgba(0,0,0,.38)");
  ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H);
}

// a simple standing figure: legs, torso, head with a face/visor in the direction of yaw
function drawPerson(V, pe, c0) {
  const { T, P, ctx, focal, shade } = V, s = 1 / c0[2];
  const at = (y) => T(pe.x, pe.y + y, pe.z);
  const seg = (y0, y1, w, col) => {
    const a = at(y0), b = at(y1); if (a[2] < 0.06 || b[2] < 0.06) return;
    const pa = P(a), pb = P(b);
    ctx.lineCap = "round"; ctx.lineWidth = w * focal * s; ctx.strokeStyle = col;
    ctx.beginPath(); ctx.moveTo(pa[0], pa[1]); ctx.lineTo(pb[0], pb[1] + 0.01); ctx.stroke(); ctx.lineCap = "butt";
  };
  const base = P(c0);
  ctx.fillStyle = "rgba(0,0,0,.35)"; ctx.beginPath(); ctx.ellipse(base[0], base[1], 0.32 * focal * s, 0.09 * focal * s, 0, 0, 6.283); ctx.fill();
  seg(0.1, 0.78, 0.26, shade(pe.legs || [40, 44, 52], 1, c0[2]));
  seg(0.82, 1.38, 0.4, shade(pe.col, 1, c0[2]));
  const hc = at(1.62); if (hc[2] < 0.06) return;
  const hp = P(hc), hr = 0.12 * focal * s;
  ctx.fillStyle = shade(pe.skin || [224, 182, 150], 1, c0[2]); ctx.beginPath(); ctx.arc(hp[0], hp[1], hr, 0, 6.283); ctx.fill();
  if (pe.hat) { ctx.fillStyle = shade(pe.hat, 1, c0[2]); ctx.beginPath(); ctx.ellipse(hp[0], hp[1] - hr * 0.6, hr * 1.5, hr * 0.45, 0, 0, 6.283); ctx.fill(); }
  if (pe.visor) {   // headset visor, drawn on the side the player faces
    const v = T(pe.x + Math.sin(pe.yaw) * 0.1, pe.y + 1.63, pe.z + Math.cos(pe.yaw) * 0.1);
    if (v[2] > 0.06 && v[2] < hc[2]) { const vp = P(v); ctx.fillStyle = "#15191f"; ctx.beginPath(); ctx.ellipse(vp[0], vp[1], hr * 0.95, hr * 0.5, 0, 0, 6.283); ctx.fill(); }
  }
}

// keyframe track: interpolates numbers (smoothstep), angles (shortest way), arrays; other values hold until the next key
function track(keys) {
  keys.sort((a, b) => a.t - b.t);
  const ease = (u) => u * u * (3 - 2 * u);
  const lerpA = (a, b, u) => a + ((((b - a + Math.PI * 3) % (Math.PI * 2)) - Math.PI) * u);
  return (t) => {
    let i = 0; while (i < keys.length - 1 && keys[i + 1].t <= t) i++;
    const a = keys[i], b = keys[Math.min(i + 1, keys.length - 1)];
    const u = b.t > a.t ? ease(Math.min(1, Math.max(0, (t - a.t) / (b.t - a.t)))) : 0;
    const out = {};
    for (const k of Object.keys(a)) {
      if (k === "t") continue;
      const va = a[k], vb = k in b ? b[k] : va;
      if (k === "yaw") out[k] = lerpA(va, vb, u);
      else if (typeof va === "number" && typeof vb === "number") out[k] = va + (vb - va) * u;
      else if (Array.isArray(va) && Array.isArray(vb)) out[k] = va.map((x, j) => x + (vb[j] - x) * u);
      else out[k] = va;
    }
    return out;
  };
}
// local (right, up, forward) offset from a player → world point
const local = (p, l) => [p.px + l[0] * Math.cos(p.yaw) + l[2] * Math.sin(p.yaw), (p.py || 0) + l[1], p.pz - l[0] * Math.sin(p.yaw) + l[2] * Math.cos(p.yaw)];
const smooth = (cur, target, k) => (cur ? cur.map((v, i) => v + (target[i] - v) * k) : target.slice());

window.Mock = Object.assign(window.Mock || {}, { engine: { render, boxFaces, track, local, smooth, norm3 } });
})();
