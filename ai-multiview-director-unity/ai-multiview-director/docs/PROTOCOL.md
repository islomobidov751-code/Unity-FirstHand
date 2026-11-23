# Message protocol

All messages are JSON over WebSocket to the bridge (`ws://<bridge-pc>:8080/<role>`).
Video never goes through the bridge. It goes game → MediaMTX (`cam1`…`cam4`) → browser.

| Endpoint | Who connects |
|---|---|
| `/game` | The game (Unity `TelemetryBridge.cs`, or `mock/arena.html`) |
| `/dashboard` | Director dashboards |
| `/viewer` | Viewer pages |

Times (`t`) are Unix milliseconds. The bridge adds `srvT` (its receive time) to everything it relays from the game.
Event names, camera names and commentary per game are defined in `games/profiles.js` (served at `/games.js`).

---

## Game → bridge

### `state` (20 times a second)
```json
{
  "type": "state", "game": "northstar", "seq": 812, "t": 1729650000000, "frame": 7290,
  "session": { "status": "live", "elapsed": 312.4, "chapter": "Open water" },
  "player": {
    "pos": [0.2, 1.0, 5.6], "yaw": 0.35, "input": "hands",
    "hands": {
      "left":  { "tracked": true, "grab": false, "pos": [-0.05, 2.6, 5.9] },
      "right": { "tracked": true, "grab": true,  "pos": [0.31, 2.5, 5.9] }
    }
  },
  "stats": { "ropes": 1, "harpoons": 0, "cargo": 0, "lines": 4 },
  "activeCam": "cam2"
}
```
- `game`: profile id (`firsthand`, `northstar`, `crypticcabinet`). The dashboard and viewers switch to that profile.
- `session.status`: `"live"` or `"ended"`. The AI director only cuts while live. `chapter` drives the progress list.
- `player.pos`: feet position `[x, y, z]` in meters; `yaw`: heading in radians (0 = +z, π/2 = +x).
- `hands.*.grab`: true while holding something. The AI director prefers the hands camera then.
- `stats`: free-form counters; the profile's `stats` list decides which ones the dashboard shows.

### `event` (when something happens)
```json
{ "type": "event", "seq": 813, "t": 1729650000040, "name": "harpoon_fired", "data": { "hit": true } }
```

**First Hand** (`firsthand`)

| `name` | `data` | Camera | Commentary (en) |
|---|---|---|---|
| `session_start` | – | Wide | Welcome to First Hand, an adventure with just your hands! |
| `chapter` | `{name}` | Wide | New area: {name} |
| `grab` | `{object}` | Hands | – |
| `poke` | `{name}` | Player view | – |
| `lift` | – | Wide | The lift is on its way up the clock tower! |
| `keypad_solved` | – | Player view | Code accepted, the door unlocks! |
| `schematic_placed` | `{n,total}` | Hands | Schematic piece in place! |
| `glove_piece` | `{n,total,part}` | Hands | Glove piece attached, {n} of {total}! |
| `gloves_complete` | – | Hands | The gloves are complete. Time to power up! |
| `palm_blast` | – | Player view | Palm blast! |
| `shield` | – | Over the shoulder | Shield up! |
| `rock_crushed` | – | Hands | Crushed the rock with one squeeze! |
| `teleport` | – | Wide | – |
| `session_end` | – | Wide | That's First Hand. Thanks for watching! |

**North Star** (`northstar`)

| `name` | `data` | Camera | Commentary (en) |
|---|---|---|---|
| `session_start` | – | Sea wide | Welcome aboard the North Star! |
| `chapter` | `{name}` | Sea wide | Next chapter: {name} |
| `dialogue` | `{speaker, text, lang?}` | Over the shoulder | *the line itself, translated* |
| `rope_tied` | – | Over the shoulder | Rope secured to the cleat! |
| `sail_raised` | – | On deck | Sails up, catching the wind! |
| `steering` | – | Over the shoulder | Taking the wheel. |
| `spyglass` | – | Player view | Spyglass up, scanning the horizon. |
| `harpoon_loaded` | – | Player view | – |
| `harpoon_fired` | `{hit}` | Player view | Harpoon away, direct hit! / just missed! |
| `cargo_retrieved` | – | On deck | Cargo hauled aboard! |
| `diving_bell` | – | Sea wide | Into the diving bell, heading down! |
| `weather` | `{kind: "storm"\|"calm"}` | Sea wide | A storm is rolling in! / The sea calms down. |
| `session_end` | – | Sea wide | That's North Star. Thanks for sailing with us! |

Events not in the profile are shown in the event log and otherwise ignored. To add one, add it to the profile.

## Bridge → game

```json
{ "type": "cmd", "cmd": "setActiveCamera", "camId": "cam2", "srvT": 1729650001000 }
```
The director cut to this camera. The game may render it at full rate (`MultiViewCameraRig > Prioritise Active Camera`).

---

## Dashboard / viewer → bridge

| Message | From | Meaning |
|---|---|---|
| `{"type":"langs","langs":["ko","fr"]}` | dashboard, viewer | Subtitle languages wanted. The bridge translates into the union of all pages' languages. |
| `{"type":"cmd", …}` | dashboard | Forwarded to the game. |
| `{"type":"program","camId":"cam2","camIndex":1,"reason":"Rope tied","by":"ai"}` | dashboard | On-air camera changed; viewers switch to it. |
| `{"type":"say","text":"와, 대박!"}` | dashboard | A commentator line (typed or from the microphone). |
| `{"type":"ping","id":123.4}` | any | Answered with `pong`. |

## Bridge → dashboard / viewer

| Message | Meaning |
|---|---|
| `hello` | On connect: `{ id, role, game: "connected"\|"disconnected", gameId, translation, program }`, then the last `state` and current caption. |
| `state`, `event` | Relayed from the game, plus `srvT`. |
| `game` | `{ id }`: the connected game changed. |
| `status` | `{ game: "connected" \| "disconnected" }` |
| `program` | Current on-air camera. |
| `audience` | Dashboards only: `{ viewers: 3, langs: { "ko": 2, "fr": 1 } }` |
| `caption` | See below. |

### `caption`
```json
{
  "type": "caption", "id": 41, "kind": "dialogue", "game": "northstar", "pri": 1, "t": 1729650000045,
  "speaker": "Captain", "source": "en", "text": "Hold fast! Here comes the storm!",
  "translations": { "en": "Hold fast! Here comes the storm!", "ko": "꽉 잡아! 폭풍이 온다!" },
  "pending": ["fr"], "latencyMs": 0, "update": false
}
```
- `kind`: `"auto"` (commentary from an event), `"dialogue"` (NPC line), `"commentator"` (dashboard mic/typing).
  Pages hold `auto` and `dialogue` back by the video delay so they appear with the picture; `commentator` shows at once.
- Sent immediately with what's ready; when the API finishes, the same `id` is sent again with `"update": true`.
  `pending` lists languages still being translated, `offline: true` means some could not be (no API key).
- Pages show dialogue as `Speaker: text`.
