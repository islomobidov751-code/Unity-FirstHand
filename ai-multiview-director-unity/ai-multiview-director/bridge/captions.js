// Captions for the running game (profile in ../games/profiles.js):
//   auto        commentary built from game events ("Glove piece attached, 2 of 4!")
//   dialogue    NPC lines the game reports (North Star crew); the text itself gets translated
//   commentator lines typed or spoken on the dashboard
// Korean, Japanese, Spanish, Chinese and English commentary is built in. Everything else (other languages,
// dialogue, free commentator lines) uses the Claude API when ANTHROPIC_API_KEY is set in bridge/.env.
// Translations are cached in translation-cache.json, so a line the crew says every run is only paid for once.
// A caption is sent at once with what is ready, then again (same id, update:true) when more translations arrive.
const fs = require("fs");
const path = require("path");
const games = require("../games/profiles.js");

const NAMES = { ko: "Korean", ja: "Japanese", zh: "Chinese", es: "Spanish", en: "English", fr: "French", de: "German", pt: "Portuguese",
  it: "Italian", vi: "Vietnamese", th: "Thai", id: "Indonesian", hi: "Hindi", ar: "Arabic", ru: "Russian", tr: "Turkish" };
const CACHE_FILE = path.join(__dirname, "translation-cache.json");

let io = null, seq = 0, cur = null, lastAuto = 0, lastSay = 0, gameId = games.DEFAULT_ID;
const extraTpl = {};            // "game|lang" -> { templateKey: text }  (Claude-translated commentary)
const tplPending = new Map();   // "game|lang" -> Promise
const lineCache = new Map();    // "lang\ntext" -> translation
try { for (const [k, v] of Object.entries(JSON.parse(fs.readFileSync(CACHE_FILE, "utf8")))) lineCache.set(k, v); } catch {}
let saveTimer = null;
function saveCache() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    try { fs.writeFileSync(CACHE_FILE, JSON.stringify(Object.fromEntries([...lineCache].slice(-5000)), null, 1)); } catch (e) { console.warn("[captions] cache not saved:", e.message); }
  }, 1500);
}

const API_KEY = () => process.env.ANTHROPIC_API_KEY || "";
const MODEL = () => process.env.ANTHROPIC_MODEL || "claude-haiku-5-5";
const profile = () => games.get(gameId);
const fill = (tpl, vars) => (tpl ? tpl.replace(/\{(\w+)\}/g, (_, k) => String(vars[k] ?? "")) : null);
const langList = (codes) => codes.map((c) => `${c} (${NAMES[c] || c})`).join(", ");
const wire = (cap, update) => ({ ...cap, update: !!update, pending: [...cap._pending], _pending: undefined, key: undefined, vars: undefined });

function init(hooks) { io = hooks; }
function setGame(id) { if (games.PROFILES[id] && id !== gameId) { gameId = id; cur = null; } }
function engine() { return { engine: API_KEY() ? "claude" : "offline", model: API_KEY() ? MODEL() : null, builtin: ["en", "ko", "ja", "es", "zh"] }; }
function current() { return cur && Date.now() - cur.t < 8000 ? wire(cur, false) : null; }
function emit(cap, update) { cur = cap; io.broadcast(wire(cap, update)); }
function tplFor(game, key, lang) {
  const set = games.templates(games.get(game))[key] || {};
  return set[lang] || (extraTpl[`${game}|${lang}`] || {})[key] || null;
}

// ---------------- game events ----------------
function onEvent(ev) {
  const p = profile(), def = p.events[ev.name], d = ev.data || {}, now = Date.now();
  if (!def) return;
  if (def.dialogue) return dialogue(d, def);
  const key = games.templateKey(p, ev.name, d);
  if (!key) return;
  const pri = def.pri || 1;
  if (cur && cur.kind === "commentator" && now - lastSay < 3500) return;            // never talk over the commentator
  if (cur && cur.kind === "dialogue" && now - cur.t < 3000 && pri < 3) return;       // let the crew finish
  if (pri < 2 && now - lastAuto < 2600) return;
  if (cur && cur.kind === "auto" && cur.pri > pri && now - cur.t < 2500) return;
  lastAuto = now;
  const cap = { type: "caption", id: ++seq, kind: "auto", game: gameId, event: ev.name, pri, t: now, eventT: ev.srvT || now, source: "en",
    text: fill(tplFor(gameId, key, "en"), d), translations: {}, latencyMs: 0, key, vars: d, _pending: new Set() };
  const langs = io.langs();
  for (const l of langs) { const s = fill(tplFor(gameId, key, l), d); if (s) cap.translations[l] = s; }
  const missing = langs.filter((l) => !cap.translations[l]);
  if (missing.length && API_KEY()) { missing.forEach((l) => cap._pending.add(l)); emit(cap); fillTemplates(missing, cap); }
  else { cap.offline = missing.length > 0; emit(cap); }
}
async function fillTemplates(langs, cap) {
  await Promise.all(langs.map((l) => ensureTemplates(cap.game, l)));
  for (const l of langs) { const s = fill(tplFor(cap.game, cap.key, l), cap.vars); if (s) cap.translations[l] = s; cap._pending.delete(l); }
  emit(cap, true);
}
// one Claude call per (game, language): translate every commentary template of the game, then reuse them
function ensureTemplates(game, lang) {
  const id = `${game}|${lang}`;
  if (extraTpl[id] || !API_KEY()) return Promise.resolve();
  if (tplPending.has(id)) return tplPending.get(id);
  const en = Object.fromEntries(Object.entries(games.templates(games.get(game))).map(([k, set]) => [k, set.en]));
  const p = (async () => {
    const prompt = `Translate these live-stream commentary lines for the VR game "${games.get(game).title}" into ${langList([lang])}.
Keep every {placeholder} exactly as written (they are filled in later with names and numbers). Keep the game's name and proper names unchanged. Write short, natural caster lines.
${JSON.stringify(en, null, 1)}
Reply with only a JSON object with the same keys.`;
    try {
      const o = await askClaude(prompt), ok = {};
      for (const [k, v] of Object.entries(en)) {
        const ph = v.match(/\{\w+\}/g) || [];
        if (o && typeof o[k] === "string" && ph.every((x) => o[k].includes(x))) ok[k] = o[k];
      }
      extraTpl[id] = ok;
    } catch (e) { console.warn(`[captions] commentary translation for ${lang} failed: ${e.message}`); }
    tplPending.delete(id);
  })();
  tplPending.set(id, p);
  return p;
}

