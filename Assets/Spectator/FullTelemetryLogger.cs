using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthStar
{
    /// <summary>
    /// FirstHand version. Deliberately uses the SAME class name and public members as the NorthStar logger, so
    /// ControlRoomDashboard and StreamServer work unchanged. Use this file only in the FirstHand project
    /// (a project cannot contain two classes with the same name).
    /// Logs every variable we can reach at logHz, continuously: head, hands, rig, speeds, interactor states,
    /// teleport jumps, scene, director event/camera, and a visibility value per spectator camera.
    /// Add a variable with ONE line in BuildChannels().
    /// </summary>
    public class FullTelemetryLogger : MonoBehaviour
    {
        [Header("Session")]
        public string playerName = "Player1";
        public float logHz = 10f;
        public bool logging = true;

        [Header("Auto-found by name if left empty (confirm the real names in the hierarchy)")]
        public Transform head, leftHand, rightHand, trackingSpace, handsMidpoint;

        [Header("Auto-filled from the 'SpectatorCameras' object if empty")]
        public Camera[] watchedCameras;
        public float teleportJumpMeters = 0.75f;

        // Filled in by the director and the dashboard
        [HideInInspector] public string activeCamera = "";
        [HideInInspector] public string mode = "";
        [HideInInspector] public string eventLabel = "";

        public string[] Names { get; private set; } = new string[0];
        public string[] Values { get; private set; } = new string[0];
        public string SessionPath { get; private set; }
        public int RowCount { get; private set; }
        public float Fps { get; private set; }

        readonly List<string> names = new List<string>();
        readonly List<Func<string>> getters = new List<Func<string>>();
        readonly StringBuilder buffer = new StringBuilder();
        HandInteractionState hands;
        float timer, headSpeed, headAngSpeed, lHandSpeed, rHandSpeed;
        Vector3 prevHead, prevL, prevR;
        Quaternion prevHeadRot;
        bool hasPrev, jumpLatched;

        static string F(float v) { return v.ToString("F4", CultureInfo.InvariantCulture); }
        static string B(bool v) { return v ? "1" : "0"; }

        void Start()
        {
            hands = FindAnyObjectByType<HandInteractionState>();
            if (head == null) head = FindByName("CenterEyeAnchor");
            if (leftHand == null) leftHand = FindByName("LeftHandAnchor");
            if (rightHand == null) rightHand = FindByName("RightHandAnchor");
            if (trackingSpace == null) trackingSpace = FindByName("TrackingSpace");
            if (handsMidpoint == null) handsMidpoint = FindByName("HandsMidpoint");

            if (watchedCameras == null || watchedCameras.Length == 0)
            {
                GameObject root = GameObject.Find("SpectatorCameras");
                if (root != null) watchedCameras = root.GetComponentsInChildren<Camera>(true);
            }

            if (head == null) Debug.LogWarning("FullTelemetryLogger: head (CenterEyeAnchor) not found");
            if (leftHand == null || rightHand == null) Debug.LogWarning("FullTelemetryLogger: hand anchors not found");
            if (hands == null) Debug.LogWarning("FullTelemetryLogger: no HandInteractionState in the scene (interaction columns will be empty)");

            BuildChannels();
            Names = names.ToArray();
            Values = new string[names.Count];
            StartNewSession();
        }

        void BuildChannels()
        {
            Add("Time", () => F(Time.time));
            Add("Frame", () => Time.frameCount.ToString());
            Add("FPS", () => F(Fps));
            Add("Scene", () => SceneManager.GetActiveScene().name);
            Add("Player", () => playerName);
            Add("Event", () => eventLabel);
            Add("ActiveCamera", () => activeCamera);
            Add("Mode", () => mode);

            // Biometric: head and hands
            AddPose("Head", () => head);
            AddPose("LeftHand", () => leftHand);
            AddPose("RightHand", () => rightHand);
            Add("LHandState", () => hands != null ? hands.LeftState : "");
            Add("RHandState", () => hands != null ? hands.RightState : "");

            // Kinematic
            AddPose("Rig", () => trackingSpace);
            Add("HeadSpeed", () => F(headSpeed));
            Add("HeadAngSpeed", () => F(headAngSpeed));
            Add("LHandSpeed", () => F(lHandSpeed));
            Add("RHandSpeed", () => F(rHandSpeed));
            Add("HandsDistance", () => leftHand != null && rightHand != null ? F(Vector3.Distance(leftHand.position, rightHand.position)) : "");
            Add("TeleportJump", () => { string v = B(jumpLatched); jumpLatched = false; return v; });

            // Interactive / semantic (from the Interaction SDK interactors)
            Add("LGrab", () => hands != null ? B(hands.LeftGrab) : "");
            Add("RGrab", () => hands != null ? B(hands.RightGrab) : "");
            Add("LPoke", () => hands != null ? B(hands.LeftPoke) : "");
            Add("RPoke", () => hands != null ? B(hands.RightPoke) : "");
            Add("GrabbingHands", () => hands != null ? hands.GrabbingHands.ToString() : "");
            Add("TeleportAiming", () => hands != null ? B(hands.TeleportAiming) : "");
            Add("Interactors", () => hands != null ? hands.InteractorCount.ToString() : "");

            // Spatial: can each spectator camera see the hands?
            if (watchedCameras != null)
                foreach (Camera c in watchedCameras)
                {
                    Camera cam = c;
                    if (cam != null) Add("Vis_" + cam.name, () => B(Visible(cam)));
                }

            // ---- ADD MORE VARIABLES HERE, one line each. Examples (check the real names first): ----
            // Add("HandTracked", () => ...);       // hand-tracking validity (e.g. from the OVRHand component)
            // Add("HeldObject", () => ...);        // name of the grabbed object
        }

        void Add(string n, Func<string> g) { names.Add(n); getters.Add(g); }

        void AddVec(string p, Func<Vector3?> g)
        {
            Add(p + "X", () => { var v = g(); return v.HasValue ? F(v.Value.x) : ""; });
            Add(p + "Y", () => { var v = g(); return v.HasValue ? F(v.Value.y) : ""; });
            Add(p + "Z", () => { var v = g(); return v.HasValue ? F(v.Value.z) : ""; });
        }

        void AddPose(string p, Func<Transform> t)
        {
            AddVec(p + "Pos", () => { var tr = t(); return tr != null ? tr.position : (Vector3?)null; });
            AddVec(p + "Rot", () => { var tr = t(); return tr != null ? tr.eulerAngles : (Vector3?)null; });
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) Fps = Mathf.Lerp(Fps, 1f / dt, 0.1f);
            UpdateMotion();

            timer += dt;
            if (timer < 1f / Mathf.Max(1f, logHz)) return;
            timer = 0f;

            var sb = new StringBuilder();
            for (int i = 0; i < getters.Count; i++)
            {
                string v = getters[i]() ?? "";
                Values[i] = v;
                sb.Append(v.Replace(',', ';'));
                if (i < getters.Count - 1) sb.Append(',');
            }
            if (logging)
            {
                buffer.AppendLine(sb.ToString());
                RowCount++;
                if (buffer.Length > 20000) Flush();
            }
        }

        void UpdateMotion()
        {
            if (head == null) return;
            float dt = Time.deltaTime;
            Vector3 l = leftHand != null ? leftHand.position : prevL;
            Vector3 r = rightHand != null ? rightHand.position : prevR;
            if (hasPrev && dt > 0f)
            {
                float jump = (head.position - prevHead).magnitude;
                if (jump > teleportJumpMeters) jumpLatched = true;
                headSpeed = jump / dt;
                headAngSpeed = Quaternion.Angle(prevHeadRot, head.rotation) / dt;
                lHandSpeed = (l - prevL).magnitude / dt;
                rHandSpeed = (r - prevR).magnitude / dt;
            }
            prevHead = head.position; prevHeadRot = head.rotation; prevL = l; prevR = r; hasPrev = true;
        }

        bool Visible(Camera cam)
        {
            if (cam == null) return false;
            Vector3 subject;
            if (handsMidpoint != null) subject = handsMidpoint.position;
            else if (head != null) subject = head.position;
            else return false;
            Vector3 from = cam.transform.position, dir = subject - from;
            float d = dir.magnitude;
            if (d < 0.15f) return true;
            return !Physics.Raycast(from, dir / d, d - 0.1f, ~0, QueryTriggerInteraction.Ignore);
        }

        static Transform FindByName(string n) { var g = GameObject.Find(n); return g != null ? g.transform : null; }

        public void StartNewSession()
        {
            Flush();
            string dir = Path.Combine(Application.persistentDataPath, "Sessions");
            Directory.CreateDirectory(dir);
            SessionPath = Path.Combine(dir, "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            File.WriteAllText(SessionPath, string.Join(",", Names) + "\n");
            RowCount = 0;
        }

        public void Flush()
        {
            if (buffer.Length == 0 || SessionPath == null) return;
            File.AppendAllText(SessionPath, buffer.ToString());
            buffer.Clear();
        }

        void OnDestroy() { Flush(); }
        void OnApplicationQuit() { Flush(); }
    }
}
