// Shared by the dashboard (index.html) and the viewer page (viewer.html).
// Everything is exposed on window.MV so the pages need no build step.
(() => {
"use strict";
const qs = new URLSearchParams(location.search);
const host = location.hostname || "localhost";
const httpBase = location.protocol.startsWith("http") ? location.host : "localhost:8080";

// ---- configuration (override any of it with URL parameters, e.g. ?media=http://192.168.0.23:8889) ----
const CONFIG = {
  media: (qs.get("media") || `http://${host}:8889`).replace(/\/$/, ""),           // MediaMTX WebRTC port
  bridge: (qs.get("bridge") || `${location.protocol === "https:" ? "wss" : "ws"}://${httpBase}`).replace(/\/$/, ""),
  camIds: ["cam1", "cam2", "cam3", "cam4"],  // MediaMTX paths; names per game come from games/profiles.js
  game: qs.get("game") || null,                   // force a game profile instead of following the telemetry
  encodeMs: 60,                                   // encoder + server time the browser can't measure (for latency estimate)
};

const LANGS = {
  ko: { name: "Korean", tts: "ko-KR" }, ja: { name: "Japanese", tts: "ja-JP" }, zh: { name: "Chinese", tts: "zh-CN" },
  es: { name: "Spanish", tts: "es-ES" }, en: { name: "English", tts: "en-US" }, fr: { name: "French", tts: "fr-FR" },
  de: { name: "German", tts: "de-DE" }, pt: { name: "Portuguese", tts: "pt-BR" }, it: { name: "Italian", tts: "it-IT" },
  vi: { name: "Vietnamese", tts: "vi-VN" }, th: { name: "Thai", tts: "th-TH" }, id: { name: "Indonesian", tts: "id-ID" },
  hi: { name: "Hindi", tts: "hi-IN" }, ar: { name: "Arabic", tts: "ar-SA" }, ru: { name: "Russian", tts: "ru-RU" }, tr: { name: "Turkish", tts: "tr-TR" },
};
const BUILTIN = ["en", "ko", "ja", "es", "zh"];
const GAMES = window.MV_GAMES;
// ---- WHEP player: plays one MediaMTX path over WebRTC, reconnects on its own ----
class WhepPlayer {
  constructor(url, onChange) {
    this.url = url; this.onChange = onChange || (() => {});
    this.stream = null; this.status = "connecting"; this.pc = null; this.closed = false;
    this.stats = { latency: null, fps: 0, kbps: 0, w: 0, h: 0 }; this._bytes = 0; this._t = 0;
    this.start();
  }
  set(status) { if (this.status !== status) { this.status = status; this.onChange(this); } }
  async start() {
    if (this.closed) return;
    this.set("connecting");
    const pc = (this.pc = new RTCPeerConnection());
    pc.addTransceiver("video", { direction: "recvonly" });
    pc.addTransceiver("audio", { direction: "recvonly" });
    pc.ontrack = (e) => { if (!this.stream) this.stream = e.streams[0] || new MediaStream([e.track]); this.set("live"); };
    pc.onconnectionstatechange = () => { if (this.pc === pc && ["failed", "disconnected", "closed"].includes(pc.connectionState)) this.retry(); };
    try {
      await pc.setLocalDescription(await pc.createOffer());
      await new Promise((r) => {
        if (pc.iceGatheringState === "complete") return r();
        pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && r());
        setTimeout(r, 1200);
      });
      const res = await fetch(this.url, { method: "POST", headers: { "Content-Type": "application/sdp" }, body: pc.localDescription.sdp });
      if (!res.ok) throw new Error("WHEP " + res.status);
      await pc.setRemoteDescription({ type: "answer", sdp: await res.text() });
    } catch { this.retry(); }
  }
  retry() {
    if (this.closed) return;
    const old = this.pc; this.pc = null; old?.close();
    this.stream = null; this.set("offline");
    clearTimeout(this._rt); this._rt = setTimeout(() => this.start(), 3000);
  }
  close() { this.closed = true; clearTimeout(this._rt); this.pc?.close(); this.pc = null; this.stream = null; }
  async poll() {
    if (!this.pc || this.status !== "live") { this.stats = { latency: null, fps: 0, kbps: 0, w: 0, h: 0 }; return this.stats; }
    let jb = null, rtt = 0, fps = 0, w = 0, h = 0, bytes = 0;
    (await this.pc.getStats()).forEach((s) => {
      if (s.type === "inbound-rtp" && s.kind === "video") {
        if (s.jitterBufferEmittedCount) jb = (s.jitterBufferDelay / s.jitterBufferEmittedCount) * 1000;
        fps = s.framesPerSecond || 0; w = s.frameWidth || 0; h = s.frameHeight || 0; bytes = s.bytesReceived || 0;
      }
      if (s.type === "candidate-pair" && s.state === "succeeded" && s.currentRoundTripTime) rtt = s.currentRoundTripTime * 1000;
    });
    const now = performance.now();
    const kbps = this._t ? ((bytes - this._bytes) * 8) / (now - this._t) : 0;
    this._bytes = bytes; this._t = now;
    this.stats = { latency: jb == null ? null : Math.round(jb + rtt / 2 + CONFIG.encodeMs), fps, kbps: Math.max(0, kbps), w, h };
    return this.stats;
  }
}

// ---- bridge connection with automatic reconnect ----
function connectBridge(role, onMessage, onConnection) {
  let ws = null;
  const api = {
    connected: false,
    send(obj) { if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj)); },
  };
  const open = () => {
    try { ws = new WebSocket(`${CONFIG.bridge}/${role}`); } catch { setTimeout(open, 2000); return; }
    ws.onopen = () => { api.connected = true; onConnection && onConnection(true); };
    ws.onclose = () => { api.connected = false; onConnection && onConnection(false); setTimeout(open, 2000); };
    ws.onmessage = (e) => { let m; try { m = JSON.parse(e.data); } catch { return; } onMessage(m); };
  };
  open();
  return api;
}

