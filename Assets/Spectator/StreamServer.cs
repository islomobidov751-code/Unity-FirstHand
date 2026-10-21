#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

        double Now { get { return clock.Elapsed.TotalSeconds; } }

        void Start()
        {
            clock.Start();
            viewerPath = Path.Combine(Application.streamingAssetsPath, "viewer.html");
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

            if (now - lastTelemetry > 0.2)
            {
                lastTelemetry = now;
                string t = BuildTelemetry();
                lock (gate) telemetryJson = t;
            }

            if (Time.unscaledTime < nextCapture) return;
            nextCapture = Time.unscaledTime + 1f / Mathf.Max(1f, streamFps);

            foreach (var v in dashboard.views)
            {
                if (v.rt == null) continue;
                bool wanted;
                lock (gate) { double w; wanted = wantedAt.TryGetValue(v.name, out w) && now - w < 4.0; }
                if (!wanted) continue;
                byte[] jpg = Encode(v.rt);
                lock (gate) frames[v.name] = jpg;
            }
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
