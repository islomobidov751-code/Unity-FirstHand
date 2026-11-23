// Game profiles — everything game-specific lives here, so the dashboard, viewer page, bridge and mock all stay generic.
// Used by the browser (window.MV_GAMES, served at /games.js) and by Node (require('../games/profiles.js')).
//
// To support a new game, copy a profile and change:
//   cams      the 4 spectator cameras (ids must stay cam1..cam4, they are the MediaMTX paths)
//   chapters  the sections shown as the progress list (the game sends the current one as session.chapter)
//   stats     counters the game sends in state.stats, shown in the dashboard
//   events    event name → which camera the AI director cuts to (cam), how important it is (pri 1–4),
//             and the auto-commentary line in each built-in language ({placeholders} come from event data)
//   quick     ready-made commentator lines with built-in translations
//   map       top-down outline for the telemetry preview (game units, usually meters)
(function (root) {
"use strict";

const COMMON_QUICK = [
  { en: "Who wants to try it next, chat?", ko: "채팅 여러분, 다음엔 누가 해볼래요?", ja: "チャットのみんな、次は誰がやってみる？", es: "Chat, ¿quién quiere probarlo después?", zh: "弹幕的朋友们，下一个谁来试试？" },
  { en: "Thanks for watching, don't forget to follow!", ko: "시청해 주셔서 감사해요, 팔로우 잊지 마세요!", ja: "見てくれてありがとう、フォローも忘れずに！", es: "¡Gracias por ver, no olviden seguir el canal!", zh: "感谢观看，别忘了关注哦！" },
];

const PROFILES = {
  firsthand: {
    id: "firsthand",
    title: "First Hand",
    tagline: "Hand-tracking puzzle adventure",
    accent: "#5fd4e6",
    cams: [
      { id: "cam1", name: "Player view" },
      { id: "cam2", name: "Hands close-up" },
      { id: "cam3", name: "Over the shoulder" },
      { id: "cam4", name: "Wide" },
    ],
    idle: ["cam3", "cam4", "cam1", "cam2"],   // AI director rotation when nothing is happening
    handsCam: "cam2",                          // preferred while the player is grabbing something
    chapters: ["Clock Tower", "Glove schematic", "Glove assembly", "Glove powers"],
    stats: [["grabs", "Grabs"], ["pokes", "Buttons pressed"], ["pieces", "Glove pieces"], ["blasts", "Palm blasts"]],
    map: { half: 4.5, shapes: [{ x: 0, z: 0.35, w: 2.2, d: 0.8, label: "Bench" }, { x: -3.05, z: 0, w: 0.6, d: 3, label: "Shelf" }, { x: 3.2, z: 2.6, w: 1.2, d: 1.2, label: "Lift" }] },
    events: {
      session_start: { cam: "cam4", pri: 3, say: { en: "Welcome to First Hand, an adventure with just your hands!", ko: "퍼스트 핸드에 오신 걸 환영해요, 손만으로 하는 모험!", ja: "First Handへようこそ、手だけで遊ぶ冒険！", es: "¡Bienvenidos a First Hand, una aventura solo con las manos!", zh: "欢迎来到 First Hand，一场只用双手的冒险！" } },
      chapter: { cam: "cam4", pri: 3, say: { en: "New area: {name}", ko: "새로운 구역: {name}", ja: "新エリア：{name}", es: "Nueva zona: {name}", zh: "新区域：{name}" } },
      grab: { cam: "cam2", pri: 1 },
      poke: { cam: "cam1", pri: 1 },
      lift: { cam: "cam4", pri: 2, say: { en: "The lift is on its way up the clock tower!", ko: "리프트가 시계탑을 올라갑니다!", ja: "リフトが時計塔を上っていく！", es: "¡El elevador sube por la torre del reloj!", zh: "升降梯正沿着钟楼上升！" } },
      keypad_solved: { cam: "cam1", pri: 3, say: { en: "Code accepted, the door unlocks!", ko: "코드 입력 성공, 문이 열립니다!", ja: "コード承認、扉が開いた！", es: "¡Código aceptado, la puerta se abre!", zh: "密码正确，门开了！" } },
      schematic_placed: { cam: "cam2", pri: 2, say: { en: "Schematic piece in place!", ko: "설계도 조각이 제자리에!", ja: "設計図のピースがはまった！", es: "¡Pieza del esquema en su sitio!", zh: "设计图碎片就位！" } },
      glove_piece: { cam: "cam2", pri: 2, say: { en: "Glove piece attached, {n} of {total}!", ko: "장갑 부품 장착, {n}/{total}!", ja: "グローブのパーツ装着、{n}/{total}！", es: "¡Pieza del guante colocada, {n} de {total}!", zh: "手套部件安装，{n}/{total}！" } },
      gloves_complete: { cam: "cam2", pri: 4, say: { en: "The gloves are complete. Time to power up!", ko: "장갑 완성! 이제 파워 업!", ja: "グローブ完成！パワーアップだ！", es: "¡Guantes completos, hora de activar el poder!", zh: "手套完成，准备启动能力！" } },
      palm_blast: { cam: "cam1", pri: 2, say: { en: "Palm blast!", ko: "손바닥 블래스트!", ja: "パームブラスト！", es: "¡Disparo de palma!", zh: "掌心冲击！" } },
      shield: { cam: "cam3", pri: 2, say: { en: "Shield up!", ko: "쉴드 전개!", ja: "シールド展開！", es: "¡Escudo arriba!", zh: "护盾展开！" } },
      rock_crushed: { cam: "cam2", pri: 2, say: { en: "Crushed the rock with one squeeze!", ko: "꽉 쥐어서 바위를 부쉈어요!", ja: "握りつぶして岩を砕いた！", es: "¡Aplastó la roca con un solo apretón!", zh: "用力一握，把石头捏碎了！" } },
      teleport: { cam: "cam4", pri: 1 },
      session_end: { cam: "cam4", pri: 4, say: { en: "That's First Hand. Thanks for watching!", ko: "퍼스트 핸드 체험 끝! 시청해 주셔서 감사합니다!", ja: "First Hand、これで終わり！ご視聴ありがとう！", es: "¡Eso fue First Hand, gracias por vernos!", zh: "First Hand 体验结束，感谢观看！" } },
    },
    quick: [
      { en: "Look at those hand-tracking moves!", ko: "이 핸드 트래킹 움직임 좀 보세요!", ja: "このハンドトラッキングの動き、見て！", es: "¡Miren esos movimientos con seguimiento de manos!", zh: "看看这手势追踪的操作！" },
      { en: "Building the gloves piece by piece.", ko: "장갑을 하나씩 조립하고 있어요.", ja: "グローブを一つずつ組み立て中。", es: "Armando los guantes pieza por pieza.", zh: "正在一件件组装手套。" },
      ...COMMON_QUICK,
    ],
  },

  northstar: {
    id: "northstar",
    title: "North Star",
    tagline: "Age-of-sail VR adventure",
    accent: "#f2c14e",
    cams: [
      { id: "cam1", name: "Player view" },
      { id: "cam2", name: "On deck" },
      { id: "cam3", name: "Over the shoulder" },
      { id: "cam4", name: "Sea wide" },
    ],
    idle: ["cam2", "cam4", "cam3", "cam1"],
    handsCam: "cam3",
    chapters: ["Rescued at sea", "Open water", "The storm", "Diving bell"],
    stats: [["ropes", "Ropes tied"], ["harpoons", "Harpoon shots"], ["cargo", "Cargo hauled"], ["lines", "Crew lines"]],
    map: { half: 9, shapes: [{ x: 0, z: 0, w: 3.6, d: 12, label: "Deck" }, { x: 0, z: -5, w: 3.4, d: 2, label: "Quarterdeck" }] },
    events: {
      session_start: { cam: "cam4", pri: 3, say: { en: "Welcome aboard the North Star!", ko: "노스 스타호에 승선한 걸 환영합니다!", ja: "North Star号へようこそ！", es: "¡Bienvenidos a bordo del North Star!", zh: "欢迎登上北极星号！" } },
      chapter: { cam: "cam4", pri: 3, say: { en: "Next chapter: {name}", ko: "다음 챕터: {name}", ja: "次の章：{name}", es: "Siguiente capítulo: {name}", zh: "下一章：{name}" } },
      dialogue: { cam: "cam3", pri: 1, dialogue: true },      // NPC lines: the text itself is translated (Claude, cached)
      rope_tied: { cam: "cam3", pri: 2, say: { en: "Rope secured to the cleat!", ko: "밧줄을 클리트에 단단히 묶었습니다!", ja: "ロープをクリートに固定！", es: "¡Cuerda asegurada en la cornamusa!", zh: "绳子系牢在系缆桩上！" } },
      sail_raised: { cam: "cam2", pri: 2, say: { en: "Sails up, catching the wind!", ko: "돛을 올려 바람을 받습니다!", ja: "帆を上げて風を受ける！", es: "¡Velas arriba, atrapando el viento!", zh: "扬帆起航，乘风而行！" } },
      steering: { cam: "cam3", pri: 1, say: { en: "Taking the wheel.", ko: "키를 잡았습니다.", ja: "舵を握った。", es: "Tomando el timón.", zh: "掌舵出发。" } },
      spyglass: { cam: "cam1", pri: 2, say: { en: "Spyglass up, scanning the horizon.", ko: "망원경으로 수평선을 살핍니다.", ja: "望遠鏡で水平線を見渡す。", es: "Catalejo en mano, vigilando el horizonte.", zh: "举起望远镜，眺望海平线。" } },
      harpoon_loaded: { cam: "cam1", pri: 1 },
      harpoon_fired: { cam: "cam1", pri: 3, variant: (d) => (d.hit ? "hit" : "miss"), say: {
        hit: { en: "Harpoon away, direct hit!", ko: "작살 발사, 명중!", ja: "銛を発射、命中！", es: "¡Arpón disparado, impacto directo!", zh: "鱼叉发射，正中目标！" },
        miss: { en: "Harpoon away, just missed!", ko: "작살 발사, 아깝게 빗나갔어요!", ja: "銛を発射、惜しくも外れた！", es: "¡Arpón disparado, por muy poco!", zh: "鱼叉发射，差一点就中了！" } } },
      cargo_retrieved: { cam: "cam2", pri: 2, say: { en: "Cargo hauled aboard!", ko: "화물을 끌어올렸습니다!", ja: "積荷を引き上げた！", es: "¡Carga subida a bordo!", zh: "货物拉上船了！" } },
      diving_bell: { cam: "cam4", pri: 3, say: { en: "Into the diving bell, heading down!", ko: "잠수종을 타고 바다 아래로!", ja: "潜水鐘で海の底へ！", es: "¡A la campana de buceo, rumbo al fondo!", zh: "进入潜水钟，开始下潜！" } },
      weather: { cam: "cam4", pri: 3, variant: (d) => (d.kind === "storm" ? "storm" : "calm"), say: {
        storm: { en: "A storm is rolling in!", ko: "폭풍이 몰려옵니다!", ja: "嵐が近づいてきた！", es: "¡Se acerca una tormenta!", zh: "暴风雨要来了！" },
        calm: { en: "The sea calms down.", ko: "바다가 잔잔해집니다.", ja: "海が穏やかになった。", es: "El mar se calma.", zh: "海面恢复平静。" } } },
      session_end: { cam: "cam4", pri: 4, say: { en: "That's North Star. Thanks for sailing with us!", ko: "노스 스타 항해 끝! 함께해 주셔서 감사합니다!", ja: "North Starの航海はここまで。ご乗船ありがとう！", es: "¡Eso fue North Star, gracias por navegar con nosotros!", zh: "北极星号航行结束，感谢同行！" } },
    },
    quick: [
      { en: "What a view of the open sea!", ko: "탁 트인 바다 풍경, 멋지네요!", ja: "広い海の景色、最高！", es: "¡Qué vista del mar abierto!", zh: "开阔的海景太美了！" },
      { en: "Hold on, the waves are getting bigger!", ko: "꽉 잡아요, 파도가 거세지고 있어요!", ja: "つかまって、波が大きくなってきた！", es: "¡Agárrense, las olas están creciendo!", zh: "抓紧了，浪越来越大了！" },
      ...COMMON_QUICK,
    ],
  },

  // Not part of the midterm demo yet. Mixed reality: spectator cameras only see virtual objects (no passthrough),
  // so add a real webcam as one of the feeds when you get to this game.
  crypticcabinet: {
    id: "crypticcabinet",
    title: "Cryptic Cabinet",
    tagline: "Mixed-reality escape room",
    accent: "#c4a6ff",
    cams: [
      { id: "cam1", name: "Player view" },
      { id: "cam2", name: "Hands close-up" },
      { id: "cam3", name: "Room webcam" },
      { id: "cam4", name: "Cabinet" },
    ],
    idle: ["cam3", "cam4", "cam1", "cam2"],
    handsCam: "cam2",
    chapters: [],
    stats: [["puzzles", "Puzzles solved"], ["hints", "Hints used"]],
    map: { half: 3, shapes: [] },
    events: {
      session_start: { cam: "cam3", pri: 3, say: { en: "Welcome to Cryptic Cabinet!", ko: "크립틱 캐비닛에 오신 걸 환영해요!", ja: "Cryptic Cabinetへようこそ！", es: "¡Bienvenidos a Cryptic Cabinet!", zh: "欢迎来到 Cryptic Cabinet！" } },
      chapter: { cam: "cam4", pri: 3, say: { en: "Next puzzle: {name}", ko: "다음 퍼즐: {name}", ja: "次の謎：{name}", es: "Siguiente acertijo: {name}", zh: "下一个谜题：{name}" } },
      puzzle_solved: { cam: "cam4", pri: 3, say: { en: "Puzzle solved!", ko: "퍼즐 해결!", ja: "謎が解けた！", es: "¡Acertijo resuelto!", zh: "谜题解开了！" } },
      session_end: { cam: "cam3", pri: 4, say: { en: "The cabinet is open. Thanks for watching!", ko: "캐비닛이 열렸습니다! 시청해 주셔서 감사합니다!", ja: "キャビネットが開いた！ご視聴ありがとう！", es: "¡El gabinete está abierto, gracias por vernos!", zh: "柜子打开了，感谢观看！" } },
    },
    quick: COMMON_QUICK,
  },
};

const DEFAULT_ID = "firsthand";
const get = (id) => PROFILES[id] || PROFILES[DEFAULT_ID];

// all commentary templates of a profile as flat {key: {lang: text}} (used for translating them into extra languages)
function templates(p) {
  const out = {};
  for (const [name, ev] of Object.entries(p.events)) {
    if (!ev.say) continue;
    if (ev.variant) for (const [v, set] of Object.entries(ev.say)) out[`${name}.${v}`] = set;
    else out[name] = ev.say;
  }
  return out;
}
// which template an event uses ("harpoon_fired.hit", "chapter", …) or null
function templateKey(p, name, data) {
  const ev = p.events[name];
  if (!ev || !ev.say) return null;
  return ev.variant ? `${name}.${ev.variant(data || {})}` : name;
}

const api = { PROFILES, DEFAULT_ID, get, templates, templateKey, ids: Object.keys(PROFILES) };
if (typeof module !== "undefined" && module.exports) module.exports = api;
else root.MV_GAMES = api;
})(typeof window !== "undefined" ? window : globalThis);
