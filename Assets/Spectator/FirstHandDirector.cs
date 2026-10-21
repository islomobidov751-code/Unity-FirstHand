using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthStar
{
    /// <summary>
    /// The report's rule-based director (Section 4), for FirstHand:
    ///   REESTABLISH (teleport or scene load)  > GRAB_TWO_HAND > INTERACT_POKE > GRAB_ONE_HAND > IDLE
    /// with a minimum shot time of 2 s (hysteresis). It writes the chosen camera into dashboard.suggestedCamera,
    /// so the control room follows it while the dashboard is in AUTO mode (clicking a camera = MANUAL override).
    /// It also points the hand cameras at whichever hand is active, and switches to an alternate side camera when
    /// the main over-the-shoulder camera is blocked (occlusion recovery).
    /// </summary>
    public class FirstHandDirector : MonoBehaviour
    {
        public HandInteractionState hands;
        public ControlRoomDashboard dashboard;
        public FullTelemetryLogger logger;
        public Transform head, leftHand, rightHand;

        [Header("Camera names (must match the cameras in the dashboard's view list)")]
        public string idleCam = "PLAYER_STABILIZED";
        public string pokeCam = "OTS_CLOSE";
        public string oneHandCam = "OTS_HAND";
        public string twoHandCam = "CLOSE_BOTH_HANDS";
        public string reestablishCam = "WIDE_FOLLOW";
        public string altCam = "OTS_HAND_ALT";

        [Header("Rules")]
        public float minShotSeconds = 2f;
        public float reestablishSeconds = 3f;
        public float teleportJumpMeters = 0.75f;
        public bool useOcclusionRecovery = true;

        [Header("Testing without a headset: force an event from the Inspector while in Play Mode")]
        public DebugEvent debugEvent = DebugEvent.None;
        public enum DebugEvent { None, GRAB_ONE_HAND, GRAB_TWO_HAND, INTERACT_POKE, REESTABLISH }

        public string CurrentEvent { get; private set; }
        public string CurrentCamera { get; private set; }

        readonly Dictionary<string, SpectatorFollowCamera> cams = new Dictionary<string, SpectatorFollowCamera>();
        float lastSwitch = -999f, reestablishUntil = -1f;
        Vector3 prevHead;
        bool hasPrev;

        void Start()
        {
            if (hands == null) hands = FindAnyObjectByType<HandInteractionState>();
            if (dashboard == null) dashboard = FindAnyObjectByType<ControlRoomDashboard>();
            if (logger == null) logger = FindAnyObjectByType<FullTelemetryLogger>();
            if (head == null) head = FindT("CenterEyeAnchor");
            if (leftHand == null) leftHand = FindT("LeftHandAnchor");
            if (rightHand == null) rightHand = FindT("RightHandAnchor");

            GameObject root = GameObject.Find("SpectatorCameras");
            if (root != null)
                foreach (SpectatorFollowCamera f in root.GetComponentsInChildren<SpectatorFollowCamera>(true)) cams[f.name] = f;
            else Debug.LogWarning("FirstHandDirector: no 'SpectatorCameras' object. Run Tools > Spectator > Create FirstHand Cameras.");
        }

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        void OnSceneLoaded(Scene s, LoadSceneMode m) { reestablishUntil = Time.time + reestablishSeconds; }

        void Update()
        {
            if (hands == null || dashboard == null) return;
            DetectTeleport();

            string ev, cam;
            Transform active = null;
            int grabbing = hands.GrabbingHands;
            bool anyPoke = hands.AnyPoke;
            bool reestablish = Time.time < reestablishUntil;
            bool leftOnly = hands.LeftGrab && !hands.RightGrab;

            switch (debugEvent)   // test overrides (None = use the real hand data)
            {
                case DebugEvent.GRAB_ONE_HAND: grabbing = 1; break;
                case DebugEvent.GRAB_TWO_HAND: grabbing = 2; break;
                case DebugEvent.INTERACT_POKE: anyPoke = true; break;
                case DebugEvent.REESTABLISH: reestablish = true; break;
            }

            if (reestablish) { ev = "REESTABLISH"; cam = reestablishCam; }
            else if (grabbing >= 2) { ev = "GRAB_TWO_HAND"; cam = twoHandCam; }
            else if (anyPoke)
            {
                ev = "INTERACT_POKE"; cam = pokeCam;
                active = (hands.LeftPoke && !hands.RightPoke) ? leftHand : rightHand;
            }
            else if (grabbing == 1)
            {
                ev = "GRAB_ONE_HAND"; cam = oneHandCam;
                active = leftOnly ? leftHand : rightHand;
            }
            else { ev = "IDLE"; cam = idleCam; }

            // Aim the hand cameras at the active hand
            if (active != null) { Aim(pokeCam, active); Aim(oneHandCam, active); Aim(altCam, active); }

            // Occlusion recovery: if the main shot is blocked, use the alternate side camera
            if (useOcclusionRecovery && active != null && cam == oneHandCam && Blocked(cam, active) && !Blocked(altCam, active))
                cam = altCam;

            // Hysteresis: hold each shot for at least minShotSeconds (re-establish shots may interrupt)
            bool bypass = ev == "REESTABLISH";
            if (string.IsNullOrEmpty(CurrentCamera) || (cam != CurrentCamera && (bypass || Time.time - lastSwitch >= minShotSeconds)))
            {
                CurrentCamera = cam;
                lastSwitch = Time.time;
            }
            CurrentEvent = ev;
            dashboard.suggestedCamera = CurrentCamera;
            if (logger != null) logger.eventLabel = ev;
        }

        void DetectTeleport()
        {
            if (head == null) return;
            Vector3 p = head.position;
            if (hasPrev && (p - prevHead).magnitude > teleportJumpMeters) reestablishUntil = Time.time + reestablishSeconds;
            prevHead = p;
            hasPrev = true;
        }

        void Aim(string camName, Transform hand)
        {
            SpectatorFollowCamera f;
            if (hand != null && cams.TryGetValue(camName, out f)) f.target = hand;
        }

        bool Blocked(string camName, Transform subject)
        {
            SpectatorFollowCamera f;
            if (subject == null || !cams.TryGetValue(camName, out f)) return false;
            Vector3 from = f.transform.position, dir = subject.position - from;
            float d = dir.magnitude;
            if (d < 0.15f) return false;
            return Physics.Raycast(from, dir / d, d - 0.1f, ~0, QueryTriggerInteraction.Ignore);
        }

        static Transform FindT(string n) { var g = GameObject.Find(n); return g != null ? g.transform : null; }
    }
}