// ---------------- NPC dialogue ----------------
function dialogue(d, def) {
  const text = String(d.text || "").trim();
  if (!text) return;
  const now = Date.now(), source = d.lang || "en";
  const cap = { type: "caption", id: ++seq, kind: "dialogue", game: gameId, pri: def.pri || 1, t: now, eventT: now, speaker: String(d.speaker || ""),
    source, text, translations: { [source]: text }, latencyMs: 0, _pending: new Set() };
  return translateMissing(cap);
}

// ---------------- commentator lines ----------------
function say(text) {
  lastSay = Date.now();
  const quick = profile().quick.find((q) => q.en.toLowerCase() === text.toLowerCase());
  const cap = { type: "caption", id: ++seq, kind: "commentator", game: gameId, pri: 5, t: lastSay, eventT: lastSay, source: quick ? "en" : null, text,
    translations: quick ? { ...quick } : {}, latencyMs: null, _pending: new Set() };
  return translateMissing(cap);
}

async function translateMissing(cap) {
  const langs = io.langs();
  for (const l of langs) if (!cap.translations[l] && lineCache.has(l + "\n" + cap.text)) cap.translations[l] = lineCache.get(l + "\n" + cap.text);
  const missing = langs.filter((l) => !cap.translations[l] && l !== cap.source);
  if (!missing.length || !API_KEY()) { cap.latencyMs = cap.latencyMs ?? 0; cap.offline = missing.length > 0; emit(cap); return; }
  missing.forEach((l) => cap._pending.add(l));
  emit(cap);
  await translateInto(cap, missing);
}
async function translateInto(cap, langs) {
  const t0 = Date.now();
  try {
    const who = cap.kind === "dialogue" ? `a line spoken by ${cap.speaker || "a character"} in the VR game "${profile().title}"` : `a line from a commentator streaming the VR game "${profile().title}"`;
    const prompt = `Translate ${who} into subtitles. Detect its language, then translate it into each target language as short, natural subtitles. Keep names unchanged. If the line is already in a target language, return it unchanged for that language.
Target languages: ${langList(langs)}
Line: """${cap.text}"""
Reply with only JSON in this shape: {"source": "<ISO 639-1 code of the line>", "t": {"<code>": "<translation>"}}`;
    const o = await askClaude(prompt);
    if (o && o.t) {
      cap.source = cap.source || o.source || null;
      for (const l of langs) if (typeof o.t[l] === "string") { cap.translations[l] = o.t[l]; lineCache.set(l + "\n" + cap.text, o.t[l]); }
      saveCache();
    }
  } catch (e) { console.warn(`[captions] translation failed: ${e.message}`); cap.error = "Translation failed. Check the API key and internet connection."; }
  langs.forEach((l) => cap._pending.delete(l));
  cap.latencyMs = Date.now() - t0;
  emit(cap, true);
}

// a page picked a new language: translate the caption on screen right away
function onLangsAdded(langs) {
  const c = cur && Date.now() - cur.t < 8000 ? cur : null;
  if (!c) return;
  const missing = langs.filter((l) => !c.translations[l]);
  if (!missing.length) return;
  if (c.kind === "auto") {
    for (const l of missing) { const s = fill(tplFor(c.game, c.key, l), c.vars); if (s) c.translations[l] = s; }
    const rest = missing.filter((l) => !c.translations[l]);
    if (rest.length && API_KEY()) { rest.forEach((l) => c._pending.add(l)); emit(c, true); fillTemplates(rest, c); }
    else emit(c, true);
  } else {
    for (const l of missing) if (lineCache.has(l + "\n" + c.text)) c.translations[l] = lineCache.get(l + "\n" + c.text);
    const rest = missing.filter((l) => !c.translations[l]);
    if (rest.length && API_KEY()) { rest.forEach((l) => c._pending.add(l)); emit(c, true); translateInto(c, rest); }
    else emit(c, true);
  }
}

// ---------------- Claude Messages API ----------------
async function askClaude(prompt) {
  const res = await fetch("https://api.anthropic.com/v1/messages", {
    method: "POST",
    headers: { "x-api-key": API_KEY(), "anthropic-version": "2023-06-01", "content-type": "application/json" },
    body: JSON.stringify({ model: MODEL(), max_tokens: 1500, messages: [{ role: "user", content: prompt }] }),
  });
  if (!res.ok) throw new Error(`API ${res.status}: ${(await res.text()).slice(0, 200)}`);
  const j = await res.json();
  const text = (j.content || []).map((c) => c.text || "").join("");
  const a = text.indexOf("{"), b = text.lastIndexOf("}");
  if (a < 0 || b < a) throw new Error("reply had no JSON");
  return JSON.parse(text.slice(a, b + 1));
}

module.exports = { init, setGame, engine, current, onEvent, say, onLangsAdded };