// ---- telemetry preview: a top-down map drawn from telemetry, shown when a camera has no video ----
// Uses the game profile's map (meters). Shows the player, where they look, and both hands (filled = grabbing).
function drawPreview(ctx, W, H, state, opts = {}) {
  const prof = opts.profile || GAMES.get(state && state.game), map = prof.map || { half: 5, shapes: [] };
  const half = map.half, dpr = opts.dpr || 1, accent = prof.accent || "#5fd4e6";
  ctx.fillStyle = "#0c1116"; ctx.fillRect(0, 0, W, H);
  const size = Math.min(W, H) * 0.86, ox = (W - size) / 2, oy = (H - size) / 2, k = size / (half * 2);
  const X = (x) => ox + (x + half) * k, Y = (z) => oy + (half - z) * k;
  ctx.strokeStyle = "rgba(129,145,165,.12)"; ctx.lineWidth = 1;
  for (let i = 0; i <= 8; i++) {
    const p = ox + (size * i) / 8 + 0.5, q = oy + (size * i) / 8 + 0.5;
    ctx.beginPath(); ctx.moveTo(p, oy); ctx.lineTo(p, oy + size); ctx.moveTo(ox, q); ctx.lineTo(ox + size, q); ctx.stroke();
  }
  ctx.font = `${10 * dpr}px "IBM Plex Sans", sans-serif`;
  for (const r of map.shapes || []) {
    ctx.fillStyle = "rgba(160,180,200,.10)"; ctx.strokeStyle = "rgba(160,180,200,.45)"; ctx.lineWidth = 1.2 * dpr;
    ctx.fillRect(X(r.x - r.w / 2), Y(r.z + r.d / 2), r.w * k, r.d * k); ctx.strokeRect(X(r.x - r.w / 2), Y(r.z + r.d / 2), r.w * k, r.d * k);
    if (W > 260 && r.label) { ctx.fillStyle = "#7b8792"; ctx.fillText(r.label, X(r.x - r.w / 2) + 3 * dpr, Y(r.z + r.d / 2) + 12 * dpr); }
  }
  const pl = state && state.player;
  if (pl && pl.pos) {
    const x = X(pl.pos[0]), y = Y(pl.pos[2]), r = Math.max(5 * dpr, 0.25 * k), yaw = pl.yaw || 0;
    ctx.fillStyle = accent + "33";               // view cone
    ctx.beginPath(); ctx.moveTo(x, y);
    ctx.arc(x, y, r * 6, -Math.PI / 2 + yaw - 0.5, -Math.PI / 2 + yaw + 0.5); ctx.closePath(); ctx.fill();
    ctx.fillStyle = accent; ctx.beginPath(); ctx.arc(x, y, r, 0, 6.283); ctx.fill();
    for (const side of ["left", "right"]) {
      const h = pl.hands && pl.hands[side];
      if (!h || !h.pos || h.tracked === false) continue;
      const hx = X(h.pos[0]), hy = Y(h.pos[2]), hr = Math.max(3 * dpr, 0.1 * k);
      ctx.strokeStyle = "#dbe2e8"; ctx.lineWidth = 1.5 * dpr; ctx.fillStyle = "#dbe2e8";
      ctx.beginPath(); ctx.arc(hx, hy, hr, 0, 6.283); h.grab ? ctx.fill() : ctx.stroke();
    }
    if (W > 260) { ctx.fillStyle = "#dbe2e8"; ctx.font = `${11 * dpr}px "IBM Plex Sans", sans-serif`; ctx.fillText("Player", x + r + 5, y + 4); }
  } else {
    ctx.fillStyle = "#7b8792"; ctx.font = `${13 * dpr}px "IBM Plex Sans", sans-serif`; ctx.textAlign = "center";
    ctx.fillText("Waiting for the game…", W / 2, H / 2); ctx.textAlign = "left";
  }
  if (opts.label) {
    ctx.font = `500 ${10 * dpr}px "JetBrains Mono", monospace`;
    const tw = ctx.measureText(opts.label).width;
    ctx.fillStyle = "rgba(0,0,0,.6)"; ctx.fillRect(6 * dpr, 6 * dpr, tw + 10 * dpr, 17 * dpr);
    ctx.fillStyle = "#f2a93b"; ctx.fillText(opts.label, 11 * dpr, 18 * dpr);
  }
}

