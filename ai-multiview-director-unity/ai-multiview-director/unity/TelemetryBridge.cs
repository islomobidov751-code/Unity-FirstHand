// TelemetryBridge.cs — sends the player's state + game events to the bridge, receives director commands.
// ------------------------------------------------------------------------------------------------------
// For single-player VR games (First Hand, North Star, …) running on PC: Unity editor with Quest Link,
// or a Windows build. Add it to an empty GameObject "TelemetryBridge" in the first scene; it survives scene loads.
//
// It reads the head and hands automatically (Main Camera + the transforms you assign), and sends 20 times a second:
//   {"type":"state","game":"northstar","session":{...},"player":{pos,yaw,input,hands},"stats":{...},"activeCam":"cam2"}
// Report what happens with the static helpers, from any script or from GameEventReporter in the Inspector:
//   TelemetryBridge.Report("rope_tied");
//   TelemetryBridge.Report("glove_piece", "n", 2, "total", 4);
//   TelemetryBridge.ReportChapter("Open water");
//   TelemetryBridge.ReportDialogue("Captain", "Hold fast!");
// Event names must match games/profiles.js so the dashboard knows which camera to cut to and what to say.
// Rendering never waits on the network: JSON is built on the main thread and sent by a background task.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public class TelemetryBridge : MonoBehaviour
{
    public static TelemetryBridge Instance { get; private set; }

    [Header("Connection")]
    [Tooltip("ws://<PC running the bridge>:8080/game")]
    public string url = "ws://127.0.0.1:8080/game";
    [Range(5, 60)] public float sendRateHz = 20f;

    [Header("Game")]
    [Tooltip("Profile id from games/profiles.js: firsthand, northstar or crypticcabinet")]
    public string gameId = "firsthand";
    [Tooltip("Sends session_start automatically when play begins")]
    public bool reportSessionStart = true;

    [Header("Player tracking (empty = found automatically)")]
    [Tooltip("The VR camera (CenterEyeAnchor). Empty = Camera.main")]
    public Transform head;
    [Tooltip("e.g. OVRCameraRig/TrackingSpace/LeftHandAnchor")]
    public Transform leftHand;
    [Tooltip("e.g. OVRCameraRig/TrackingSpace/RightHandAnchor")]
    public Transform rightHand;
    [Tooltip("\"hands\" for hand tracking, \"controllers\" otherwise")]
    public string inputMode = "hands";

    [Header("Cameras (optional)")]
    public MultiViewCameraRig cameraRig;

    [Header("Director commands")]
    public UnityEvent<string> onSetActiveCamera;

    // ---------------- live values (set from game code or GameEventReporter) ----------------
    [NonSerialized] public bool leftGrabbing, rightGrabbing;
    [NonSerialized] public string chapter = "";
    [NonSerialized] public string status = "live";
    readonly Dictionary<string, double> _stats = new Dictionary<string, double>();
    float _startTime;

    readonly ConcurrentQueue<string> _outbox = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
    CancellationTokenSource _cts;
    long _seq;
    float _nextSend;
    public bool Connected { get; private set; }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;   // always "1.5", never "1,5"
    static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ") + "\"";
    static string N(double v) => v.ToString("0.###", Inv);
    static string V3(Vector3 v) => "[" + N(v.x) + "," + N(v.y) + "," + N(v.z) + "]";

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _startTime = Time.time;
    }
    void OnEnable() { _cts = new CancellationTokenSource(); _ = Task.Run(() => ConnectionLoop(_cts.Token)); }
    void OnDisable() { _cts?.Cancel(); }
    void Start() { if (reportSessionStart) Report("session_start"); }

    // ======================= public API =======================
    /// Report an event with optional key/value pairs: Report("glove_piece", "n", 2, "total", 4)
    public static void Report(string name, params object[] kv)
    {
        if (!Instance) return;
        var sb = new StringBuilder("{");
        for (int i = 0; i + 1 < kv.Length; i += 2)
        {
            if (sb.Length > 1) sb.Append(',');
            sb.Append(Q(Convert.ToString(kv[i], Inv))).Append(':').Append(JsonValue(kv[i + 1]));
        }
        Instance.SendEvent(name, sb.Append('}').ToString());
    }
    public static void ReportChapter(string chapterName)
    {
        if (!Instance) return;
        Instance.chapter = chapterName;
        Report("chapter", "name", chapterName);
    }
    /// NPC lines: shown as subtitles and translated for viewers (needs an API key on the bridge for non-English)
    public static void ReportDialogue(string speaker, string text) => Report("dialogue", "speaker", speaker, "text", text);
    public static void ReportSessionEnd() { if (Instance) Instance.status = "ended"; Report("session_end"); }
    public static void SetStat(string key, double value) { if (Instance) Instance._stats[key] = value; }
    public static void AddStat(string key, double amount = 1) { if (Instance) { Instance._stats.TryGetValue(key, out var v); Instance._stats[key] = v + amount; } }
    public static void SetGrab(bool left, bool grabbing) { if (!Instance) return; if (left) Instance.leftGrabbing = grabbing; else Instance.rightGrabbing = grabbing; }

    /// Any event with a ready-made JSON object as data
    public void SendEvent(string name, string dataJson = "{}")
    {
        long seq = Interlocked.Increment(ref _seq);
        Enqueue("{\"type\":\"event\",\"seq\":" + seq + ",\"t\":" + NowMs() + ",\"name\":" + Q(name) + ",\"data\":" + (string.IsNullOrEmpty(dataJson) ? "{}" : dataJson) + "}");
    }
    static string JsonValue(object v)
    {
        if (v == null) return "null";
        if (v is bool b) return b ? "true" : "false";
        if (v is string s) return Q(s);
        if (v is int || v is long || v is float || v is double || v is short || v is byte || v is decimal) return N(Convert.ToDouble(v, Inv));
        return Q(Convert.ToString(v, Inv));
    }

    // ======================= telemetry =======================
    void Update()
    {
        while (_inbox.TryDequeue(out var json)) HandleCommand(json);
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + 1f / sendRateHz;

        var h = head ? head : (Camera.main ? Camera.main.transform : null);
        var pos = h ? h.position : Vector3.zero;
        float yaw = h ? h.eulerAngles.y * Mathf.Deg2Rad : 0f;
        var sb = new StringBuilder(512);
        sb.Append("{\"type\":\"state\",\"game\":").Append(Q(gameId))
          .Append(",\"seq\":").Append(Interlocked.Increment(ref _seq))
          .Append(",\"t\":").Append(NowMs())
          .Append(",\"frame\":").Append(Time.frameCount)
          .Append(",\"session\":{\"status\":").Append(Q(status))
          .Append(",\"elapsed\":").Append(N(Time.time - _startTime))
          .Append(",\"chapter\":").Append(Q(chapter)).Append('}')
          .Append(",\"player\":{\"pos\":").Append(V3(new Vector3(pos.x, pos.y - 1.6f, pos.z)))   // feet ≈ head − 1.6 m
          .Append(",\"yaw\":").Append(N(yaw))
          .Append(",\"input\":").Append(Q(inputMode))
          .Append(",\"hands\":{\"left\":").Append(Hand(leftHand, leftGrabbing))
          .Append(",\"right\":").Append(Hand(rightHand, rightGrabbing)).Append("}}")
          .Append(",\"stats\":{");
        bool first = true;
        foreach (var kv in _stats) { if (!first) sb.Append(','); first = false; sb.Append(Q(kv.Key)).Append(':').Append(N(kv.Value)); }
        sb.Append("},\"activeCam\":").Append(Q(cameraRig ? cameraRig.ActiveCameraId : "")).Append('}');
        Enqueue(sb.ToString());
    }
    static string Hand(Transform t, bool grab)
    {
        bool tracked = t && t.gameObject.activeInHierarchy;
        return "{\"tracked\":" + (tracked ? "true" : "false") + ",\"grab\":" + (grab ? "true" : "false") + (tracked ? ",\"pos\":" + V3(t.position) : "") + "}";
    }
    void Enqueue(string json)
    {
        while (_outbox.Count > 200) _outbox.TryDequeue(out _);   // offline: drop stale telemetry instead of growing forever
        _outbox.Enqueue(json);
    }

    [Serializable] class CmdMsg { public string type, cmd, camId; }
    void HandleCommand(string json)
    {
        CmdMsg c;
        try { c = JsonUtility.FromJson<CmdMsg>(json); } catch { return; }
        if (c == null || c.type != "cmd") return;
        if (c.cmd == "setActiveCamera" && !string.IsNullOrEmpty(c.camId))
        {
            if (cameraRig) cameraRig.SetActive(c.camId);
            onSetActiveCamera?.Invoke(c.camId);
        }
    }

    // ======================= background networking =======================
    async Task ConnectionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using (var ws = new ClientWebSocket())
            {
                try
                {
                    await ws.ConnectAsync(new Uri(url), ct);
                    Connected = true;
                    Debug.Log("[TelemetryBridge] connected to " + url);
                    var recv = ReceiveLoop(ws, ct);
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        if (_outbox.TryDequeue(out var json))
                        {
                            var bytes = Encoding.UTF8.GetBytes(json);
                            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                        }
                        else await Task.Delay(2, ct);
                    }
                    await recv;
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Debug.LogWarning("[TelemetryBridge] " + e.Message + " (retrying in 2 s)");
                }
                Connected = false;
            }
            try { await Task.Delay(2000, ct); } catch { }
        }
    }
    async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[8192];
        var sb = new StringBuilder();
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                if (r.EndOfMessage) { _inbox.Enqueue(sb.ToString()); sb.Clear(); }
            }
        }
        catch { /* ConnectionLoop reconnects */ }
    }
}
