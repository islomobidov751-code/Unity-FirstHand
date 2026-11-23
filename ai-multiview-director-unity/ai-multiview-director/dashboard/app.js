// AI MultiView Director — dashboard logic (single-player VR games: First Hand, North Star, …)
// Video:     4 WHEP players (cam1..cam4) from MediaMTX. The program monitor reuses the on-air camera's stream.
// Telemetry: WebSocket from the bridge, held back by the measured video delay so panels match the picture.
// Director:  cuts on game events using the game's profile (games/profiles.js), otherwise rotates idle shots.
// Captions:  built by the bridge; this page shows them, sends commentator lines and the languages it wants.
(() => {
"use strict";
const { CONFIG, LANGS, GAMES, WhepPlayer, connectBridge, drawPreview, fitCanvas, captionFor, speakText, fmtElapsed, esc, langOptions } = window.MV;
const $ = (s) => document.querySelector(s);
const IDS = CONFIG.camIds;

// ======================= state =======================
const ui = { latAuto: true, latManual: 300, sync: true, measured: null, bitrate: 0 };
let state = null, lastRaw = null, gameUp = false, forced = CONFIG.game, gameId = forced || GAMES.DEFAULT_ID;
let prof = GAMES.get(gameId);
const held = [];
let stateCount = 0, hz = 0, rtt = null, lastAction = "—";
const hold = () => (!ui.sync ? 0 : ui.latAuto && ui.measured != null ? ui.measured : ui.latManual);
const camName = (i) => prof.cams[i] ? prof.cams[i].name : IDS[i];
const camIndex = (id) => Math.max(0, IDS.indexOf(id));

// ======================= event log =======================
const logEl = $("#log");
function log(html, kind) {
  const li = document.createElement("li");
  li.innerHTML = `<time>${new Date().toTimeString().slice(0, 8)}</time><span class="${kind || ""}">${html}</span>`;
  logEl.prepend(li);
  while (logEl.children.length > 80) logEl.lastChild.remove();
}

// ======================= game profile =======================
$("#gameSel").innerHTML += GAMES.ids.map((id) => `<option value="${id}">${esc(GAMES.get(id).title)}</option>`).join("");
$("#gameSel").value = forced || "";
$("#gameSel").addEventListener("change", (e) => { forced = e.target.value || null; setGame(forced || (lastRaw && lastRaw.game) || gameId, true); });
function setGame(id, force) {
  if (!id || (!force && forced) || (id === gameId && !force)) return;
  gameId = id; prof = GAMES.get(id);
  document.documentElement.style.setProperty("--game", prof.accent);
  $("#gTitle").textContent = prof.title;
  tiles.forEach((t, i) => (t.nm.textContent = camName(i)));
  buildChapters(); buildStats(); buildQuick(); renderTally();
  log(`Game profile: ${esc(prof.title)}`, "sys");
}

// ======================= bridge =======================
const bridge = connectBridge("dashboard", onMessage, (ok) => {
  $("#dBridge").className = "dot" + (ok ? " on" : "");
  if (ok) { sendLangs(); sendProgram(); log("Connected to the bridge", "sys"); }
  else { setGameUp(false); log("Bridge offline, reconnecting…", "sys"); }
});
function setGameUp(up) { if (up !== gameUp) { gameUp = up; $("#dGame").className = "dot" + (up ? " on" : ""); } }
function onMessage(m) {
  const now = performance.now();
  switch (m.type) {
    case "hello": setGameUp(m.game === "connected"); setEngine(m.translation); if (m.gameId) setGame(m.gameId); break;
    case "status": setGameUp(m.game === "connected"); log(m.game === "connected" ? "Game connected" : "Game disconnected", "sys"); break;
    case "game": setGame(m.id); break;
    case "state": lastRaw = m; stateCount++; setGameUp(true); if (m.game) setGame(m.game); held.push({ due: now + hold(), msg: m }); break;
    case "event": held.push({ due: now + hold(), msg: m }); break;
    case "caption": onCaption(m); break;
    case "audience": renderAudience(m); break;
    case "pong": rtt = Math.round(now - m.id); break;
  }
}
function apply(m) {
  if (m.type === "state") state = m;
  else if (m.type === "event") handleEvent(m);
  else if (m.type === "_showcap") { const c = caps.get(m.id); if (c) showCaption(c); }
}
setInterval(() => bridge.send({ type: "ping", id: performance.now() }), 2000);
setInterval(() => { hz = stateCount; stateCount = 0; }, 1000);

// ======================= video =======================
const tiles = IDS.map((id, i) => {
  const b = document.createElement("button");
  b.type = "button"; b.className = "tile novideo";
  b.innerHTML = `<span class="scr"><video muted autoplay playsinline></video><canvas></canvas><span class="sig">CONNECTING</span></span>
    <span class="meta"><kbd>${i + 1}</kbd><span class="nm"></span><span class="tally"></span></span>`;
  b.addEventListener("click", () => manualCut(i));
  $("#multiview").appendChild(b);
  return { el: b, video: b.querySelector("video"), canvas: b.querySelector("canvas"), sig: b.querySelector(".sig"), nm: b.querySelector(".nm") };
});
const pgmVideo = $("#pgmVideo"), pgmCanvas = $("#pgmCanvas");
const players = IDS.map((id, i) => new WhepPlayer(`${CONFIG.media}/${id}/whep`, () => onVideo(i)));
function onVideo(i) {
  const p = players[i], t = tiles[i], live = p.status === "live" && p.stream;
  if (live && t.video.srcObject !== p.stream) { t.video.srcObject = p.stream; t.video.play().catch(() => {}); }
  if (!live) t.video.srcObject = null;
  t.el.classList.toggle("novideo", !live);
  t.sig.classList.toggle("live", !!live);
  t.sig.textContent = live ? "LIVE" : p.status === "connecting" ? "CONNECTING" : "NO VIDEO";
  if (p.status === "live") log(`${esc(camName(i))} video connected`, "sys");
  if (i === director.live) attachProgram();
  const n = players.filter((q) => q.status === "live").length;
  $("#nVideo").textContent = `${n}/${IDS.length}`;
  $("#dVideo").className = "dot" + (n === IDS.length ? " on" : n ? " warn" : "");
}
function attachProgram() {
  const p = players[director.live], live = p.status === "live" && p.stream ? p.stream : null;
  if (pgmVideo.srcObject !== live) { pgmVideo.srcObject = live; if (live) pgmVideo.play().catch(() => {}); }
  $("#program").classList.toggle("novideo", !live);
}
setInterval(async () => {
  let kbps = 0;
  for (const p of players) kbps += (await p.poll()).kbps;
  ui.bitrate = kbps;
  const prog = players[director.live].stats.latency;
  const any = players.map((p) => p.stats.latency).filter((x) => x != null);
  ui.measured = prog != null ? prog : any.length ? Math.round(any.reduce((a, b) => a + b, 0) / any.length) : null;
  tiles.forEach((t, i) => { const s = players[i].stats; if (players[i].status === "live") t.sig.textContent = s.latency != null ? `LIVE · ${s.latency} ms` : "LIVE"; });
}, 1000);

// ======================= AI director =======================
// Event → the camera the profile names for it (after a minimum shot length). No events → rotate idle shots,
// preferring the hands camera while the player is holding something.
const director = {
  auto: true, live: 3, lastCut: -1e9, reason: "Establishing shot", pending: null, idleIdx: 0,
  onEvent(e) { if (!this.pending || e.pri >= this.pending.pri) this.pending = e; },
  cut(i, reason, src) {
    const changed = this.live !== i;
    this.live = i; this.lastCut = performance.now() / 1000; this.reason = reason;
    if (changed || src === "manual") log(`${src === "ai" ? "AI cut" : "Manual cut"} → Cam ${i + 1} ${esc(camName(i))}`, src);
    renderTally(); attachProgram(); sendProgram();
    bridge.send({ type: "cmd", cmd: "setActiveCamera", camId: IDS[i] });
  },
  tick() {
    if (window.MV.UNITY || !this.auto || !state || (state.session && state.session.status === "ended")) { this.pending = null; return; }
    const since = performance.now() / 1000 - this.lastCut;
    if (this.pending && since > 1.5) {
      const e = this.pending; this.pending = null;
      if (e.cam) { const i = camIndex(e.cam); if (i !== this.live || e.pri >= 3) return this.cut(i, e.reason, "ai"); }
    }
    if (since > 8) {
      const hands = state.player && state.player.hands, busy = hands && ((hands.left && hands.left.grab) || (hands.right && hands.right.grab));
      let id;
      if (busy && prof.handsCam && IDS[this.live] !== prof.handsCam) id = prof.handsCam;
      else { do { id = prof.idle[this.idleIdx++ % prof.idle.length]; } while (id === IDS[this.live] && prof.idle.length > 1); }
      this.cut(camIndex(id), busy && id === prof.handsCam ? "Player is handling something" : `Variety: ${camName(camIndex(id))}`, "ai");
    }
  },
};
function sendProgram() {
  bridge.send({ type: "program", camId: IDS[director.live], camIndex: director.live, reason: director.reason, by: director.auto ? "ai" : "manual" });
}
const titleCase = (s) => s.replace(/_/g, " ").replace(/^\w/, (c) => c.toUpperCase());
function describe(ev) {
  const d = ev.data || {};
  if (ev.name === "dialogue") return `${esc(d.speaker || "NPC")}: “${esc(d.text || "")}”`;
  if (ev.name === "chapter") return `Chapter: <b>${esc(d.name || "")}</b>`;
  const extra = Object.entries(d).map(([k, v]) => `${esc(k)} ${esc(v)}`).join(", ");
  return `${esc(titleCase(ev.name))}${extra ? ` <span class="sys">(${extra})</span>` : ""}`;
}
function handleEvent(ev) {
  const def = prof.events[ev.name], d = ev.data || {};
  log(describe(ev), ev.name === "dialogue" ? "cap" : "");
  if (ev.name !== "dialogue") lastAction = titleCase(ev.name) + (d.name ? `: ${d.name}` : d.object ? `: ${d.object}` : "");
  if (ev.name === "session_end") return director.cut(camIndex(def ? def.cam : IDS[3]), "Session over", "ai");
  const what = d.object || d.part || (ev.name !== "chapter" && d.name) || (d.n && d.total ? `${d.n} of ${d.total}` : "");
  if (def && def.cam) director.onEvent({ cam: def.cam, pri: def.pri || 1, reason: ev.name === "chapter" ? `New chapter: ${d.name || ""}` : titleCase(ev.name) + (what ? `: ${what}` : "") });
}
function manualCut(i) { if (director.auto) setAuto(false); director.cut(i, "Picked by the operator", "manual"); }
function setAuto(on) {
  director.auto = on;
  $("#aiSwitch").setAttribute("aria-checked", String(on));
  $("#aiState").textContent = on ? "Auto" : "Manual";
  $("#reason").classList.toggle("off", !on);
  log(on ? "AI director on" : "AI director off · manual control", "sys");
  if (on) director.lastCut = performance.now() / 1000 - 7;
  renderTally();
}
$("#aiSwitch").addEventListener("click", () => setAuto(!director.auto));
function renderTally() {
  tiles.forEach((t, i) => t.el.classList.toggle("live", i === director.live));
  $("#pgmLabel").textContent = `Cam ${director.live + 1} · ${camName(director.live)}`;
  $("#reasonTxt").textContent = director.reason;
  const why = $("#pgmWhy");
  why.textContent = (director.auto ? "AI · " : "Manual · ") + director.reason;
  why.classList.toggle("manual", !director.auto);
}

// ======================= panels =======================
function buildChapters() { $("#chapters").innerHTML = prof.chapters.map((c) => `<li>${esc(c)}</li>`).join("") || `<li>No chapters defined</li>`; }
function buildStats() { $("#stats").innerHTML = prof.stats.map(([k, label]) => `<div><dt>${esc(label)}</dt><dd id="st-${k}">0</dd></div>`).join(""); }
function handText(h) { if (!h || h.tracked === false) return ["Not tracked", "lost"]; return h.grab ? ["Grabbing", "grab"] : ["Tracked", ""]; }
function updatePanels() {
  const s = ui.sync ? state : lastRaw;
  if (s) {
    const ses = s.session || {}, chapter = ses.chapter || "";
    $("#gChapter").textContent = chapter || prof.tagline;
    $("#clk").textContent = fmtElapsed(ses.elapsed);
    const idx = prof.chapters.indexOf(chapter);
    [...$("#chapters").children].forEach((li, i) => { li.className = idx < 0 ? "" : i < idx ? "done" : i === idx ? "now" : ""; });
    for (const [k] of prof.stats) { const el = $("#st-" + k); if (el) el.textContent = (s.stats && s.stats[k]) ?? 0; }
    const pl = s.player || {};
    $("#inputMode").textContent = pl.input ? (pl.input === "hands" ? "hand tracking" : pl.input) : "—";
    for (const [side, id] of [["left", "#handL"], ["right", "#handR"]]) {
      const [txt, cls] = handText(pl.hands && pl.hands[side]);
      $(id).className = "hand " + cls; $(id + " b").textContent = txt;
    }
    $("#lastAction").textContent = "Last action: " + lastAction;
    const live = !ses.status || ses.status === "live";
    $("#matchChip").className = "chip " + (gameUp ? (live ? "live" : "ended") : "");
    $("#matchChip").textContent = !gameUp ? "Game offline" : live ? "● Live" : "Session over";
    $("#mFrame").textContent = s.frame != null ? Number(s.frame).toLocaleString() : "—";
    if ($("details.payload").open) $("#payload").textContent = JSON.stringify(s, null, 2);
  } else {
    $("#matchChip").className = "chip"; $("#matchChip").textContent = gameUp ? "● Live" : "Game offline";
  }
  $("#mVid").textContent = ui.measured != null ? ui.measured + " ms" : "no video";
  $("#mHold").textContent = hold() + " ms";
  $("#mRtt").textContent = rtt != null ? rtt + " ms" : "—";
  $("#mHz").textContent = hz + " Hz";
  $("#mBit").textContent = (ui.bitrate / 1000).toFixed(1) + " Mb/s";
}

// ======================= pipeline controls =======================
$("#lat").addEventListener("input", (e) => { ui.latManual = +e.target.value; $("#latOut").textContent = ui.latManual + " ms"; });
$("#latAuto").addEventListener("click", (e) => { ui.latAuto = !ui.latAuto; e.currentTarget.setAttribute("aria-checked", String(ui.latAuto)); });
$("#syncSwitch").addEventListener("click", (e) => {
  ui.sync = !ui.sync; e.currentTarget.setAttribute("aria-checked", String(ui.sync));
  log(ui.sync ? "Telemetry synced to video" : "Telemetry sync off · events now arrive before the picture", "sys");
});
document.addEventListener("keydown", (e) => {
  const tag = e.target.tagName;
  if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA" || (e.target.closest && e.target.closest("summary"))) return;
  if (e.key >= "1" && e.key <= String(IDS.length)) manualCut(+e.key - 1);
  else if (e.key === "a" || e.key === "A") setAuto(!director.auto);
});

// ======================= captions & translation =======================
const caps = new Map();
const tr = { subLang: "ko", on: true, orig: true, voice: false, current: null, clearAt: 0, list: [], visible: false };
const previews = [{ name: "Mina", color: "#c4a6ff", lang: "ja" }, { name: "Diego", color: "#8fd0ff", lang: "es" }, { name: "Wei", color: "#9fe8b8", lang: "zh" }];
const wanted = () => [...new Set([tr.subLang, ...previews.map((v) => v.lang)])];
function sendLangs() { bridge.send({ type: "langs", langs: wanted() }); }
function setEngine(t) {
  if (!t) return;
  $("#tr-engine").innerHTML = t.engine === "claude"
    ? `<b>Translating with Claude</b> (${esc(t.model)}). Any language, including NPC dialogue.`
    : `<b>Offline translation.</b> Commentary in Korean, Japanese, Spanish, Chinese and English is built in. Add an API key to <code>bridge/.env</code> for other languages, NPC dialogue and free commentary.`;
}
function onCaption(m) {
  const prev = caps.get(m.id);
  const cap = Object.assign(prev || {}, m);
  if (!prev) {
    caps.set(m.id, cap); tr.list.unshift(cap);
    if (tr.list.length > 60) caps.delete(tr.list.pop().id);
    if (cap.kind !== "dialogue") log(`Caption (${cap.kind === "auto" ? "auto" : "commentator"}): ${esc(cap.text)}`, "cap");
    if (cap.kind === "commentator") showCaption(cap);
    else held.push({ due: performance.now() + hold(), msg: { type: "_showcap", id: m.id } });   // game-made: wait for the picture
  } else if (tr.current === cap) {
    tr.clearAt = Math.max(tr.clearAt, performance.now() + 4000);
    renderTr();
    if (!(cap.pending || []).length) voiceOver(cap);
  } else renderTranscript();
  if (cap.kind === "commentator") commentatorStatus(cap);
}
function showCaption(cap) {
  tr.current = cap;
  tr.clearAt = performance.now() + (cap.kind === "commentator" ? 6500 : cap.kind === "dialogue" ? 5500 : cap.pri >= 3 ? 5000 : 4000);
  renderTr();
  if (!(cap.pending || []).length) voiceOver(cap);
}
function commentatorStatus(cap) {
  const n = Object.keys(cap.translations || {}).length;
  setTrStatus((cap.pending || []).length ? "Translating…"
    : cap.error ? cap.error
    : cap.offline ? "Only the built-in languages could be translated. Add an API key to bridge/.env for the others."
    : cap.latencyMs ? `Translated into ${n} language${n === 1 ? "" : "s"} in ${cap.latencyMs} ms.` : "Built-in translation used.");
}
const setTrStatus = (t) => { $("#tr-status").textContent = t; };
function voiceOver(cap) {
  if (!tr.voice || !tr.on) return;
  const c = captionFor(cap, tr.subLang);
  if (c.kind === "ok") speakText(c.text, tr.subLang);
}
function renderCaption() {
  const cap = tr.current, vis = tr.on && !!cap && performance.now() < tr.clearAt;
  tr.visible = vis;
  $("#subs").classList.toggle("gone", !vis);
  const cc = $("#ccChip");
  cc.textContent = tr.on ? "CC · " + LANGS[tr.subLang].name : "CC off";
  cc.classList.toggle("off", !tr.on);
  if (!vis) return;
  const c = captionFor(cap, tr.subLang);
  $("#sub-s").textContent = c.text; $("#sub-s").className = "s" + (c.kind === "pending" ? " wait" : "");
  $("#sub-o").textContent = tr.orig && c.kind !== "fallback" && cap.source !== tr.subLang ? cap.text : "";
}
function renderViewers() {
  const cap = tr.current; if (!cap) return;
  previews.forEach((v, i) => {
    const c = captionFor(cap, v.lang);
    $("#vc-" + i).textContent = c.text;
    $("#vc-" + i).className = "vcap" + (c.kind === "ok" ? "" : " muted");
    $("#vm-" + i).textContent = c.kind === "ok" ? `${LANGS[v.lang].name} · ${cap.kind === "auto" ? "auto commentary" : cap.latencyMs ? cap.latencyMs + " ms" : "built-in"}`
      : c.kind === "fallback" ? "not translated, showing original" : "";
  });
}
const KIND = { auto: ["Auto", "auto"], dialogue: ["NPC", "auto"], commentator: ["Commentator", "streamer"] };
function renderTranscript() {
  const langs = wanted();
  $("#ttbl").tHead.innerHTML = `<tr><th>Time</th><th>From</th><th>Original</th>${langs.map((l) => `<th>${LANGS[l].name}</th>`).join("")}<th>Latency</th></tr>`;
  const tb = $("#ttbl").tBodies[0]; tb.textContent = "";
  for (const cap of tr.list) {
    const row = document.createElement("tr"), [kl, kc] = KIND[cap.kind] || KIND.auto;
    const cells = [[new Date(cap.t).toTimeString().slice(0, 8), "m"], [cap.kind === "dialogue" && cap.speaker ? cap.speaker : kl, "k " + kc], [cap.text, ""],
      ...langs.map((l) => { const c = captionFor(cap, l); return [c.kind === "ok" ? (cap.translations[l] || cap.text) : c.kind === "pending" ? "…" : "—", c.kind === "ok" ? "" : "m"]; }),
      [cap.kind === "auto" ? "instant" : cap.latencyMs ? cap.latencyMs + " ms" : (cap.pending || []).length ? "…" : "cached", "m"]];
    for (const [txt, cls] of cells) { const td = document.createElement("td"); td.textContent = txt; if (cls) td.className = cls; row.append(td); }
    tb.append(row);
  }
  $("#t-count").textContent = tr.list.length + (tr.list.length === 1 ? " line" : " lines");
  $("#t-empty").hidden = tr.list.length > 0;
}
function renderPacket() {
  const c = tr.current; if (!c) return;
  const { type, id, kind, game, t, speaker, source, text, translations, pending, latencyMs } = c;
  $("#cap-pkt").textContent = JSON.stringify({ type, id, kind, game, t, speaker, source, text, translations, pending, latencyMs }, null, 2);
}
function renderTr() { renderCaption(); renderViewers(); renderTranscript(); renderPacket(); }
function renderAudience(m) {
  const parts = Object.entries(m.langs || {}).map(([l, n]) => `${(LANGS[l] || { name: l }).name} ${n}`);
  $("#audience").innerHTML = m.viewers ? `<b>${m.viewers}</b> viewer page${m.viewers === 1 ? "" : "s"} connected${parts.length ? " · " + esc(parts.join(", ")) : ""}` : "No viewer pages connected";
}

$("#sub-lang").innerHTML = langOptions(tr.subLang);
$("#sub-lang").addEventListener("change", (e) => { tr.subLang = e.target.value; sendLangs(); renderTr(); });
$("#vlist").innerHTML = previews.map((v, i) => `
  <div class="vcard">
    <div class="vhead"><span class="av" style="background:${v.color}">${v.name[0]}</span><span class="vname">${v.name} <small class="note">preview</small></span>
      <select id="vl-${i}" aria-label="${v.name}'s language">${langOptions(v.lang)}</select></div>
    <div class="vcap muted" id="vc-${i}">No captions yet</div>
    <div class="vmeta" id="vm-${i}"></div>
  </div>`).join("");
previews.forEach((v, i) => $("#vl-" + i).addEventListener("change", (e) => { v.lang = e.target.value; sendLangs(); renderTr(); }));
function bindSwitch(id, fn) {
  const el = $("#" + id);
  el.addEventListener("click", () => { const v = el.getAttribute("aria-checked") !== "true"; el.setAttribute("aria-checked", String(v)); fn(v); });
}
bindSwitch("sw-subs", (v) => { tr.on = v; renderCaption(); });
bindSwitch("sw-orig", (v) => { tr.orig = v; renderCaption(); });
bindSwitch("sw-voice", (v) => { tr.voice = v; if (!v) try { speechSynthesis.cancel(); } catch {} });
function say(text) {
  if (!text) return;
  if (!bridge.connected) { setTrStatus("The bridge is offline, so the line wasn't sent."); return; }
  bridge.send({ type: "say", text }); setTrStatus("Sent. Translating…");
}
function buildQuick() {
  $("#qlines").innerHTML = "";
  for (const q of prof.quick) {
    const b = document.createElement("button"); b.type = "button"; b.className = "qline"; b.textContent = q.en;
    b.addEventListener("click", () => say(q.en)); $("#qlines").append(b);
  }
}
$("#say-form").addEventListener("submit", (e) => { e.preventDefault(); const t = $("#say").value.trim(); if (t) { say(t); $("#say").value = ""; } });

// microphone: browser speech recognition (Chrome / Edge; needs localhost or https)
const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
$("#micLang").innerHTML = langOptions("ko").replace(/ · Claude/g, "");
let rec = null, recOn = false;
if (!SR) $("#micSupport").textContent = "Typing only in this browser";
$("#micBtn").addEventListener("click", () => {
  if (!SR) { setTrStatus("This browser has no speech recognition. Use Chrome or Edge, or type instead."); return; }
  if (recOn) { recOn = false; rec && rec.stop(); return micUI(false); }
  rec = new SR();
  rec.lang = LANGS[$("#micLang").value].tts; rec.continuous = true; rec.interimResults = true;
  rec.onresult = (e) => {
    let interim = "";
    for (let i = e.resultIndex; i < e.results.length; i++) {
      const r = e.results[i];
      if (r.isFinal) say(r[0].transcript.trim()); else interim += r[0].transcript;
    }
    $("#interim").textContent = interim;
  };
  rec.onerror = (e) => {
    if (e.error === "not-allowed" || e.error === "service-not-allowed") { recOn = false; micUI(false); setTrStatus("Microphone access was blocked. Allow it in the browser's address bar, then try again."); }
  };
  rec.onend = () => { if (recOn) { try { rec.start(); } catch {} } };
  try { rec.start(); recOn = true; micUI(true); setTrStatus("Listening…"); } catch { setTrStatus("The microphone couldn't start. Open the dashboard on http://localhost or https."); }
});
$("#micLang").addEventListener("change", () => { if (recOn) { recOn = false; rec.stop(); micUI(false); setTrStatus("Speaking language changed. Start the microphone again."); } });
function micUI(on) { const b = $("#micBtn"); b.textContent = on ? "Stop microphone" : "Start microphone"; b.classList.toggle("rec", on); if (!on) $("#interim").textContent = ""; }

const vu = `${location.protocol.startsWith("http") ? location.protocol + "//" + location.host : "http://localhost:8080"}/viewer.html`;
$("#viewerUrl").textContent = vu + (/localhost|127\.0\.0\.1/.test(vu) ? "  (use this PC's network address for other devices)" : "");

// ======================= tabs =======================
function setTab(name) {
  document.body.dataset.tab = name;
  for (const b of document.querySelectorAll(".tabs [role=tab]")) b.setAttribute("aria-selected", String(b.dataset.tab === name));
  try { history.replaceState(null, "", location.search + "#" + name); } catch {}
}
document.querySelectorAll(".tabs [role=tab]").forEach((b) => b.addEventListener("click", () => setTab(b.dataset.tab)));
setTab(location.hash === "#translation" ? "translation" : "director");

// ======================= loops =======================
let lastTick = 0, lastPanel = 0;
setInterval(() => {   // keeps running when the tab is in the background
  const now = performance.now();
  while (held.length && held[0].due <= now) apply(held.shift().msg);
  if (now - lastTick > 250) { lastTick = now; director.tick(); if (window.MV.follow) window.MV.follow(director); }
  if (now - lastPanel > 150) { lastPanel = now; updatePanels(); renderTally(); }
  if (tr.visible && now >= tr.clearAt) renderCaption();
}, 30);
let lastDraw = 0;
function draw(now) {
  if (now - lastDraw > 80) {
    lastDraw = now;
    const s = ui.sync ? state : lastRaw;
    if ($("#program").classList.contains("novideo")) {
      const dpr = fitCanvas(pgmCanvas);
      drawPreview(pgmCanvas.getContext("2d"), pgmCanvas.width, pgmCanvas.height, s, { dpr, profile: prof, label: `NO VIDEO ON ${IDS[director.live].toUpperCase()} · TELEMETRY PREVIEW` });
    }
    if (document.body.dataset.tab !== "translation") tiles.forEach((t) => {
      if (!t.el.classList.contains("novideo")) return;
      const dpr = fitCanvas(t.canvas);
      drawPreview(t.canvas.getContext("2d"), t.canvas.width, t.canvas.height, s, { dpr, profile: prof });
    });
  }
  requestAnimationFrame(draw);
}
requestAnimationFrame(draw);
setGame(gameId, true);
renderTr(); updatePanels();
log("Dashboard started", "sys");
})();
