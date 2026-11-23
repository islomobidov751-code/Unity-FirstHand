// FFmpegStreamer.cs — streams every MultiViewCameraRig feed to MediaMTX (one RTSP path per camera: cam1..cam4).
// ------------------------------------------------------------------------------------------------------------
// How it works: each feed's RenderTexture is copied off the GPU with AsyncGPUReadback (never stalls the frame),
// the raw pixels are piped into one FFmpeg process per camera, and FFmpeg encodes H.264 and publishes to
// rtsp://<mediaHost>:8554/camN. The dashboard then plays them over WebRTC from MediaMTX.
//
// Requirements: FFmpeg installed (ffmpeg on PATH, or set ffmpegPath), MediaMTX running.
// NVIDIA GPU? Tick useNvenc: encoding moves to the GPU and frees the CPU for the game.
// Picture upside down? Toggle flipVertically (depends on the graphics API).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class FFmpegStreamer : MonoBehaviour
{
    public MultiViewCameraRig rig;
    [Tooltip("\"ffmpeg\" if it is on PATH, otherwise the full path, e.g. C:\\ffmpeg\\bin\\ffmpeg.exe")]
    public string ffmpegPath = "ffmpeg";
    public string mediaHost = "127.0.0.1";
    public int rtspPort = 8554;
    [Range(10, 60)] public int fps = 30;
    public int bitrateKbps = 2500;
    public bool useNvenc = false;
    public bool flipVertically = true;

    class Pipe
    {
        public string id; public RenderTexture rt; public Process proc; public Stream stdin;
        public readonly ConcurrentQueue<byte[]> frames = new ConcurrentQueue<byte[]>();
        public Thread writer; public volatile bool running; public bool waiting;
    }
    readonly List<Pipe> _pipes = new List<Pipe>();
    float _next;

    void Start()
    {
        if (!rig) rig = FindAnyObjectByType<MultiViewCameraRig>();
        if (!rig) { Debug.LogError("[FFmpegStreamer] no MultiViewCameraRig found"); enabled = false; return; }
        foreach (var f in rig.Feeds)
        {
            if (!f.texture) { Debug.LogError("[FFmpegStreamer] feed " + f.id + " has no RenderTexture yet"); continue; }
            var p = new Pipe { id = f.id, rt = f.texture };
            if (Launch(p)) _pipes.Add(p);
        }
    }

    bool Launch(Pipe p)
    {
        int w = p.rt.width, h = p.rt.height, gop = fps;
        string enc = useNvenc
            ? $"-c:v h264_nvenc -preset p1 -tune ll -zerolatency 1 -rc cbr"
            : $"-c:v libx264 -preset ultrafast -tune zerolatency -profile:v baseline";
        string args = $"-hide_banner -loglevel warning -f rawvideo -pix_fmt rgba -s {w}x{h} -r {fps} -i - " +
                      (flipVertically ? "-vf vflip " : "") +
                      $"{enc} -pix_fmt yuv420p -g {gop} -bf 0 -b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps / 2}k " +
                      $"-f rtsp -rtsp_transport tcp rtsp://{mediaHost}:{rtspPort}/{p.id}";
        try
        {
            p.proc = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpegPath, args)
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true },
                EnableRaisingEvents = true,
            };
            p.proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[ffmpeg {p.id}] {e.Data}"); };
            p.proc.Start();
            p.proc.BeginErrorReadLine();
            p.stdin = p.proc.StandardInput.BaseStream;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FFmpegStreamer] could not start ffmpeg ({e.Message}). Is FFmpeg installed and on PATH?");
            return false;
        }
        p.running = true;
        p.writer = new Thread(() => WriteLoop(p)) { IsBackground = true, Name = "ffmpeg-" + p.id };
        p.writer.Start();
        Debug.Log($"[FFmpegStreamer] {p.id} → rtsp://{mediaHost}:{rtspPort}/{p.id} ({w}x{h} @ {fps} fps)");
        return true;
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Mathf.Max(_next + 1f / fps, Time.unscaledTime - 0.1f);
        foreach (var p in _pipes)
        {
            if (!p.running || p.waiting) continue;   // previous readback still in flight: skip, never queue up latency
            p.waiting = true;
            AsyncGPUReadback.Request(p.rt, 0, TextureFormat.RGBA32, req =>
            {
                p.waiting = false;
                if (req.hasError || !p.running) return;
                if (p.frames.Count < 2) p.frames.Enqueue(req.GetData<byte>().ToArray());
            });
        }
    }

    static void WriteLoop(Pipe p)
    {
        try
        {
            while (p.running)
            {
                if (p.frames.TryDequeue(out var f)) { p.stdin.Write(f, 0, f.Length); p.stdin.Flush(); }
                else Thread.Sleep(2);
            }
        }
        catch (IOException e) { Debug.LogWarning($"[FFmpegStreamer] {p.id} stopped: {e.Message} (is MediaMTX running?)"); p.running = false; }
    }

    void OnDestroy()
    {
        foreach (var p in _pipes)
        {
            p.running = false;
            try { p.stdin?.Close(); } catch { }
            try { if (p.proc != null && !p.proc.WaitForExit(800)) p.proc.Kill(); } catch { }
        }
        _pipes.Clear();
    }
}
