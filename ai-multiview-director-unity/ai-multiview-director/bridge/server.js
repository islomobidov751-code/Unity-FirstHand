// AI MultiView Director — bridge server
// ------------------------------------------------------------------------------------------
// One process does three jobs:
//   1. Serves the web pages:  /            director dashboard
//                             /viewer.html viewer page (program feed + subtitles)
//                             /mock/       browser mock games (First Hand / North Star stand-ins)
//                             /games.js    game profiles (../games/profiles.js)
//   2. Relays WebSocket messages between the game and every web page:
//        ws://HOST:8080/game       the game (Unity build or the mock arena)
//        ws://HOST:8080/dashboard  director dashboards
//        ws://HOST:8080/viewer     viewer pages
//   3. Builds captions: auto commentary from game events + commentator lines, translated into
//      every language a dashboard or viewer asked for (built-in templates, Claude API optional).
//
// Run:  npm install  then  npm start      (settings: copy .env.example to .env)
// Message formats: see ../docs/PROTOCOL.md

const http = require("http");
const fs = require("fs");
const path = require("path");
const { WebSocketServer } = require("ws");

loadEnv(path.join(__dirname, ".env"));
const captions = require("./captions");
const games = require("../games/profiles.js");

const PORT = Number(process.env.PORT || 8080);
const ROOT = path.resolve(__dirname, "..");
const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
  ".json": "application/json", ".png": "image/png", ".svg": "image/svg+xml", ".ico": "image/x-icon" };

// ---------------------------------------------------------------- state
const clients = { game: new Set(), dashboard: new Set(), viewer: new Set() };
let lastState = null;
let gameId = games.DEFAULT_ID;
let program = { type: "program", camId: "cam4", camIndex: 3, reason: "Establishing shot", by: "ai", t: Date.now() };
const stats = { state: 0, event: 0, cmd: 0, caption: 0, dropped: 0, started: new Date().toISOString() };
let nextId = 1;

// ---------------------------------------------------------------- http (static files + status)
const server = http.createServer((req, res) => {
  const url = new URL(req.url, "http://x");
  if (url.pathname === "/status") {
    res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
    return res.end(JSON.stringify({ game: clients.game.size, dashboards: clients.dashboard.size, viewers: clients.viewer.size,
      game: gameId, translation: captions.engine(), languages: requestedLangs(), program, stats, lastState }, null, 2));
  }
  if (url.pathname === "/games.js") {
    return fs.readFile(path.join(ROOT, "games", "profiles.js"), (err, data) => {
      res.writeHead(err ? 404 : 200, { "Content-Type": MIME[".js"], "Cache-Control": "no-cache" });
      res.end(err ? "" : data);
    });
  }
  let rel = decodeURIComponent(url.pathname);
  let base = path.join(ROOT, "dashboard");
  if (rel.startsWith("/mock/")) { base = path.join(ROOT, "mock"); rel = rel.slice(5); }
  if (rel.endsWith("/")) rel += "index.html";
  const file = path.resolve(base, "." + rel);
  if (!file.startsWith(base)) { res.writeHead(403); return res.end("Forbidden"); }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404, { "Content-Type": "text/plain" }); return res.end("Not found"); }
    res.writeHead(200, { "Content-Type": MIME[path.extname(file)] || "application/octet-stream", "Cache-Control": "no-cache" });
    res.end(data);
  });
});

// ---------------------------------------------------------------- websockets
const wss = new WebSocketServer({ server });

function send(ws, obj) {
  if (ws.readyState !== ws.OPEN) return;
  const s = typeof obj === "string" ? obj : JSON.stringify(obj);
  // a slow page must never make everyone else lag: drop telemetry for it, never events or captions
  if (ws.bufferedAmount > 1_000_000 && s.startsWith('{"type":"state"')) { stats.dropped++; return; }
  ws.send(s);
}
function broadcast(roles, obj) {
  const s = JSON.stringify(obj);
  for (const r of roles) for (const ws of clients[r]) send(ws, s);
}
const toPages = (obj) => broadcast(["dashboard", "viewer"], obj);

function requestedLangs() {
  const set = new Set(["en"]);
  for (const r of ["dashboard", "viewer"]) for (const ws of clients[r]) for (const l of ws.langs || []) set.add(l);
  return [...set];
}
function audience() {
  const langs = {};
  for (const ws of clients.viewer) for (const l of ws.langs || []) langs[l] = (langs[l] || 0) + 1;
  broadcast(["dashboard"], { type: "audience", viewers: clients.viewer.size, langs });
}

