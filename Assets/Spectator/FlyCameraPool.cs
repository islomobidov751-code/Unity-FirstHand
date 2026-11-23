using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace NorthStar
{
    /// <summary>One personal fly camera. The web page sets the input values, FreeFlyCamera moves the camera.</summary>
    public class FlySlot
    {
        public int index;
        public string name;
        public volatile string owner;                       // viewer id, or null while the slot is free
        public volatile float moveX, moveY, moveZ, yaw, pitch;
        public volatile bool fast, resetRequested;
        long stamp;
        public Camera cam;
        public RenderTexture rt;
        public FreeFlyCamera fly;
        public void Touch() { Interlocked.Exchange(ref stamp, Stopwatch.GetTimestamp()); }
        public double Age() { return (Stopwatch.GetTimestamp() - Interlocked.Read(ref stamp)) / (double)Stopwatch.Frequency; }
    }

    /// <summary>
    /// A small pool of fly cameras. Each viewer who asks gets their OWN camera until they leave or stop moving.
    /// Every active camera costs one extra render and one JPEG encode on the PC, so the number of slots is limited.
    /// Put this on an empty object (not under SpectatorCameras) and drag it into StreamServer's Fly Pool field.
    /// </summary>
    public class FlyCameraPool : MonoBehaviour
    {
        [Range(1, 8)] public int slots = 3;
        [Tooltip("Camera object to copy the render settings from (a fixed camera, such as WIDE_SCENE_FIXED)")]
        public string templateName = "WIDE_SCENE_FIXED";
        public string anchorName = "CenterEyeAnchor";
        public int width = 640, height = 360;
        [Tooltip("A viewer who sends nothing for this long loses the camera")]
        public float idleSeconds = 8f;

        public readonly List<FlySlot> Slots = new List<FlySlot>();
        readonly object gate = new object();

        void Start()
        {
            Transform anchor = null;
            GameObject a = GameObject.Find(anchorName);
            if (a != null) anchor = a.transform;
            else UnityEngine.Debug.LogWarning("FlyCameraPool: no object named " + anchorName + ". Fly cameras will start where the template camera is.");
            GameObject template = GameObject.Find(templateName);
            if (template == null) UnityEngine.Debug.LogWarning("FlyCameraPool: no object named " + templateName + ". Using plain cameras.");

            for (int i = 1; i <= slots; i++)
            {
                GameObject go = template != null ? Instantiate(template) : new GameObject();
                go.name = "FLY_" + i;
                go.tag = "Untagged";
                go.transform.SetParent(transform, true);
                foreach (var mb in go.GetComponents<MonoBehaviour>())
                    if (!mb.GetType().Name.Contains("AdditionalCameraData")) Destroy(mb);   // keep only render settings
                foreach (var al in go.GetComponents<AudioListener>()) Destroy(al);
                Camera cam = go.GetComponent<Camera>();
                if (cam == null) cam = go.AddComponent<Camera>();
                var rt = new RenderTexture(width, height, 24);
                rt.Create();
                cam.targetTexture = rt;
                cam.enabled = false;                                  // switched on only while a viewer uses it
                var s = new FlySlot { index = i, name = go.name, cam = cam, rt = rt };
                var fly = go.AddComponent<FreeFlyCamera>();
                fly.slot = s; fly.anchor = anchor; s.fly = fly;
                Slots.Add(s);
            }
        }

        void Update()
        {
            foreach (var s in Slots)
            {
                bool inUse = s.owner != null;
                if (inUse && s.Age() > idleSeconds) { s.owner = null; inUse = false; }
                if (s.cam != null && s.cam.enabled != inUse) s.cam.enabled = inUse;
            }
        }

        // ---- called from the network thread ----
        public FlySlot Join(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (gate)
            {
                foreach (var s in Slots) if (s.owner == id) { s.Touch(); return s; }
                foreach (var s in Slots)
                    if (s.owner == null)
                    {
                        s.moveX = s.moveY = s.moveZ = 0f; s.fast = false;
                        s.yaw = 0f; s.pitch = 10f; s.resetRequested = true;
                        s.Touch(); s.owner = id;
                        return s;
                    }
            }
            return null;
        }

        public void Leave(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (gate) foreach (var s in Slots) if (s.owner == id) s.owner = null;
        }

        public FlySlot Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (gate) foreach (var s in Slots) if (s.owner == id) return s;
            return null;
        }

        void OnDestroy() { foreach (var s in Slots) if (s.rt != null) s.rt.Release(); }
    }
}
