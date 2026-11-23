# Connecting First Hand and North Star

Goal: the real game replaces `mock/arena.html`. It sends the same telemetry and publishes the same four cameras
(`cam1`–`cam4`), so the dashboard, viewer and bridge need no changes.

Both games are Meta's open-source Unity samples:

| | First Hand | North Star |
|---|---|---|
| Repo | `github.com/oculus-samples/Unity-FirstHand` | `github.com/oculus-samples/Unity-NorthStar` |
| Unity | 2022.3.22f1+ (Unity 6 recommended) | 6000.0.59f2+ |
| Start scene | `Assets/Project/Scenes/Level/FirstLoad` | `Assets/NorthStar/Scenes/#GameplayBeats/Launch.unity` |
| Input | Hand tracking (Interaction SDK) | Hands or controllers |

**Run them on the PC**: Quest Link (or Air Link) + **Play in the Unity editor**, or a Windows build. A standalone Quest build
can't run FFmpeg or render 4 extra cameras smoothly. The scripts target Unity 2022.3+ / Unity 6, Windows.

---

## 1. Add the scripts (both games)

1. Copy everything in `unity/` into the project, e.g. `Assets/MultiView/`.
2. In the **first scene that loads** (FirstLoad / Launch), create an empty root GameObject **TelemetryBridge** and add `TelemetryBridge`:
   - **Url**: `ws://127.0.0.1:8080/game` (the bridge PC).
   - **Game Id**: `firsthand` or `northstar`.
   - **Head**: the headset camera (`OVRCameraRig/TrackingSpace/CenterEyeAnchor`); empty = Camera.main.
   - **Left Hand / Right Hand**: `LeftHandAnchor` / `RightHandAnchor` (or the hand-tracking hand roots).
   It stays alive across scene loads (DontDestroyOnLoad).
3. Add `GameEventReporter` to the same object (used for Inspector wiring, step 4).

## 2. Build the spectator rig

1. Create an empty root GameObject **SpectatorRig** (also in the first scene; drag it under TelemetryBridge so it survives scene loads)
   with 4 child cameras: `PlayerViewCam`, `SecondCam`, `ShoulderCam`, `WideCam`.
   On each: **Target Eye = None (Main Display)**, remove `AudioListener`, and don't let them render to the headset.
2. Add **MultiViewCameraRig** → Feeds: `cam1` PlayerViewCam, `cam2` SecondCam, `cam3` ShoulderCam, `cam4` WideCam.
   1280×720 at 30 fps. Use **Spectator Culling Mask** to hide things viewers shouldn't see (debug UI, the player's own body if it looks odd).
3. Add **SpectatorCameras** → drag the 4 cameras in, set **Obstacles** to walls/ship layers.
   - First Hand: **Second Mode = Hands Close Up**. Place an empty for **Wide Mount** in a corner of the clock tower room
     (or leave it empty to orbit).
   - North Star: **Second Mode = Fixed Mount** with an empty up the main mast as **Second Mount**, and an empty off the
     side of the ship as **Wide Mount**. Parent both mounts to the ship so they move with it.
4. Add **FFmpegStreamer** → Rig = SpectatorRig. Tick **Use Nvenc** on NVIDIA GPUs.
5. Back on TelemetryBridge, drag SpectatorRig into **Camera Rig**.

Press Play with MediaMTX + bridge running: the Console shows `[TelemetryBridge] connected` and 4× `[FFmpegStreamer] camN → rtsp://…`,
and the dashboard shows video. Positions and hands already work at this point; next you add the events.

## 3. Events — First Hand

First Hand uses Meta's **Interaction SDK**, which has UnityEvents you can wire without code.
Open the Clock Tower scene and find the interactables listed in the repo's README (lift, keypad, glove schematic pieces,
glove pieces, schematic UI, palm blast / shield poses):

