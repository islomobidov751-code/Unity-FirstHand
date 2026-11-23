using System.Collections.Generic;
using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Rule-based auto zoom. Watches which camera is on air and the current game event, and eases that camera's
    /// field of view in (poke = zoom in) or out (idle = a little wider). Put it on the ControlRoom object.
    /// </summary>
    public class ZoomDirector : MonoBehaviour
    {
        public FullTelemetryLogger logger;
        [Tooltip("Camera object names that must never zoom")]
        public string[] skipCameras = { "WIDE_SCENE_FIXED", "FREE_ORBIT", "FREE_FLY" };
        [Tooltip("Events that zoom IN (name must match the Event column)")]
        public string[] tightEvents = { "INTERACT_POKE" };
        [Tooltip("Events that zoom OUT a little")]
        public string[] wideEvents = { "IDLE" };
        [Range(0.4f, 1f)] public float tightFactor = 0.70f;   // 0.70 = field of view becomes 70 percent (zoomed in)
        [Range(1f, 1.4f)] public float wideFactor = 1.10f;
        public float seconds = 1.2f;            // time to ease to the new zoom
        public float holdBeforeZoom = 0.6f;     // no zoom right after a cut

        readonly Dictionary<string, Camera> cams = new Dictionary<string, Camera>();
        readonly Dictionary<string, float> baseFov = new Dictionary<string, float>();
        Camera cur; string curName = ""; float vel; double onAirSince; int eventIdx = -1; float nextScan;

        void ScanCameras()
        {
            cams.Clear();
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                cams[c.name] = c;
                if (c.transform.parent != null && !cams.ContainsKey(c.transform.parent.name)) cams[c.transform.parent.name] = c;
            }
        }

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

        static bool In(string[] list, string s) { foreach (var x in list) if (x == s) return true; return false; }

        void LateUpdate()
        {
            if (logger == null) return;
            string air = logger.activeCamera;
            if (string.IsNullOrEmpty(air)) return;

            if (air != curName)
            {
                if (cur != null && baseFov.ContainsKey(curName)) cur.fieldOfView = baseFov[curName];   // reset the camera that left the air
                curName = air; vel = 0f; onAirSince = Time.timeAsDouble;
                if (!cams.ContainsKey(air) && Time.unscaledTime > nextScan) { ScanCameras(); nextScan = Time.unscaledTime + 2f; }
                cur = cams.ContainsKey(air) ? cams[air] : null;
                if (cur == null) { Debug.LogWarning("ZoomDirector: no Camera named " + air); return; }
                if (!baseFov.ContainsKey(air)) baseFov[air] = cur.fieldOfView;
            }
            if (cur == null || In(skipCameras, curName)) return;
            if (Time.timeAsDouble - onAirSince < holdBeforeZoom) return;

            string ev = CurrentEvent();
            float factor = In(tightEvents, ev) ? tightFactor : In(wideEvents, ev) ? wideFactor : 1f;
            cur.fieldOfView = Mathf.SmoothDamp(cur.fieldOfView, baseFov[curName] * factor, ref vel, seconds);
        }
    }
}
