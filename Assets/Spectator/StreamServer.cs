#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NorthStar
{
    /// <summary>
    /// Serves the dashboard's camera views to a web page, so viewers can pick which camera to watch.
    ///   /                 the viewer page (Assets/StreamingAssets/viewer.html)
    ///   /cameras          JSON list of camera names
    ///   /telemetry        JSON with scene, player, active (director) camera and live values
    ///   /snapshot/NAME    one JPEG
    ///   /stream/NAME      live MJPEG stream (works in a normal <img> tag)
    /// Cameras are only encoded while somebody is watching them. Editor/desktop only (not for the Quest build).
    /// </summary>
    public class StreamServer : MonoBehaviour
    {
        public ControlRoomDashboard dashboard;
        public FullTelemetryLogger logger;
        public int port = 8080;
        [Tooltip("OFF = only this computer. ON = phones and other computers on the same network can watch.")]
        public bool allowLan = false;
        public float streamFps = 12f;
        [Range(10, 95)] public int jpegQuality = 70;

        [Header("Replay (clips of what was on air)")]
        public bool recordReplay = true;
        [Tooltip("Length of each clip in seconds")] public int replaySeconds = 12;
        [Tooltip("Keep this many clips (each is about 5 MB)")] public int maxClips = 15;
        [Tooltip("Save a clip by itself when an event happens")] public bool autoReplay = true;
        public float autoDelay = 4f;       // seconds after the event starts
        public float autoCooldown = 15f;   // minimum seconds between automatic clips

        [Header("Personal fly cameras")]
        public FlyCameraPool flyPool;   // drag the FlyPool object here

        [Header("Viewer chat (live, watched by moderators and an automatic helper)")]
        [Tooltip("Secret for the moderator page, at least 6 characters. Empty = moderator page is switched off")]
        public string modKey = "";
        [Tooltip("ON: messages reach the player at once and moderators remove them afterwards. OFF: moderators must approve each message first")]
        public bool liveChat = true;
        [Tooltip("Automatic helper that checks every message with fixed rules (not a language model)")]
        public bool autoMod = true;
        [Tooltip("Seconds a viewer must wait between messages")] public float chatCooldown = 5f;
        public int maxChatLength = 120;
        public int maxPending = 60;
        [Tooltip("Messages that contain one of these words are dropped and count as a strike")] public string[] bannedWords = new string[0];
        public bool chatPaused;

        [Header("Automatic helper rules")]
        public bool blockLinks = true;
        public bool blockContactInfo = true;      // phone numbers and e-mail addresses
        public bool holdShouting = true;          // ALL CAPS messages wait for a moderator
        public int strikesForTimeout = 3;
        public int strikesForBan = 8;
        public float autoTimeoutMinutes = 2f;     // grows with every extra strike
        [Tooltip("The board keeps at most this many messages waiting")] public int boardQueueMax = 5;

        class ChatMsg { public int id, seq, count = 1; public string viewer, name, text, norm, flag = ""; public double at; public bool mod, removed, held; }
        enum Verdict { Allow, Hold, Drop }
        readonly List<ChatMsg> pending = new List<ChatMsg>();     // held for a moderator
        readonly List<ChatMsg> history = new List<ChatMsg>();     // everything that went to the player
        readonly Queue<ChatMsg> toPlayer = new Queue<ChatMsg>();
        readonly HashSet<string> banned = new HashSet<string>();
        readonly Dictionary<string, double> timeoutUntil = new Dictionary<string, double>();
        readonly Dictionary<string, int> strikes = new Dictionary<string, int>();
        readonly Dictionary<string, string> viewerName = new Dictionary<string, string>();
        readonly Dictionary<string, double> lastChatAt = new Dictionary<string, double>();
        readonly List<string> modLog = new List<string>();
        static readonly Regex LinkRx = new Regex(@"(https?://|www\.)|\b[a-z0-9\-]+\.(com|net|org|io|gg|tv|ly|me|co|xyz)\b", RegexOptions.IgnoreCase);
        int nextChatId = 1, sentSeq, boardClearToken;
        string moderatorPath;

        HttpListener listener;
        Thread thread;
        volatile bool running;
        readonly object gate = new object();
        readonly Dictionary<string, byte[]> frames = new Dictionary<string, byte[]>();
        readonly Dictionary<string, double> wantedAt = new Dictionary<string, double>();
        readonly Dictionary<string, double> streamingAt = new Dictionary<string, double>();
        readonly Stopwatch clock = new Stopwatch();
        string camerasJson = "[]";
        string telemetryJson = "{}";
        string viewerPath;
        float nextCapture;
        double lastTelemetry;
        Texture2D scratch;

        class Clip { public int id; public string label, scene, camera; public double savedAt; public float fps; public List<byte[]> frames; }
        readonly List<byte[]> ring = new List<byte[]>();
        readonly List<Clip> clips = new List<Clip>();
        int nextClipId = 1, eventIdx = -1;
        volatile string pendingSaveLabel;
        string lastEvent = "", autoLabel = "";
        double autoSaveAt, lastAutoSave = -999;

        double Now { get { return clock.Elapsed.TotalSeconds; } }

        void Start()
        {
            clock.Start();
            viewerPath = Path.Combine(Application.streamingAssetsPath, "viewer.html");
            moderatorPath = Path.Combine(Application.streamingAssetsPath, "moderator.html");
            if (string.IsNullOrEmpty(modKey) || modKey.Length < 6) Debug.LogWarning("StreamServer: set Mod Key (6 or more characters) to switch on the moderator page.");
            if (dashboard == null) { Debug.LogWarning("StreamServer: assign the dashboard"); return; }

            var sb = new StringBuilder("[");
            for (int i = 0; i < dashboard.views.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Q(dashboard.views[i].name)).Append('}');
            }
            camerasJson = sb.Append(']').ToString();

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://" + (allowLan ? "+" : "localhost") + ":" + port + "/");
                listener.Start();
                running = true;
                thread = new Thread(AcceptLoop) { IsBackground = true };
                thread.Start();
                Debug.Log("Viewer page: http://localhost:" + port + "/" + (allowLan ? "   (LAN: http://" + LanIp() + ":" + port + "/)" : ""));
            }
            catch (Exception e)
            {
                Debug.LogWarning("StreamServer could not start (try allowLan = off, or another port): " + e.Message);
            }
        }

        void Update()
        {
            if (!running || dashboard == null) return;
            double now = Now;

            // cameras with a live viewer stream render at full rate; the rest stay at thumbnail rate
            dashboard.fullRateNames.Clear();
            lock (gate) foreach (var kv in streamingAt) if (now - kv.Value < 1.0) dashboard.fullRateNames.Add(kv.Key);
            string air = logger != null ? logger.activeCamera : null;
            if (recordReplay && !string.IsNullOrEmpty(air)) dashboard.fullRateNames.Add(air);   // the on-air camera renders at full rate

            if (now - lastTelemetry > 0.2)
            {
                lastTelemetry = now;
                string t = BuildTelemetry();
                lock (gate) telemetryJson = t;
            }

            // ---- replay: save clips on request or when an event starts ----
            string req = pendingSaveLabel;
            if (req != null) { pendingSaveLabel = null; SaveClip(req); }
            if (recordReplay && autoReplay && logger != null)
            {
                string ev = CurrentEvent();
                if (ev != lastEvent)
                {
                    lastEvent = ev;
                    if (!string.IsNullOrEmpty(ev) && ev != "IDLE" && ev != "None" && now - lastAutoSave > autoCooldown)
                    { autoSaveAt = now + autoDelay; autoLabel = ev; lastAutoSave = now; }
                }
                if (autoSaveAt > 0 && now >= autoSaveAt) { autoSaveAt = 0; SaveClip(autoLabel); }
            }

            if (Time.unscaledTime < nextCapture) return;
            nextCapture = Time.unscaledTime + 1f / Mathf.Max(1f, streamFps);

            foreach (var v in dashboard.views)
            {
                if (v.rt == null) continue;
                bool wanted;
                lock (gate) { double w; wanted = wantedAt.TryGetValue(v.name, out w) && now - w < 4.0; }
                bool isAir = recordReplay && !string.IsNullOrEmpty(air) && v.name == air;
                if (!wanted && !isAir) continue;
                byte[] jpg = Encode(v.rt);
                lock (gate) frames[v.name] = jpg;
                if (isAir) PushReplay(jpg);
            }

            if (flyPool != null)
                foreach (var fs in flyPool.Slots)
                {
                    if (fs.owner == null || fs.rt == null) continue;
                    bool flyWanted;
                    lock (gate) { double fw; flyWanted = wantedAt.TryGetValue(fs.name, out fw) && now - fw < 4.0; }
                    if (!flyWanted) continue;
                    byte[] fjpg = Encode(fs.rt);
                    lock (gate) frames[fs.name] = fjpg;
                }
        }

        // ---------- viewer chat: live to the player, watched by moderators and an automatic helper ----------
        static string CleanText(string s, int max)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c0 in s)
            {
                char c = c0;
                if (c == '<' || c == '>') continue;                     // rich text tags are never allowed
                if (char.IsControl(c) || c == '\u200b') c = ' ';
                if (c == ' ') { if (space) continue; space = true; } else space = false;
                sb.Append(c);
                if (sb.Length >= max) break;
            }
            return sb.ToString().Trim();
        }

        static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        string DisplayName(string viewer)   // inside lock(gate)
        {
            string n;
            if (viewerName.TryGetValue(viewer, out n)) return n;
            return viewer.Length > 6 ? viewer.Substring(0, 6) : viewer;
        }

        void Log(string text) { modLog.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text); while (modLog.Count > 60) modLog.RemoveAt(0); }   // inside lock(gate)

        /// <summary>The automatic helper: fixed rules, no language model. Inside lock(gate).</summary>
        Verdict AutoCheck(string viewer, string text, out string flag)
        {
            flag = "";
            if (!autoMod) return Verdict.Allow;
            if (blockLinks && LinkRx.IsMatch(text)) { flag = "link"; return Verdict.Drop; }
            if (blockContactInfo)
            {
                int digits = 0;
                foreach (char c in text) if (char.IsDigit(c)) digits++;
                if (digits >= 9 || text.Contains("@")) { flag = "contact info"; return Verdict.Drop; }
            }
            int run = 1;
            for (int i = 1; i < text.Length; i++)
            {
                run = text[i] == text[i - 1] ? run + 1 : 1;
                if (run >= 8) { flag = "spam (repeated characters)"; return Verdict.Drop; }
            }
            string norm = Norm(text);
            int same = 0;
            for (int i = history.Count - 1; i >= 0 && same < 2; i--)
            {
                var h = history[i];
                if (h.viewer == viewer && h.norm == norm && Now - h.at < 60.0) same++;
            }
            if (same >= 2) { flag = "spam (same message again)"; return Verdict.Drop; }
            if (holdShouting)
            {
                int letters = 0, upper = 0;
                foreach (char c in text) if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
                if (letters >= 10 && upper > letters * 0.7f) { flag = "shouting"; return Verdict.Hold; }
            }
            return Verdict.Allow;
        }

        void AddStrike(string viewer, string why)   // inside lock(gate)
        {
            if (!autoMod) return;
            int n; strikes.TryGetValue(viewer, out n); n++; strikes[viewer] = n;
            string who = DisplayName(viewer);
            if (n >= strikesForBan)
            {
                banned.Add(viewer); RemoveMessagesOf(viewer);
                Log("AUTO-BAN " + who + " (" + n + " strikes, last: " + why + ")");
            }
            else if (n >= strikesForTimeout)
            {
                float mins = autoTimeoutMinutes * (n - strikesForTimeout + 1);
                timeoutUntil[viewer] = Now + mins * 60.0;
                Log("AUTO-TIMEOUT " + who + " for " + mins.ToString("0.#", CultureInfo.InvariantCulture) + " min (" + n + " strikes, last: " + why + ")");
            }
            else Log("Blocked a message from " + who + ": " + why + " (strike " + n + ")");
        }

        void GoLive(ChatMsg m)   // inside lock(gate)
        {
            m.held = false; m.seq = ++sentSeq; m.at = Now;
            toPlayer.Enqueue(m);
            while (toPlayer.Count > Mathf.Max(1, boardQueueMax)) toPlayer.Dequeue();
            history.Add(m);
            while (history.Count > 80) history.RemoveAt(0);
        }

        void RebuildQueue()   // inside lock(gate)
        {
            var keep = new List<ChatMsg>();
            foreach (var m in toPlayer) if (!m.removed) keep.Add(m);
            toPlayer.Clear();
            foreach (var m in keep) toPlayer.Enqueue(m);
        }

        void RemoveMessagesOf(string viewer)   // inside lock(gate)
        {
            foreach (var m in history) if (m.viewer == viewer) m.removed = true;
            pending.RemoveAll(p => p.viewer == viewer);
            RebuildQueue();
        }

        string ChatSend(string viewer, string name, string text, out int seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(viewer) || viewer.Length > 64) return "bad";
            text = CleanText(text, maxChatLength);
            name = CleanText(name, 20);
            if (text.Length == 0) return "empty";
            if (name.Length == 0) name = "Guest";
            if (chatPaused) return "paused";
            string low = text.ToLowerInvariant();
            lock (gate)
            {
                viewerName[viewer] = name;
                if (banned.Contains(viewer)) return "banned";
                double until;
                if (timeoutUntil.TryGetValue(viewer, out until))
                {
                    if (until > Now) { seconds = (int)(until - Now) + 1; return "timeout"; }
                    timeoutUntil.Remove(viewer);
                }
                double last;
                if (lastChatAt.TryGetValue(viewer, out last) && Now - last < chatCooldown) return "wait";
                if (lastChatAt.Count > 5000) lastChatAt.Clear();
                lastChatAt[viewer] = Now;

                foreach (var w in bannedWords)
                    if (!string.IsNullOrEmpty(w) && low.Contains(w.ToLowerInvariant())) { AddStrike(viewer, "banned word"); return "filtered"; }
                string flag;
                Verdict v = AutoCheck(viewer, text, out flag);
                if (v == Verdict.Drop) { AddStrike(viewer, flag); return "filtered"; }

                var m = new ChatMsg { id = nextChatId++, viewer = viewer, name = name, text = text, norm = Norm(text), at = Now, flag = flag };
                if (v == Verdict.Hold || !liveChat)
                {
                    if (m.norm.Length > 0)
                        foreach (var p in pending) if (p.norm == m.norm) { p.count++; return "ok"; }   // same words again: count them
                    if (pending.Count >= maxPending) return "full";
                    m.held = true; pending.Add(m);
                    if (v == Verdict.Hold) Log("Held for you: " + name + " (" + flag + ")");
                    return "ok";
                }
                // live: never show the same words twice within 30 seconds, count them instead
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    var h = history[i];
                    if (Now - h.at > 30.0) break;
                    if (!h.removed && !h.mod && m.norm.Length > 0 && h.norm == m.norm) { h.count++; return "ok"; }
                }
                GoLive(m);
            }
            return "ok";
        }

        // ---- moderator actions (each one takes the lock itself) ----
        void ChatRelease(int id, string edited)   // held message -> player
        {
            lock (gate)
                for (int i = 0; i < pending.Count; i++)
                    if (pending[i].id == id)
                    {
                        var m = pending[i]; pending.RemoveAt(i);
                        string t = CleanText(edited, maxChatLength);
                        if (t.Length > 0) { m.text = t; m.norm = Norm(t); }
                        GoLive(m);
                        return;
                    }
        }

        void ChatReject(int id) { lock (gate) pending.RemoveAll(m => m.id == id); }

        void ChatRemove(int id)   // take a message off the board and the viewers' list
        {
            lock (gate)
            {
                foreach (var m in history) if (m.id == id) m.removed = true;
                RebuildQueue();
            }
        }

        void ChatTimeout(string viewer, float minutes)
        {
            if (string.IsNullOrEmpty(viewer)) return;
            minutes = Mathf.Clamp(minutes, 0.5f, 240f);
            lock (gate)
            {
                timeoutUntil[viewer] = Now + minutes * 60.0;
                pending.RemoveAll(p => p.viewer == viewer);
                Log("Moderator: timeout " + DisplayName(viewer) + " for " + minutes.ToString("0.#", CultureInfo.InvariantCulture) + " min");
            }
        }

        void ChatBan(string viewer)
        {
            if (string.IsNullOrEmpty(viewer)) return;
            lock (gate) { banned.Add(viewer); RemoveMessagesOf(viewer); Log("Moderator: banned " + DisplayName(viewer)); }
        }

        void ChatUnban(string viewer)
        {
            if (string.IsNullOrEmpty(viewer)) return;
            lock (gate) { banned.Remove(viewer); timeoutUntil.Remove(viewer); strikes.Remove(viewer); Log("Moderator: cleared the penalties of " + DisplayName(viewer)); }
        }

        void ChatClear() { lock (gate) { toPlayer.Clear(); boardClearToken++; Log("Moderator: cleared the board"); } }

        void ChatModSend(string text)
        {
            string t = CleanText(text, maxChatLength);
            if (t.Length == 0) return;
            lock (gate) GoLive(new ChatMsg { id = nextChatId++, name = "Mod", text = t, norm = "", mod = true, at = Now });
        }

        // ---- for the headset board (main thread) ----
        public bool TryTakeForPlayer(out int id, out string name, out string text, out bool mod, out int count)
        {
            lock (gate)
            {
                while (toPlayer.Count > 0 && toPlayer.Peek().removed) toPlayer.Dequeue();
                if (toPlayer.Count == 0) { id = 0; name = text = ""; mod = false; count = 1; return false; }
                var m = toPlayer.Dequeue();
                id = m.id; name = m.name; text = m.text; mod = m.mod; count = m.count;
                return true;
            }
        }

        public bool IsRemoved(int id)
        {
            lock (gate)
            {
                for (int i = history.Count - 1; i >= 0; i--) if (history[i].id == id) return history[i].removed;
                return true;   // no longer in the list
            }
        }

        public int BoardClearToken { get { lock (gate) return boardClearToken; } }

        string StateJson()
        {
            var sb = new StringBuilder();
            lock (gate)
            {
                sb.Append("{\"paused\":").Append(chatPaused ? "true" : "false")
                  .Append(",\"live\":").Append(liveChat ? "true" : "false")
                  .Append(",\"autoMod\":").Append(autoMod ? "true" : "false")
                  .Append(",\"held\":[");
                for (int i = 0; i < pending.Count; i++)
                {
                    var m = pending[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"id\":").Append(m.id).Append(",\"viewer\":").Append(Q(m.viewer)).Append(",\"name\":").Append(Q(m.name))
                      .Append(",\"text\":").Append(Q(m.text)).Append(",\"count\":").Append(m.count).Append(",\"flag\":").Append(Q(m.flag))
                      .Append(",\"age\":").Append((int)(Now - m.at)).Append('}');
                }
                sb.Append("],\"history\":[");
                int n = 0;
                for (int i = history.Count - 1; i >= 0 && n < 40; i--, n++)
                {
                    var m = history[i];
                    if (n > 0) sb.Append(',');
                    sb.Append("{\"id\":").Append(m.id).Append(",\"viewer\":").Append(Q(m.viewer ?? "")).Append(",\"name\":").Append(Q(m.name))
                      .Append(",\"text\":").Append(Q(m.text)).Append(",\"count\":").Append(m.count).Append(",\"mod\":").Append(m.mod ? "true" : "false")
                      .Append(",\"removed\":").Append(m.removed ? "true" : "false").Append(",\"age\":").Append((int)(Now - m.at)).Append('}');
                }
                sb.Append("],\"penalties\":[");
                bool first = true;
                foreach (var v in banned)
                {
                    if (!first) sb.Append(','); first = false;
                    sb.Append("{\"type\":\"ban\",\"viewer\":").Append(Q(v)).Append(",\"name\":").Append(Q(DisplayName(v))).Append(",\"seconds\":0}");
                }
                foreach (var kv in timeoutUntil)
                {
                    if (kv.Value <= Now) continue;
                    if (!first) sb.Append(','); first = false;
                    sb.Append("{\"type\":\"timeout\",\"viewer\":").Append(Q(kv.Key)).Append(",\"name\":").Append(Q(DisplayName(kv.Key)))
                      .Append(",\"seconds\":").Append((int)(kv.Value - Now)).Append('}');
                }
                sb.Append("],\"log\":[");
                for (int i = modLog.Count - 1, c = 0; i >= 0 && c < 30; i--, c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append(Q(modLog[i]));
                }
                sb.Append("]}");
            }
            return sb.ToString();
        }

        string FeedJson(int since)
        {
            var sb = new StringBuilder("{\"paused\":" + (chatPaused ? "true" : "false") + ",\"cooldown\":" + chatCooldown.ToString(CultureInfo.InvariantCulture) + ",\"messages\":[");
            var gone = new StringBuilder();
            bool first = true, firstGone = true;
            lock (gate)
                foreach (var m in history)
                {
                    if (m.removed) { if (!firstGone) gone.Append(','); firstGone = false; gone.Append(m.seq); continue; }   // viewers delete these
                    if (m.seq <= since) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"seq\":").Append(m.seq).Append(",\"name\":").Append(Q(m.name)).Append(",\"text\":").Append(Q(m.text))
                      .Append(",\"mod\":").Append(m.mod ? "true" : "false").Append(",\"count\":").Append(m.count).Append('}');
                }
            return sb.Append("],\"removed\":[").Append(gone.ToString()).Append("]}").ToString();
        }

        bool ModOk(HttpListenerContext ctx)
        {
            string k = ctx.Request.QueryString["key"];
            bool ok = !string.IsNullOrEmpty(modKey) && modKey.Length >= 6 && k == modKey;
            if (!ok) Thread.Sleep(300);   // slows down guessing
            return ok;
        }

        // ---------- replay and free camera helpers (main thread unless noted) ----------
        string CurrentEvent()
        {
            if (logger == null || logger.Names == null || logger.Values == null) return "";
            if (eventIdx < 0 || eventIdx >= logger.Names.Length || logger.Names[eventIdx] != "Event")
            {
                eventIdx = -1;
                for (int i = 0; i < logger.Names.Length; i++) if (logger.Names[i] == "Event") { eventIdx = i; break; }
            }
            return (eventIdx >= 0 && eventIdx < logger.Values.Length) ? (logger.Values[eventIdx] ?? "") : "";
        }

        void PushReplay(byte[] jpg)
        {
            int cap = Mathf.Max(10, Mathf.RoundToInt(replaySeconds * Mathf.Max(1f, streamFps)));
            lock (gate) { ring.Add(jpg); while (ring.Count > cap) ring.RemoveAt(0); }
        }

        void SaveClip(string label)
        {
            List<byte[]> copy;
            lock (gate) copy = new List<byte[]>(ring);
            if (copy.Count < 10) return;
            var c = new Clip
            {
                id = nextClipId++, label = string.IsNullOrEmpty(label) ? "clip" : label,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                camera = logger != null ? logger.activeCamera : "", savedAt = Now, fps = streamFps, frames = copy
            };
            lock (gate) { clips.Add(c); while (clips.Count > Mathf.Max(1, maxClips)) clips.RemoveAt(0); }
        }

        string ReplayListJson()   // network thread: reads only stored fields
        {
            var sb = new StringBuilder("[");
            lock (gate)
                for (int i = clips.Count - 1; i >= 0; i--)
                {
                    var c = clips[i];
                    if (i != clips.Count - 1) sb.Append(',');
                    sb.Append("{\"id\":").Append(c.id).Append(",\"label\":").Append(Q(c.label)).Append(",\"scene\":").Append(Q(c.scene))
                      .Append(",\"camera\":").Append(Q(c.camera)).Append(",\"frames\":").Append(c.frames.Count)
                      .Append(",\"fps\":").Append(c.fps.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append(",\"age\":").Append(((int)(Now - c.savedAt)).ToString(CultureInfo.InvariantCulture)).Append('}');
                }
            return sb.Append(']').ToString();
        }

        byte[] ReplayFrame(int id, int n)
        {
            lock (gate)
                foreach (var c in clips)
                    if (c.id == id) return (n >= 0 && n < c.frames.Count) ? c.frames[n] : null;
            return null;
        }

        static float Num(string s, float fallback)
        {
            float f;
            return (s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) ? f : fallback;
        }

        byte[] Encode(RenderTexture rt)
        {
            if (scratch == null || scratch.width != rt.width || scratch.height != rt.height)
                scratch = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            scratch.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = prev;
            return scratch.EncodeToJPG(jpegQuality);
        }

        string BuildTelemetry()
        {
            var sb = new StringBuilder("{");
            sb.Append("\"scene\":").Append(Q(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));
            sb.Append(",\"player\":").Append(Q(logger != null ? logger.playerName : ""));
            sb.Append(",\"activeCamera\":").Append(Q(logger != null ? logger.activeCamera : ""));
            sb.Append(",\"mode\":").Append(Q(logger != null ? logger.mode : ""));
            sb.Append(",\"values\":{");
            if (logger != null)
                for (int i = 0; i < logger.Names.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Q(logger.Names[i])).Append(':').Append(Q(logger.Values[i] ?? ""));
                }
            return sb.Append("}}").ToString();
        }

        // ---------- network thread ----------
        void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
                }
                catch { if (!running) break; }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            HttpListenerResponse res = ctx.Response;
            try
            {
                res.AddHeader("Access-Control-Allow-Origin", "*");
                res.AddHeader("Cache-Control", "no-store");
                string path = ctx.Request.Url.AbsolutePath;

                if (path == "/" || path == "/index.html" || path == "/viewer.html")
                {
                    if (File.Exists(viewerPath)) Send(res, "text/html; charset=utf-8", File.ReadAllBytes(viewerPath));
                    else Send(res, "text/plain", Encoding.UTF8.GetBytes("Stream server is running, but Assets/StreamingAssets/viewer.html was not found."));
                }
                else if (path == "/cameras") Send(res, "application/json", Encoding.UTF8.GetBytes(camerasJson));
                else if (path == "/telemetry")
                {
                    string t; lock (gate) t = telemetryJson;
                    Send(res, "application/json", Encoding.UTF8.GetBytes(t));
                }
                else if (path.StartsWith("/snapshot/"))
                {
                    string name = Uri.UnescapeDataString(path.Substring("/snapshot/".Length));
                    MarkWanted(name);
                    byte[] f = GetFrame(name);
                    if (f == null) { res.StatusCode = 503; res.Close(); return; }
                    Send(res, "image/jpeg", f);
                }
                else if (path.StartsWith("/stream/")) Stream(ctx, Uri.UnescapeDataString(path.Substring("/stream/".Length)));
                else if (path == "/free")   // viewers' orbit camera: /free?yaw=30&pitch=20&dist=3
                {
                    var q = ctx.Request.QueryString;
                    FreeCamControl.yaw = Num(q["yaw"], FreeCamControl.yaw);
                    FreeCamControl.pitch = Mathf.Clamp(Num(q["pitch"], FreeCamControl.pitch), -10f, 80f);
                    FreeCamControl.dist = Mathf.Clamp(Num(q["dist"], FreeCamControl.dist), 1f, 8f);
                    Send(res, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                }
                else if (path == "/fly/join")   // a viewer asks for a personal fly camera: /fly/join?id=VIEWERID
                {
                    string id = ctx.Request.QueryString["id"];
                    if (id != null && id.Length > 64) id = null;
                    FlySlot s = flyPool != null ? flyPool.Join(id) : null;
                    if (s != null) lock (gate) frames.Remove(s.name);   // do not show the previous viewer's last picture
                    string json = flyPool == null ? "{\"error\":\"no pool\"}"
                                : s != null ? "{\"slot\":" + Q(s.name) + "}"
                                : "{\"slot\":null,\"busy\":true,\"max\":" + flyPool.Slots.Count + "}";
                    Send(res, "application/json", Encoding.UTF8.GetBytes(json));
                }
                else if (path == "/fly/leave")
                {
                    if (flyPool != null) flyPool.Leave(ctx.Request.QueryString["id"]);
                    Send(res, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                }
                else if (path == "/fly")   // /fly?id=VIEWERID&x=0&y=0&z=1&yaw=30&pitch=10&fast=0  (reset=1 returns the camera to the player)
                {
                    var q = ctx.Request.QueryString;
                    FlySlot s = flyPool != null ? flyPool.Find(q["id"]) : null;
                    if (s != null)
                    {
                        s.moveX = Mathf.Clamp(Num(q["x"], 0f), -1f, 1f);
                        s.moveY = Mathf.Clamp(Num(q["y"], 0f), -1f, 1f);
                        s.moveZ = Mathf.Clamp(Num(q["z"], 0f), -1f, 1f);
                        s.yaw = Num(q["yaw"], s.yaw);
                        s.pitch = Mathf.Clamp(Num(q["pitch"], s.pitch), -85f, 85f);
                        s.fast = q["fast"] == "1";
                        if (q["reset"] == "1") s.resetRequested = true;
                        s.Touch();
                    }
                    Send(res, "application/json", Encoding.UTF8.GetBytes(s != null ? "{\"ok\":true}" : "{\"ok\":false}"));
                }
                else if (path == "/chat/send")   // /chat/send?id=VIEWERID&name=NAME&text=MESSAGE
                {
                    var q = ctx.Request.QueryString;
                    int secs;
                    string r = ChatSend(q["id"], q["name"], q["text"], out secs);
                    Send(res, "application/json", Encoding.UTF8.GetBytes("{\"ok\":" + (r == "ok" ? "true" : "false") + ",\"reason\":" + Q(r) +
                        ",\"seconds\":" + secs + ",\"cooldown\":" + chatCooldown.ToString(CultureInfo.InvariantCulture) + "}"));
                }
                else if (path == "/chat/feed")   // what went to the player: /chat/feed?since=SEQ
                {
                    int since; int.TryParse(ctx.Request.QueryString["since"], out since);
                    Send(res, "application/json", Encoding.UTF8.GetBytes(FeedJson(since)));
                }
                else if (path == "/mod" || path == "/moderator.html")
                {
                    if (File.Exists(moderatorPath)) Send(res, "text/html; charset=utf-8", File.ReadAllBytes(moderatorPath));
                    else Send(res, "text/plain", Encoding.UTF8.GetBytes("Assets/StreamingAssets/moderator.html was not found."));
                }
                else if (path.StartsWith("/mod/"))   // moderator actions, all need ?key=SECRET and all answer with the fresh state
                {
                    if (!ModOk(ctx)) { res.StatusCode = 403; Send(res, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"bad key\"}")); return; }
                    var q = ctx.Request.QueryString;
                    int mid; int.TryParse(q["id"], out mid);
                    float mins; float.TryParse(q["minutes"], NumberStyles.Float, CultureInfo.InvariantCulture, out mins);
                    if (path == "/mod/release" || path == "/mod/approve") ChatRelease(mid, q["text"]);
                    else if (path == "/mod/reject") ChatReject(mid);
                    else if (path == "/mod/remove") ChatRemove(mid);
                    else if (path == "/mod/timeout") ChatTimeout(q["viewer"], mins > 0f ? mins : 1f);
                    else if (path == "/mod/ban" || path == "/mod/block") ChatBan(q["viewer"]);
                    else if (path == "/mod/unban") ChatUnban(q["viewer"]);
                    else if (path == "/mod/clear") ChatClear();
                    else if (path == "/mod/send") ChatModSend(q["text"]);
                    else if (path == "/mod/pause") chatPaused = q["on"] == "1";
                    else if (path == "/mod/mode") liveChat = q["live"] == "1";
                    else if (path == "/mod/automod") autoMod = q["on"] == "1";
                    Send(res, "application/json", Encoding.UTF8.GetBytes(StateJson()));
                }
                else if (path == "/replay/list") Send(res, "application/json", Encoding.UTF8.GetBytes(ReplayListJson()));
                else if (path == "/replay/save")
                {
                    pendingSaveLabel = string.IsNullOrEmpty(ctx.Request.QueryString["label"]) ? "viewer clip" : ctx.Request.QueryString["label"];
                    Send(res, "application/json", Encoding.UTF8.GetBytes("{\"queued\":true}"));
                }
                else if (path.StartsWith("/replay/frame/"))   // /replay/frame/ID/N
                {
                    string[] p = path.Substring("/replay/frame/".Length).Split('/');
                    int id, n;
                    byte[] f = (p.Length == 2 && int.TryParse(p[0], out id) && int.TryParse(p[1], out n)) ? ReplayFrame(id, n) : null;
                    if (f == null) { res.StatusCode = 404; res.Close(); return; }
                    Send(res, "image/jpeg", f);
                }
                else { res.StatusCode = 404; res.Close(); }
            }
            catch { try { res.Abort(); } catch { } }
        }

        void Stream(HttpListenerContext ctx, string name)
        {
            HttpListenerResponse res = ctx.Response;
            res.ContentType = "multipart/x-mixed-replace; boundary=frame";
            res.SendChunked = true;
            byte[] last = null;
            try
            {
                while (running)
                {
                    MarkWanted(name);
                    lock (gate) streamingAt[name] = Now;
                    byte[] f = GetFrame(name);
                    if (f != null && !ReferenceEquals(f, last))
                    {
                        last = f;
                        byte[] head = Encoding.ASCII.GetBytes("--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + f.Length + "\r\n\r\n");
                        res.OutputStream.Write(head, 0, head.Length);
                        res.OutputStream.Write(f, 0, f.Length);
                        res.OutputStream.Write(new byte[] { 13, 10 }, 0, 2);
                        res.OutputStream.Flush();
                    }
                    Thread.Sleep(40);
                }
            }
            catch { /* viewer closed the page or switched camera */ }
            finally { try { res.Abort(); } catch { } }
        }

        void MarkWanted(string name) { lock (gate) wantedAt[name] = Now; }
        byte[] GetFrame(string name) { lock (gate) { byte[] f; return frames.TryGetValue(name, out f) ? f : null; } }

        static void Send(HttpListenerResponse res, string type, byte[] data)
        {
            res.ContentType = type;
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
            res.Close();
        }

        static string Q(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string LanIp()
        {
            try
            {
                foreach (IPAddress a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
            }
            catch { }
            return "your-ip";
        }

        void OnDestroy()
        {
            running = false;
            try { if (listener != null) listener.Close(); } catch { }
        }
    }
}
#endif