captions.init({
  broadcast: (cap) => { stats.caption++; toPages(cap); },
  langs: requestedLangs,
});
function setGame(id) {
  if (!id || id === gameId || !games.PROFILES[id]) return;
  gameId = id; captions.setGame(id);
  console.log(`[bridge] game is now ${games.get(id).title}`);
  toPages({ type: "game", id });
}

wss.on("connection", (ws, req) => {
  const role = new URL(req.url, "http://x").pathname.replace(/^\/+|\/+$/g, "");
  if (!clients[role]) { ws.close(1008, "Connect to /game, /dashboard or /viewer"); return; }
  ws.id = nextId++; ws.role = role; ws.langs = []; ws.isAlive = true;
  clients[role].add(ws);
  ws.on("pong", () => (ws.isAlive = true));
  console.log(`[bridge] ${role} #${ws.id} connected (${clients[role].size} ${role})`);

  if (role === "game") toPages({ type: "status", game: "connected" });
  else {
    send(ws, { type: "hello", id: ws.id, role, game: clients.game.size ? "connected" : "disconnected", gameId, translation: captions.engine(), program });
    if (lastState) send(ws, lastState);
    const cur = captions.current(); if (cur) send(ws, cur);
  }
  if (role === "viewer") audience();

  ws.on("message", (raw) => {
    let m; try { m = JSON.parse(raw); } catch { return; }
    if (!m || typeof m !== "object") return;
    if (m.type === "ping") return send(ws, { type: "pong", id: m.id, srvT: Date.now() });

    if (role === "game") {
      m.srvT = Date.now();
      if (m.type === "state") { setGame(m.game); lastState = m; stats.state++; toPages(m); }
      else if (m.type === "event") { stats.event++; toPages(m); captions.onEvent(m); }
      return;
    }
    if (m.type === "langs" && Array.isArray(m.langs)) {
      const before = new Set(requestedLangs());
      ws.langs = m.langs.filter((l) => typeof l === "string" && /^[a-z]{2,3}(-[A-Za-z]{2,4})?$/.test(l)).slice(0, 12);
      const added = requestedLangs().filter((l) => !before.has(l));
      if (added.length) captions.onLangsAdded(added);
      if (role === "viewer") audience();
      return;
    }
    if (role !== "dashboard") return; // viewers only pick languages
    if (m.type === "cmd") { stats.cmd++; m.srvT = Date.now(); broadcast(["game"], m); }
    else if (m.type === "program") {
      program = { type: "program", camId: String(m.camId), camIndex: m.camIndex, reason: String(m.reason || ""), by: m.by === "manual" ? "manual" : "ai", t: Date.now(), origin: ws.id };
      toPages(program);
    } else if (m.type === "say" && typeof m.text === "string" && m.text.trim()) captions.say(m.text.trim().slice(0, 300));
  });

  ws.on("close", () => {
    clients[role].delete(ws);
    console.log(`[bridge] ${role} #${ws.id} disconnected`);
    if (role === "game") toPages({ type: "status", game: clients.game.size ? "connected" : "disconnected" });
    if (role === "viewer") audience();
  });
});

// drop dead connections (a crashed game never sends a close frame)
setInterval(() => {
  for (const ws of wss.clients) { if (!ws.isAlive) { ws.terminate(); continue; } ws.isAlive = false; ws.ping(); }
}, 5000);

server.listen(PORT, () => {
  const ips = Object.values(require("os").networkInterfaces()).flat().filter((i) => i && i.family === "IPv4" && !i.internal).map((i) => i.address);
  console.log(`\n  AI MultiView Director bridge running on port ${PORT}`);
  console.log(`  Dashboard   http://localhost:${PORT}/`);
  console.log(`  Viewer      http://localhost:${PORT}/viewer.html`);
  console.log(`  Mock games  http://localhost:${PORT}/mock/arena.html?game=firsthand   (or ?game=northstar)`);
  if (ips.length) console.log(`  On your network: ${ips.map((ip) => `http://${ip}:${PORT}/`).join("  ")}`);
  console.log(`  Game connects to ws://<this-pc>:${PORT}/game`);
  console.log(`  Translation: ${captions.engine().engine === "claude" ? "Claude API (" + captions.engine().model + ")" : "offline (built-in languages only; add ANTHROPIC_API_KEY to .env for more)"}\n`);
});

// tiny .env reader so the project needs no extra packages
function loadEnv(file) {
  if (!fs.existsSync(file)) return;
  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/);
    if (m && !line.trim().startsWith("#") && process.env[m[1]] === undefined) process.env[m[1]] = m[2].replace(/^["']|["']$/g, "");
  }
}