// keep a canvas sharp at its displayed size
function fitCanvas(c) {
  const dpr = Math.min(window.devicePixelRatio || 1, 2), r = c.getBoundingClientRect();
  const w = Math.max(2, Math.round(r.width * dpr)), h = Math.max(2, Math.round(r.height * dpr));
  if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
  return dpr;
}

// caption text for one language: ok / pending / fallback (shows the original)
function captionFor(cap, lang) {
  if (!cap) return { text: "", kind: "none" };
  const who = cap.kind === "dialogue" && cap.speaker ? cap.speaker + ": " : "";
  if (cap.translations && cap.translations[lang]) return { text: who + cap.translations[lang], kind: "ok" };
  if (cap.source === lang) return { text: who + cap.text, kind: "ok" };
  if ((cap.pending || []).includes(lang)) return { text: "translating…", kind: "pending" };
  return { text: who + cap.text, kind: "fallback" };
}
function speakText(text, lang) {
  try { speechSynthesis.cancel(); const u = new SpeechSynthesisUtterance(text); u.lang = (LANGS[lang] || {}).tts || lang; u.rate = 1.08; speechSynthesis.speak(u); } catch {}
}
const fmtClock = (s) => { s = Math.max(0, Math.ceil(s || 0)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`; };
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const langOptions = (sel) => Object.entries(LANGS).map(([k, v]) => `<option value="${k}"${k === sel ? " selected" : ""}>${v.name}${BUILTIN.includes(k) ? "" : " · Claude"}</option>`).join("");

const fmtElapsed = (s) => { s = Math.max(0, Math.floor(s || 0)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`; };
window.MV = { CONFIG, LANGS, BUILTIN, GAMES, fmtElapsed, WhepPlayer, connectBridge, drawPreview, fitCanvas, captionFor, speakText, fmtClock, esc, langOptions };
})();
