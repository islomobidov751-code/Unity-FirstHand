// Free camera sliders and replay player, added to the right column. Needs the new StreamServer.cs (/free and /replay/... addresses).
(() => {
"use strict";
const MV = window.MV, U = () => MV.CONFIG.media;
const $ = (s, r = document) => r.querySelector(s);
const el = (tag, attrs = {}, ...kids) => { const e = document.createElement(tag); Object.assign(e, attrs); kids.forEach((k) => e.append(k)); return e; };

function init() {
  const side = $(".side"); if (!side) return;
  const css = el("style", { textContent: `
    .xt{display:grid;gap:8px}.xt label{display:grid;grid-template-columns:70px 1fr;gap:8px;align-items:center;font-size:12px}
    .xt button{cursor:pointer;padding:6px 10px;border-radius:8px;border:1px solid #2a3340;background:#18202b;color:inherit;font:inherit;font-size:12px}
    .xt button:hover{border-color:#e8a33d}.xt .row2{display:flex;gap:8px;flex-wrap:wrap}
    #rpList{display:grid;gap:6px;max-height:210px;overflow:auto}.rpi{display:flex;justify-content:space-between;gap:8px;align-items:center;font-size:12px}
    .rpi small{opacity:.7}#rpOver{position:absolute;inset:0;background:#000;display:none;z-index:5}
    #rpOver canvas{width:100%;height:100%;object-fit:contain;display:block}
    #rpBar{position:absolute;left:8px;right:8px;bottom:8px;display:flex;gap:8px;align-items:center;font-size:12px}
    #rpBar .fill{height:4px;background:#e8a33d;width:0;flex:1 1 0}` });
  document.head.append(css);

  // ---- free camera ----
  const fc = el("div", { className: "panel xt" });
  fc.append(el("h2", { textContent: "Free camera", style: "margin:0" }));
  const vals = { yaw: 0, pitch: 20, dist: 3 };
  const mk = (name, min, max, step) => {
    const r = el("input", { type: "range", min, max, step, value: vals[name] });
    fc.append(el("label", {}, name[0].toUpperCase() + name.slice(1), r));
    return r;
  };
  const sy = mk("yaw", -180, 180, 1), sp = mk("pitch", -10, 80, 1), sd = mk("dist", 1, 8, 0.1);
  let last = 0, timer = 0;
  const send = () => {
    vals.yaw = +sy.value; vals.pitch = +sp.value; vals.dist = +sd.value; MV.fast = "FREE_ORBIT";
    fetch(`${U()}/free?yaw=${vals.yaw}&pitch=${vals.pitch}&dist=${vals.dist}`, { cache: "no-store" }).catch(() => {});
  };
  const throttled = () => { const now = Date.now(); clearTimeout(timer); if (now - last > 100) { last = now; send(); } else timer = setTimeout(() => { last = Date.now(); send(); }, 100); };
  [sy, sp, sd].forEach((r) => r.addEventListener("input", throttled));
  const watch = el("button", { textContent: "Watch free camera" });
  watch.onclick = () => {
    MV.fast = "FREE_ORBIT";
    const tile = [...document.querySelectorAll("#multiview .tile")].find((t) => /free\s*orbit/i.test(t.textContent));
    if (tile) tile.click(); else alert("No FREE_ORBIT camera yet. Add it in Unity first.");
  };
  fc.append(el("div", { className: "row2" }, watch));
  fc.append(el("small", { textContent: "One shared camera: the last person to move it wins.", style: "opacity:.7;font-size:11px" }));

  // ---- replays ----
  const rp = el("div", { className: "panel xt" });
  const saveBtn = el("button", { textContent: "Save replay now" });
  const list = el("div", { id: "rpList" });
  rp.append(el("h2", { textContent: "Replays", style: "margin:0" }), el("div", { className: "row2" }, saveBtn), list);
  side.append(fc, rp);

  // overlay on the main picture: only this viewer sees it
  const pgm = $("#program"); if (pgm) pgm.style.position = "relative";
  const cv = el("canvas", { width: 640, height: 360 });
  const fill = el("div", { className: "fill" }), info = el("span"), back = el("button", { textContent: "Back to live" });
  const bar = el("div", { id: "rpBar" }, back, fill, info), over = el("div", { id: "rpOver" }, cv, bar);
  (pgm || document.body).append(over);
  let playing = 0;
  back.onclick = () => { playing++; over.style.display = "none"; };

  async function play(c) {
    const my = ++playing; over.style.display = "block"; info.textContent = "Loading..."; fill.style.width = "0";
    const imgs = new Array(c.frames);
    let loaded = 0;
    await Promise.all([...Array(c.frames).keys()].map((n) => new Promise((ok) => {
      const im = new Image(); im.crossOrigin = "anonymous";
      im.onload = im.onerror = () => { loaded++; info.textContent = `Loading ${Math.round(loaded * 100 / c.frames)}%`; ok(); };
      im.src = `${U()}/replay/frame/${c.id}/${n}`; imgs[n] = im;
    })));
    if (my !== playing) return;
    const ctx = cv.getContext("2d"), ms = 1000 / (c.fps || 12);
    for (let n = 0; n < c.frames && my === playing; n++) {
      const im = imgs[n];
      if (im.naturalWidth) { if (cv.width !== im.naturalWidth) { cv.width = im.naturalWidth; cv.height = im.naturalHeight; } ctx.drawImage(im, 0, 0); }
      fill.style.width = ((n + 1) * 100 / c.frames) + "%"; info.textContent = `${c.label} · ${n + 1}/${c.frames}`;
      await new Promise((r) => setTimeout(r, ms));
    }
    if (my === playing) { info.textContent = "Replay finished"; }
  }

  async function refresh() {
    try {
      const clips = await (await fetch(`${U()}/replay/list`, { cache: "no-store" })).json();
      list.textContent = "";
      if (!clips.length) list.append(el("small", { textContent: "No clips yet. They appear after a game event.", style: "opacity:.7" }));
      clips.forEach((c) => {
        const b = el("button", { textContent: "Play" }); b.onclick = () => play(c);
        list.append(el("div", { className: "rpi" }, el("span", { innerHTML: "" }), b));
        const row = list.lastChild.firstChild;
        row.append(`${c.label} `, el("small", { textContent: `${c.scene} · ${c.age}s ago` }));
      });
    } catch (e) { /* Unity not ready yet */ }
  }
  saveBtn.onclick = async () => { saveBtn.disabled = true; try { await fetch(`${U()}/replay/save?label=${encodeURIComponent("viewer clip")}`); } catch (e) {} setTimeout(() => { saveBtn.disabled = false; refresh(); }, 1500); };
  refresh(); setInterval(refresh, 3000);
}
if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init); else init();
})();