| Moment | Where to hook it | Call |
|---|---|---|
| Glove piece picked up | the piece's grab interactable → `InteractableUnityEventWrapper` → **When Select** | `GameEventReporter.Grab("Palm plate")` |
| Released | same → **When Unselect** | `GameEventReporter.ReleaseRight` |
| Lift / keypad buttons | the poke interactable → **When Select** | `GameEventReporter.Poke("Keypad")`; lift: `Report("lift")` |
| Keypad code correct | the script that opens the door | `TelemetryBridge.Report("keypad_solved");` |
| Schematic piece placed | its snap / drop zone event | `GameEventReporter.Report("schematic_placed")` |
| Glove piece attached | the glove's snap interactor event | `GameEventReporter.GlovePieceAttached("Palm plate")` (sends `gloves_complete` after the 4th) |
| Palm blast / shield | the pose-detection "selected" event | `GameEventReporter.PalmBlast()` / `Report("shield")` |
| New area | scene load or trigger volume | `GameEventReporter.ReportChapter("Glove assembly")` |
| End | final scene / credits | `GameEventReporter.EndSession()` |

If an interactable has no `InteractableUnityEventWrapper`, add one (Add Component) and drag the interactable into its
**Interactable View** field. Use chapter names from `games/profiles.js` (`Clock Tower`, `Glove schematic`,
`Glove assembly`, `Glove powers`) so the progress list lines up, or edit the profile to match the real names.

## 4. Events — North Star

North Star's interactions are scripts (cranks, levers, rope, harpoon, spyglass), and its NPCs speak through the
**Narrative System** (see the repo's docs). Add one line where each thing happens:

```csharp
TelemetryBridge.Report("rope_tied");                      // rope wrapped around a cleat
TelemetryBridge.Report("sail_raised");                    // sail crank finished
TelemetryBridge.Report("steering");                       // player grabbed the wheel
TelemetryBridge.Report("spyglass");                       // spyglass raised to the eye
TelemetryBridge.Report("harpoon_loaded");
TelemetryBridge.Report("harpoon_fired", "hit", hit);      // bool hit
TelemetryBridge.Report("cargo_retrieved");
TelemetryBridge.Report("diving_bell");
TelemetryBridge.Report("weather", "kind", "storm");       // or "calm"
TelemetryBridge.ReportChapter("The storm");               // at each gameplay beat
TelemetryBridge.AddStat("ropes");                          // counters shown on the dashboard: ropes, harpoons, cargo, lines
TelemetryBridge.ReportDialogue(speakerName, subtitleText); // where the narrative system shows a subtitle
TelemetryBridge.ReportSessionEnd();                       // end of the experience
```

The dialogue hook is the most valuable one: every crew line becomes a subtitle for viewers in their language
(translated once by the bridge, then cached). Find the place where the narrative system displays the caption text above
the NPC and call `ReportDialogue` there with the speaker's name and the English text.

The scenes in `#GameplayBeats` are a natural place to call `ReportChapter`. Rename the profile's `chapters` in
`games/profiles.js` to the real beat names.

## 5. Test checklist

1. MediaMTX and bridge running, **mock page closed**.
2. Quest connected with Link, press Play.
3. Dashboard: Game dot green, title shows the right game, Video 4/4, the hands panel reacts when you pinch/grab.
4. Do one tracked action (grab a glove piece / tie a rope): the event log shows it, the AI cuts to the profile's camera,
   and a subtitle appears.

## Performance tips

- Keep the headset at its frame rate first. If it drops: tick Use Nvenc, set the rig to 960×540 or 24 fps.
- `MultiViewCameraRig` staggers the 4 cameras across frames. Only turn on **Prioritise Active Camera** if the on-air shot looks choppy.
- North Star is a visual showcase (ocean, ropes, lighting): its spectator cameras are expensive. Cull far detail with the
  spectator culling mask or lower the cameras' far clip plane.

## Cryptic Cabinet (later)

Mixed reality: spectator cameras only see the virtual objects, not the real room (passthrough isn't available to Unity cameras).
Plan: use a real webcam as `cam3` (OBS → WHIP to `http://<pc>:8889/cam3/whip`) and Unity cameras for the rest.
The profile is already in `games/profiles.js` with basic events (`puzzle_solved`, `chapter`, …).
