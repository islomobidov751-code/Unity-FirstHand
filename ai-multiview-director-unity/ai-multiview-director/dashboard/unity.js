// Adapter: lets this dashboard use the Unity StreamServer (13 cameras, JPEG pictures) instead of MediaMTX + the bridge.
// Nothing in Unity changes. Loaded between shared.js and app.js. Open the dashboard with ?unity=http://192.168.x.x:8080 for another PC.
(() => {
"use strict";
const MV = window.MV, qs = new URLSearchParams(location.search);
const UNITY = (qs.get("unity") || `http://${location.hostname || "localhost"}:8080`).replace(/\/$/, "");
const FALLBACK = ["PLAYER_STABILIZED","OTS_HAND","OTS_CLOSE","CLOSE_BOTH_HANDS","WIDE_FOLLOW","RAW_FPV","CROPPED_FPV",
  "TORSO_ANCHORED","OTS_HAND_ALT","POSE_HANDS","ORBIT_TWO_HAND","DYNAMIC_TRACKING","WIDE_SCENE_FIXED"];
const pretty = (n) => n.replace(/_/g, " ").toLowerCase().replace(/\b\w/g, (c) => c.toUpperCase());
MV.UNITY = true; MV.unity = { active: null, mode: "" }; MV.fast = null;

// 1. camera list: ask Unity (blocking, once, before app.js runs); fall back to the 13 known names
let names = FALLBACK;
try { const x = new XMLHttpRequest(); x.open("GET", UNITY + "/cameras", false); x.send();
  if (x.status === 200) { const l = JSON.parse(x.responseText).map((c) => c.name); if (l.length) names = l; } } catch {}
MV.CONFIG.camIds = names; MV.CONFIG.media = UNITY; MV.CONFIG.game = "firsthand";
const prof = MV.GAMES.get("firsthand");
prof.cams = names.map((n) => ({ id: n, name: pretty(n) })); prof.idle = names; prof.handsCam = null;

// 2. video: same interface as WhepPlayer, but pulls Unity JPEGs into a canvas and exposes it as a MediaStream
class MjpegPlayer {
  constructor(url, onChange) {
    this.id = decodeURIComponent(url.split("/").slice(-2)[0]); this.onChange = onChange || (() => {});
    this.status = "connecting"; this.closed = false; this.frames = 0; this._f = 0; this._t = performance.now();
    this.cv = document.createElement("canvas"); this.cv.width = 640; this.cv.height = 360;
    this.cx = this.cv.getContext("2d"); this.stream = this.cv.captureStream(15);
    this.stats = { latency: null, fps: 0, kbps: 0, w: 0, h: 0 }; this.loop();
  }
  set(s) { if (this.status !== s) { this.status = s; this.onChange(this); } }
  async loop() {
    while (!this.closed) {
      const t0 = performance.now(), img = new Image(); img.crossOrigin = "anonymous";
      img.src = `${UNITY}/snapshot/${encodeURIComponent(this.id)}?t=${Date.now()}`;
      try {
        await img.decode();
        if (this.cv.width !== img.naturalWidth) { this.cv.width = img.naturalWidth; this.cv.height = img.naturalHeight; }
        this.cx.drawImage(img, 0, 0); this.frames++; this.set("live");
      } catch { this.set("offline"); }
      const fast = MV.unity.active === this.id || MV.fast === this.id;           // on-air camera ~8 pictures/s, the rest ~1/s
      await new Promise((r) => setTimeout(r, Math.max(30, (fast ? 120 : 1000) - (performance.now() - t0))));
    }
  }
  close() { this.closed = true; }
  async poll() {
    const now = performance.now(), fps = this.status === "live" ? ((this.frames - this._f) * 1000) / (now - this._t) : 0;
    this._f = this.frames; this._t = now;
    this.stats = this.status === "live" ? { latency: 500, fps, kbps: 0, w: this.cv.width, h: this.cv.height } : { latency: null, fps: 0, kbps: 0, w: 0, h: 0 };
    return this.stats;
  }
}
MV.WhepPlayer = MjpegPlayer;

// 3. telemetry: poll Unity's /telemetry and feed it to the dashboard as the messages the bridge used to send
MV.connectBridge = (role, onMessage, onConn) => {
  let up = false, seq = 0;
  const grab = (s) => /grab|pinch|hold/i.test(s || "");
  const poll = async () => {
    try {
      const t = await (await fetch(UNITY + "/telemetry", { cache: "no-store" })).json(), v = t.values || {};
      MV.unity.active = t.activeCamera || null; MV.unity.mode = t.mode || "";
      if (!up) { up = true; onConn && onConn(true); }
      onMessage({ type: "state", game: "firsthand", seq: ++seq, t: Date.now(), session: { status: "live", elapsed: 0, chapter: t.scene || "" },
        player: { pos: [0, 0, 0], yaw: 0, hands: { left: { tracked: true, grab: grab(v.LHandState) }, right: { tracked: true, grab: grab(v.RHandState) } } }, stats: {} });
    } catch { if (up) { up = false; onConn && onConn(false); } }
  };
  setInterval(poll, 300); poll();
  return { connected: true, send() {} };
};

// 4. follow the camera Unity's control room / Python AI has on air (only while the dashboard is in AI/auto)
let last = null;
MV.follow = (director) => {
  const a = MV.unity.active; if (!a || a === last) return; last = a;
  const i = names.indexOf(a); if (i < 0 || !director.auto || director.live === i) return;
  director.cut(i, "Unity director · " + (MV.unity.mode || "AUTO"), "ai");
};
})();
