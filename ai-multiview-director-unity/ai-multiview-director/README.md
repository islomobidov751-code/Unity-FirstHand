# AI MultiView Director

Live multi-camera streaming of single-player VR games (**First Hand** and **North Star**, Cryptic Cabinet later) to a
web director dashboard, with an AI auto-director, telemetry kept in sync with the video, and live translated
subtitles for viewers.

```
                              VIDEO  (WebRTC, ~150–400 ms)
 VR game on PC ── cam1..cam4 ──► MediaMTX ───────────────────────► Dashboard  (director)
 (Unity + Quest Link,           :8889                          └─► Viewer page (audience)
  or the mock game)
        │                    TELEMETRY + EVENTS  (WebSocket, ~5 ms)
        └── ws://…:8080/game ─► Bridge (Node.js) ─────────────────► Dashboard / Viewer
            ◄── setActiveCamera ───┘   └ commentary + NPC dialogue → subtitles in every viewer's language
```

The game runs on the PC (Unity editor with Quest Link, or a Windows build) so it can render 4 extra spectator cameras
and stream them. Everything game-specific (cameras, events, commentary, panels) lives in **`games/profiles.js`**, so the
same dashboard works for every game. It switches automatically to whichever game is connected.

---

## What's in this folder

| Folder / file | What it is |
|---|---|
| `start-bridge.bat` / `.sh` | Starts the bridge (installs its packages the first time) |
| `start-mediamtx.bat` / `.sh` | Starts the MediaMTX video server (after you download it, see below) |
| `games/profiles.js` | **Game profiles**: First Hand, North Star, Cryptic Cabinet. Cameras, events, commentary (5 languages), stats |
| `bridge/` | Node.js server: serves the pages, relays game ↔ dashboard messages, builds subtitles and translations |
| `dashboard/index.html` | **Director dashboard**: program monitor, 4-camera multiview, AI director, session/player panels, Translation tab |
| `dashboard/viewer.html` | **Viewer page**: what the audience watches. Program feed + subtitles in the language each viewer picks |
| `mock/arena.html` | **Mock games**: scripted First Hand and North Star sessions that stream 4 cameras + telemetry exactly like Unity |
| `unity/` | C# scripts for the real games: telemetry, event reporter, spectator cameras, camera rig, FFmpeg video streamer |
| `mediamtx/` | Put the MediaMTX program here. Its default settings already match this project |
| `docs/UNITY_SETUP.md` | Step-by-step: adding the scripts to the First Hand and North Star Unity projects |
| `docs/PROTOCOL.md` | Every message format (game ↔ bridge ↔ pages) |

---

## 1. Install (one time)

1. **Node.js 18 or newer**: <https://nodejs.org> → download **LTS** → install with the defaults.
2. **MediaMTX**: open <https://github.com/bluenviron/mediamtx/releases>, download the newest file for your system
   (Windows: `mediamtx_vX.Y.Z_windows_amd64.zip`), and unzip it **into the `mediamtx` folder** of this project.
3. **Chrome or Edge**: needed for the microphone. Everything else also works in Firefox/Safari.
4. **FFmpeg**: needed by the Unity video streamer. Windows: `winget install Gyan.FFmpeg`.

## 2. Rehearse the demo without the headset (5 minutes)

1. Double-click **`start-mediamtx.bat`**. Leave it open.
2. Double-click **`start-bridge.bat`**. Leave it open. It prints the addresses to use.
3. In Chrome, open each page **in its own window** (browsers slow down hidden tabs):
   - Mock game: <http://localhost:8080/mock/arena.html?game=firsthand> (or `?game=northstar`). All 4 cameras say **PUBLISHING**.
   - Dashboard: <http://localhost:8080/>. Video in all tiles, and the AI director cuts on game moments.
   - Viewer: <http://localhost:8080/viewer.html>. The on-air camera with subtitles; pick a language at the top.

The mock sessions loop (First Hand ~1.5 min, North Star ~1.5 min). Switch games with the menu at the top of the mock page;
the dashboard and viewer follow automatically.

What to try on the dashboard:
- Click a tile or press `1`–`4` to cut. `A` switches the AI director back on.
- **Pipeline**: turn *Sync telemetry to video* off and watch events and subtitles arrive before the picture.
- **Translation** tab: *Start microphone* and talk (or type). In North Star the crew's lines become subtitles too.

> No MediaMTX yet? Everything still runs: tiles show a **telemetry preview** (top-down map of the player, view
> direction and hands) instead of video.

## 3. Connect the real games

