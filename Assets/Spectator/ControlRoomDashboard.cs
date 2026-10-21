using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthStar
{
    /// <summary>
    /// Task 2: the director's control room, drawn with IMGUI so it needs no prefab or canvas setup.
    ///  - Live telemetry panel (from FullTelemetryLogger), player, scene, status
    ///  - Multiview: every spectator camera renders to a RenderTexture and is shown in a grid
    ///  - Manual override: click a view (or press 1-9) to send it to the PROGRAM output; F1 = fullscreen output for OBS
    ///  - Export: CSV copy, PNG frames (selected or all views), image-sequence clips
    /// AUTO mode follows 'suggestedCamera', which the future AI director writes to.
    /// </summary>
    public class ControlRoomDashboard : MonoBehaviour
    {
        [Serializable]
        public class View
        {
            public string name;
            public Camera camera;
            [NonSerialized] public RenderTexture rt;
        }

        public FullTelemetryLogger logger;
        public List<View> views = new List<View>();
        public int rtWidth = 960, rtHeight = 540;
        public float clipFps = 10f;
        public KeyCode fullscreenKey = KeyCode.F1;
        [Tooltip("The director writes a camera name here; used in AUTO mode.")]
        public string suggestedCamera = "";
        [Tooltip("Optional: drag the 'SpectatorCameras' object here to add every camera under it as a view.")]
        public Transform autoCollectFrom;
        [Tooltip("Frame rate of the small thumbnails. The program output renders every frame. Lower = faster game.")]
        public float thumbnailFps = 5f;
        /// <summary>Views being streamed to a web viewer right now; filled by StreamServer, rendered at full rate.</summary>
        public readonly List<string> fullRateNames = new List<string>();
        float[] nextRender;

        int selected;
        bool manual = true, fullscreen, recording;
        string status = "Ready";
        Vector2 scroll;

        string ExportDir { get { return Path.Combine(Application.persistentDataPath, "Exports"); } }
        View Current { get { return views.Count > 0 ? views[Mathf.Clamp(selected, 0, views.Count - 1)] : null; } }
        static string Stamp() { return DateTime.Now.ToString("yyyyMMdd_HHmmss"); }

        void Awake()
        {
            if (autoCollectFrom != null && views.Count == 0)
                foreach (Camera c in autoCollectFrom.GetComponentsInChildren<Camera>(true))
                    views.Add(new View { name = c.name, camera = c });

            foreach (View v in views)
            {
                if (v.camera == null) continue;
                v.rt = new RenderTexture(rtWidth, rtHeight, 24);
                v.camera.targetTexture = v.rt;                      // render to texture, not to the headset
                v.camera.stereoTargetEye = StereoTargetEyeMask.None;
                v.camera.enabled = false;                           // rendered by hand in LateUpdate (saves performance)
                AudioListener al = v.camera.GetComponent<AudioListener>();
                if (al != null) al.enabled = false;
            }
        }

        void Update()
        {
            if (!manual && !string.IsNullOrEmpty(suggestedCamera))
            {
                int i = views.FindIndex(v => v.name == suggestedCamera);
                if (i >= 0) selected = i;
            }
            if (logger != null && Current != null)
            {
                logger.activeCamera = Current.name;
                logger.mode = manual ? "MANUAL" : "AUTO";
            }
        }

        void LateUpdate()
        {
            if (views.Count == 0) return;
            if (nextRender == null || nextRender.Length != views.Count) nextRender = new float[views.Count];
            float now = Time.unscaledTime;
            for (int i = 0; i < views.Count; i++)
            {
                View v = views[i];
                if (v.camera == null || v.rt == null) continue;
                bool fullRate = i == selected || fullRateNames.Contains(v.name);
                if (!fullRate && now < nextRender[i]) continue;
                nextRender[i] = now + 1f / Mathf.Max(1f, thumbnailFps);
                v.camera.Render();
            }
        }

        void Pick(int i) { selected = i; manual = true; }   // clicking always overrides AUTO

        void OnGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == fullscreenKey) { fullscreen = !fullscreen; e.Use(); }
                int n = e.keyCode - KeyCode.Alpha1;
                if (n >= 0 && n < 9 && n < views.Count) { Pick(n); e.Use(); }
            }
            if (Current == null || Current.rt == null) return;

            float W = Screen.width, H = Screen.height;
            if (fullscreen)
            {
                GUI.DrawTexture(new Rect(0, 0, W, H), Current.rt, ScaleMode.ScaleToFit, false);
                return;
            }

            // Top bar
            string player = logger != null ? logger.playerName : "-";
            string fps = logger != null ? logger.Fps.ToString("F0") : "-";
            string rows = logger != null ? logger.RowCount.ToString() : "-";
            string log = logger != null && logger.logging ? "ON" : "OFF";
            GUILayout.BeginArea(new Rect(10, 8, W - 20, 36));
            GUILayout.BeginHorizontal();
            GUILayout.Label("CONTROL ROOM   Player: " + player + "   Scene: " + SceneManager.GetActiveScene().name +
                            "   FPS: " + fps + "   CSV logging: " + log + " (" + rows + " rows)   Output: " + Current.name);
            if (GUILayout.Button(manual ? "MODE: MANUAL" : "MODE: AUTO", GUILayout.Width(130))) manual = !manual;
            if (GUILayout.Button("Fullscreen output (F1)", GUILayout.Width(170))) fullscreen = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // Left: live telemetry
            GUILayout.BeginArea(new Rect(10, 50, 300, H - 60), GUI.skin.box);
            GUILayout.Label("LIVE TELEMETRY");
            scroll = GUILayout.BeginScrollView(scroll);
            if (logger != null)
                for (int i = 0; i < logger.Names.Length; i++)
                    GUILayout.Label(logger.Names[i] + ": " + logger.Values[i]);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Center: program output + multiview grid
            float pw = Mathf.Max(320f, W - 600f);
            float ph = Mathf.Min(pw * 9f / 16f, H * 0.55f);
            pw = ph * 16f / 9f;
            GUI.DrawTexture(new Rect(320, 50, pw, ph), Current.rt, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(326, 52, 400, 22), "PROGRAM OUTPUT: " + Current.name);

            float tw = 200f, th = 112f, y0 = 50f + ph + 10f;
            int cols = Mathf.Max(1, (int)((W - 600f) / (tw + 10f)));
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].rt == null) continue;
                Rect r = new Rect(320 + (i % cols) * (tw + 10f), y0 + (i / cols) * (th + 10f), tw, th);
                GUI.DrawTexture(r, views[i].rt, ScaleMode.ScaleToFit, false);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Pick(i);
                GUI.Label(new Rect(r.x + 4, r.y + 2, tw, 20), (i == selected ? "> " : "") + (i + 1) + ". " + views[i].name);
            }

            // Right: export
            GUILayout.BeginArea(new Rect(W - 270, 50, 260, H - 60), GUI.skin.box);
            GUILayout.Label("DATA EXPORT");
            if (logger != null)
            {
                if (GUILayout.Button(logger.logging ? "Pause CSV logging" : "Resume CSV logging")) logger.logging = !logger.logging;
                if (GUILayout.Button("Start new session file")) { logger.StartNewSession(); status = "New session file started"; }
                if (GUILayout.Button("Save CSV copy now")) SaveCsv();
            }
            if (GUILayout.Button("Save frame: " + Current.name)) SaveFrames(false);
            if (GUILayout.Button("Save frames: ALL views")) SaveFrames(true);
            if (GUILayout.Button(recording ? "STOP clip" : "Record clip: " + Current.name))
            {
                recording = !recording;
                if (recording) StartCoroutine(RecordClip(Current));
            }
            GUILayout.Space(8);
            GUILayout.Label("Status: " + status);
            GUILayout.Label("Exports: " + ExportDir);
            if (logger != null) GUILayout.Label("Session file: " + logger.SessionPath);
            GUILayout.EndArea();
        }

        void SaveCsv()
        {
            try
            {
                logger.Flush();
                Directory.CreateDirectory(ExportDir);
                string p = Path.Combine(ExportDir, "telemetry_" + Stamp() + ".csv");
                File.Copy(logger.SessionPath, p, true);
                status = "Saved " + p;
            }
            catch (Exception ex) { status = "CSV save failed: " + ex.Message; }
        }

        void SaveFrames(bool all)
        {
            try
            {
                Directory.CreateDirectory(ExportDir);
                string stamp = Stamp();
                int count = 0;
                foreach (View v in views)
                {
                    if (v.rt == null || (!all && v != Current)) continue;
                    WritePng(v.rt, Path.Combine(ExportDir, v.name + "_" + stamp + ".png"));
                    count++;
                }
                status = "Saved " + count + " frame(s) to Exports";
            }
            catch (Exception ex) { status = "Frame save failed: " + ex.Message; }
        }

        IEnumerator RecordClip(View v)
        {
            string dir = Path.Combine(ExportDir, "clip_" + v.name + "_" + Stamp());
            Directory.CreateDirectory(dir);
            int n = 0;
            while (recording)
            {
                WritePng(v.rt, Path.Combine(dir, "frame_" + n.ToString("D5") + ".png"));
                n++;
                yield return new WaitForSeconds(1f / clipFps);
            }
            status = "Clip saved: " + n + " frames in " + dir;
        }

        static void WritePng(RenderTexture rt, string path)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        void OnDestroy()
        {
            foreach (View v in views) if (v.rt != null) v.rt.Release();
        }
    }
}
