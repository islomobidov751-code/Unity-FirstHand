using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Connects the Python AI (director_server.py) to the control room. Works in both games.
    ///   every 1/sendHz s : sends the logger's latest values (and each camera's visibility) to Python over UDP
    ///   Python replies   : one camera name
    ///   this script      : applies safety rules (minimum shot time, never a camera that cannot see the subject)
    ///                      and writes the result into dashboard.suggestedCamera (the dashboard follows it in AUTO mode)
    /// If Python is not running or stops answering, this script does nothing, so the rule director (FirstHand)
    /// or the current camera stays in control. Runs after the other scripts so the AI choice wins when it is alive.
    /// Needs the dashboard to be in AUTO mode; clicking a camera in the dashboard is always a manual override.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class AIDirectorBridge : MonoBehaviour
    {
        public ControlRoomDashboard dashboard;
        public FullTelemetryLogger logger;
        public bool useAI = true;
        public string aiHost = "127.0.0.1";
        public int aiPort = 5005;        // Python listens here
        public int listenPort = 5006;    // Unity listens here
        public float sendHz = 10f;
        public float aiTimeoutSeconds = 0.5f;
        public float minShotSeconds = 2f;

        public string Status { get; private set; }

        UdpClient udp;
        string proposal, current;
        float lastReply = -999f, lastSwitch = -999f, sendTimer;
        bool wasAlive;
        Dictionary<string, int> visIndex;

        void Start()
        {
            if (dashboard == null) dashboard = FindAnyObjectByType<ControlRoomDashboard>();
            if (logger == null) logger = FindAnyObjectByType<FullTelemetryLogger>();
            Status = "AI off";
            try
            {
                udp = new UdpClient(listenPort);
                udp.Client.Blocking = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("AIDirectorBridge: UDP not started (" + e.Message + "). The AI will be off.");
                udp = null;
            }
        }

        void Update()
        {
            if (!useAI || udp == null || dashboard == null || logger == null || logger.Names.Length == 0) return;
            if (visIndex == null) BuildVisIndex();

            Receive();
            sendTimer += Time.unscaledDeltaTime;
            if (sendTimer >= 1f / Mathf.Max(1f, sendHz)) { sendTimer = 0f; Send(); }

            bool alive = proposal != null && Time.unscaledTime - lastReply < aiTimeoutSeconds;
            if (alive != wasAlive)
            {
                Debug.Log(alive ? "AIDirectorBridge: AI connected" : "AIDirectorBridge: AI silent, falling back to the rule director");
                wasAlive = alive;
            }
            Status = alive ? "AI on" : "AI silent";
            if (!alive) return;

            // Safety rules
            if (string.IsNullOrEmpty(current)) { current = proposal; lastSwitch = Time.time; }
            else if (proposal != current && Time.time - lastSwitch >= minShotSeconds && IsVisible(proposal))
            {
                current = proposal;
                lastSwitch = Time.time;
            }
            dashboard.suggestedCamera = current;
        }

        void BuildVisIndex()
        {
            visIndex = new Dictionary<string, int>();
            for (int i = 0; i < logger.Names.Length; i++)
                if (logger.Names[i].StartsWith("Vis_")) visIndex[logger.Names[i].Substring(4)] = i;
        }

        bool IsVisible(string cam)
        {
            int i;
            if (!visIndex.TryGetValue(cam, out i)) return true;   // unknown camera: let it through
            return logger.Values[i] != "0";
        }

        void Send()
        {
            var sb = new StringBuilder("current=").Append(logger.activeCamera);
            for (int i = 0; i < logger.Names.Length; i++)
                sb.Append(';').Append(logger.Names[i]).Append('=').Append(Clean(logger.Values[i]));
            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
            try { udp.Send(data, data.Length, aiHost, aiPort); }
            catch (SocketException) { }
        }

        static string Clean(string v) { return v == null ? "" : v.Replace(';', '_').Replace('=', '_'); }

        void Receive()
        {
            try
            {
                while (udp.Available > 0)
                {
                    IPEndPoint ep = null;
                    string s = Encoding.UTF8.GetString(udp.Receive(ref ep)).Trim();
                    if (s.Length > 0 && s.Length < 64) { proposal = s; lastReply = Time.unscaledTime; }
                }
            }
            catch (SocketException) { }
        }

        void OnDestroy() { if (udp != null) udp.Close(); }
    }
}
