// Mock VR game page — stands in for the Unity build of First Hand or North Star.
// Runs the chosen scene (firsthand.js / northstar.js), renders its 4 spectator cameras, publishes each to MediaMTX
// over WebRTC (WHIP, paths cam1..cam4) and sends telemetry + events to the bridge with exactly the JSON that
// unity/TelemetryBridge.cs sends. Director commands come back and show up in the message list.
(() => {
"use strict";
const { CONFIG, GAMES } = window.MV;
const $ = (s) => document.querySelector(s);
const qs = new URLSearchParams(location.search);

// ======================= game selection =======================
let gameId = window.Mock.scenes[qs.get("game")] ? qs.get("game") : "firsthand";
let game = null, prof = null;
$("#gameSel").innerHTML = Object.keys(window.Mock.scenes).map((id) => `<option value="${id}">${GAMES.get(id).title}</option>`).join("");
$("#gameSel").addEventListener("change", (e) => loadGame(e.target.value));
function loadGame(id) {
  gameId = id; game = window.Mock.scenes[id](); prof = GAMES.get(id);
  $("#gameSel").value = id;
  document.title = `Mock ${prof.title} · AI MultiView Director`;
  $("#gTitle").textContent = prof.title;
  views.forEach((v, i) => (v.label.textContent = `${CONFIG.camIds[i].toUpperCase()} ${prof.cams[i].name}`));
  try { history.replaceState(null, "", `?game=${id}${qs.has("nopublish") ? "&nopublish" : ""}`); } catch {}
  note(`Loaded mock ${prof.title}`);
}

// ======================= game → bridge (same JSON as unity/TelemetryBridge.cs) =======================
let ws = null, wsUp = false, seq = 0, activeCam = "cam4", frameNo = 0;
function connect() {
  try { ws = new WebSocket(`${CONFIG.bridge}/game`); } catch { setTimeout(connect, 2000); return; }
  ws.onopen = () => { wsUp = true; note("Connected to the bridge"); paint(); };
  ws.onclose = () => { wsUp = false; paint(); setTimeout(connect, 2000); };
  ws.onmessage = (e) => { let m; try { m = JSON.parse(e.data); } catch { return; } if (m.type === "cmd") onCommand(m); };
}
const send = (o) => { if (ws && ws.readyState === 1) ws.send(JSON.stringify(o)); };
function emit(name, data) {
  send({ type: "event", seq: ++seq, t: Date.now(), name, data });
  note(`event → ${name} ${Object.keys(data).length ? JSON.stringify(data) : ""}`);
}
function sendState() {
  send({ type: "state", game: gameId, seq: ++seq, t: Date.now(), frame: frameNo, ...game.telemetry(), activeCam });
}
function onCommand(m) {
  if (m.cmd === "setActiveCamera") { activeCam = String(m.camId); note(`cmd ← setActiveCamera ${activeCam}`); paint(); }
  else note(`cmd ← ${m.cmd}`);
}

// ======================= video → MediaMTX (WHIP) =======================
class WhipPublisher {
  constructor(canvas, url, onChange) { this.canvas = canvas; this.url = url; this.onChange = onChange; this.status = "off"; this.closed = false; this.start(); }
  set(s) { if (this.status !== s) { this.status = s; this.onChange(); } }
  async start() {
    if (this.closed) return;
    this.set("connecting");
    this.stream = this.stream || this.canvas.captureStream(30);
    const pc = (this.pc = new RTCPeerConnection());
    for (const t of this.stream.getVideoTracks()) pc.addTransceiver(t, { direction: "sendonly", streams: [this.stream], sendEncodings: [{ maxBitrate: 3000000 }] });
    pc.onconnectionstatechange = () => {
      if (this.pc !== pc) return;
      if (pc.connectionState === "connected") this.set("live");
      if (["failed", "disconnected", "closed"].includes(pc.connectionState)) this.retry();
    };
    try {
      await pc.setLocalDescription(await pc.createOffer());
      await new Promise((r) => {
        if (pc.iceGatheringState === "complete") return r();
        pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && r());
        setTimeout(r, 1200);
      });
      const res = await fetch(this.url, { method: "POST", headers: { "Content-Type": "application/sdp" }, body: pc.localDescription.sdp });
      if (!res.ok) throw new Error("WHIP " + res.status);
      await pc.setRemoteDescription({ type: "answer", sdp: await res.text() });
    } catch { this.retry(); }
  }
  retry() {
    if (this.closed) return;
    const old = this.pc; this.pc = null; old && old.close();
    this.set("offline");
    clearTimeout(this.rt); this.rt = setTimeout(() => this.start(), 3000);
  }
  stop() { this.closed = true; clearTimeout(this.rt); this.pc && this.pc.close(); this.pc = null; this.set("off"); }
}

// ======================= page =======================
const VW = 960, VH = 540;
const views = CONFIG.camIds.map((id, i) => {
  const canvas = $("#c" + i); canvas.width = VW; canvas.height = VH;
  return { id, canvas, ctx: canvas.getContext("2d"), st: $("#s" + i), label: $("#l" + i), pub: null };
});
let publishing = !qs.has("nopublish"), paused = false;
function startPublishing() { views.forEach((v) => (v.pub = new WhipPublisher(v.canvas, `${CONFIG.media}/${v.id}/whip`, paint))); }
function stopPublishing() { views.forEach((v) => { v.pub && v.pub.stop(); v.pub = null; }); }
$("#pub").addEventListener("click", () => { publishing = !publishing; publishing ? startPublishing() : stopPublishing(); paint(); });
$("#pause").addEventListener("click", () => { paused = !paused; paint(); });
$("#restart").addEventListener("click", () => { game.reset(); note("Session restarted"); });
$("#mediaUrl").textContent = CONFIG.media;
$("#bridgeUrl").textContent = CONFIG.bridge + "/game";

function note(text) {
  const li = document.createElement("li");
  li.textContent = `${new Date().toTimeString().slice(0, 8)}  ${text}`;
  $("#log").prepend(li);
  while ($("#log").children.length > 50) $("#log").lastChild.remove();
}
function paint() {
  $("#dBridge").className = "dot" + (wsUp ? " on" : "");
  const live = views.filter((v) => v.pub && v.pub.status === "live").length;
  $("#nPub").textContent = publishing ? `${live}/4` : "off";
  $("#dPub").className = "dot" + (live === 4 ? " on" : live ? " warn" : "");
  $("#pub").textContent = publishing ? "Stop publishing video" : "Publish video";
  $("#pause").textContent = paused ? "Resume" : "Pause";
  views.forEach((v) => {
    const s = v.pub ? v.pub.status : "off";
    v.st.textContent = s === "live" ? "PUBLISHING" : s === "connecting" ? "CONNECTING" : s === "offline" ? "MEDIAMTX NOT REACHABLE" : "NOT PUBLISHING";
    v.st.className = "st " + s;
    v.canvas.parentElement.classList.toggle("on", v.id === activeCam);
  });
}

// ======================= main loop =======================
loadGame(gameId);
connect();
if (publishing) startPublishing();
paint();
let last = performance.now(), lastRender = 0, telAcc = 0, uiAcc = 0;
function loop(now) {
  const dt = Math.min(0.05, (now - last) / 1000); last = now;
  if (!paused) { game.step(dt, emit); frameNo++; }
  const cams = game.cams(paused ? 0 : dt);
  if (now - lastRender > 32) {   // ~30 fps video
    lastRender = now;
    const fr = game.frame();
    views.forEach((v, i) => window.Mock.engine.render(v.ctx, VW, VH, cams[i], game.scene, fr));
  }
  telAcc += dt;
  if (telAcc >= 0.05) { telAcc = 0; sendState(); }   // 20 Hz telemetry
  uiAcc += dt;
  if (uiAcc > 0.25) { uiAcc = 0; const tm = game.telemetry().session; $("#chapter").textContent = `${tm.chapter || "—"} · ${Math.floor(tm.elapsed / 60)}:${String(Math.floor(tm.elapsed % 60)).padStart(2, "0")}`; }
  requestAnimationFrame(loop);
}
requestAnimationFrame(loop);
})();