Follow **[docs/UNITY_SETUP.md](docs/UNITY_SETUP.md)**. In short, for each game's Unity project:
1. Copy `unity/*.cs` into `Assets/MultiView/`.
2. Add `TelemetryBridge` (set **Game Id** to `firsthand` or `northstar`), plus a `SpectatorRig` with
   `MultiViewCameraRig`, `SpectatorCameras` and `FFmpegStreamer` and 4 cameras.
3. Report moments: wire existing UnityEvents to `GameEventReporter` in the Inspector, or add one line of code such as
   `TelemetryBridge.Report("rope_tied");`. For North Star, also `TelemetryBridge.ReportDialogue(speaker, text)` where the crew talks.
4. Close the mock page, connect the Quest with Link, press Play.

## 4. Demo day on several computers

Usually the **game PC** runs Unity + MediaMTX + bridge, and other laptops only open web pages.
1. The bridge prints the game PC's address on start (`On your network: http://192.168.x.x:8080/`).
2. In `mediamtx/mediamtx.yml` change `webrtcAdditionalHosts: []` to `webrtcAdditionalHosts: [192.168.x.x]`.
3. Allow these ports in the game PC's firewall: **8080**, **8889**, **8189/UDP**, **8554**.
4. Other laptops open `http://192.168.x.x:8080/` (dashboard) or `http://192.168.x.x:8080/viewer.html` (audience).
5. The microphone works only on `http://localhost` or `https://`, so use the Translation tab's mic on the game PC.

Encoding 4 cameras costs CPU/GPU on the game PC. If the headset frame rate drops, use NVENC (`FFmpegStreamer > Use Nvenc`)
or lower the rig to 960×540.

## 5. Translation

| What | Languages | How | Cost |
|---|---|---|---|
| Auto commentary (from game events) | Korean, Japanese, Spanish, Chinese, English | Built in (`games/profiles.js`) | Free, offline |
| Auto commentary | any other language | Claude API, translated once per game per language | API key |
| NPC dialogue (North Star crew) | all except the line's own language | Claude API, cached in `bridge/translation-cache.json` | Each line paid once, then free |
| Commentator (mic / typed) | all | Claude API; the profile's quick lines are built in | API key |

To turn on full translation: copy `bridge/.env.example` to `bridge/.env`, set `ANTHROPIC_API_KEY=` (from
<https://console.anthropic.com>), restart the bridge. It prints `Translation: Claude API`. Each line is translated once for
all requested languages and sent to every viewer, so 100 viewers cost the same as 1. Never commit `.env`.
Tip: run each game once with the key before the exam. The cache then holds every crew line, and the demo works even offline.

## 6. Ports

| Port | Used by |
|---|---|
| 8080 | Bridge: pages, `/games.js`, WebSockets (`/game`, `/dashboard`, `/viewer`), `/status` |
| 8889 | MediaMTX WebRTC: `…/camN/whep` (watch), `…/camN/whip` (publish from the mock) |
| 8189/udp | MediaMTX WebRTC media |
| 8554 | MediaMTX RTSP: Unity's FFmpegStreamer publishes here |

Health check: <http://localhost:8080/status> shows what's connected, the current game and the last telemetry message.
Watch one camera directly: <http://localhost:8889/cam1>.

## 7. Troubleshooting

| Problem | Fix |
|---|---|
| `'node' is not recognized` | Install Node.js, then close and reopen the window. |
| **Bridge** dot grey on the dashboard | The bridge window was closed or crashed. Run `start-bridge` again. |
| **Game** dot grey | Mock page not open, Unity not playing, or wrong `TelemetryBridge > Url`. |
| Dashboard shows the wrong game | Check `TelemetryBridge > Game Id`, or pick the game in the Session panel's menu. |
| Tiles say **NO VIDEO** | MediaMTX not running, or nothing publishes cam1–cam4. The mock page should say PUBLISHING. |
| Video only works on the game PC | Set `webrtcAdditionalHosts` (section 4) and allow 8189/UDP. |
| Subtitles stay in English | That language (or NPC dialogue) needs the API key (section 5). |
| The AI director never cuts on an event | The event name must match `games/profiles.js` exactly (e.g. `rope_tied`, not `RopeTied`). |
| Microphone does nothing | Use Chrome/Edge on `http://localhost:8080` and allow the microphone. |
| Unity: `could not start ffmpeg` | Install FFmpeg or set `FFmpegStreamer > Ffmpeg Path` to ffmpeg.exe. |
| Unity video upside down | Toggle `FFmpegStreamer > Flip Vertically`. |
| Headset stutters while streaming | Tick `Use Nvenc`, lower the rig resolution/fps, or turn off `Prioritise Active Camera`. |
