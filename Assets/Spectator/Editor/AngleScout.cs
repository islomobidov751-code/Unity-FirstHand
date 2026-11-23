#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using NorthStar;

/// <summary>
/// Tools > Spectator > Angle Scout
/// Finds good camera angles around a target (a hand, or a mission object on the map):
///   1. places many candidate cameras around the target (rings of yaw x distance x height)
///   2. scores each: can it see the target (several raycasts), how big the target appears, how comfortable the elevation is,
///      and it rejects cameras inside walls or props
///   3. picks the best few that are DIFFERENT from each other
///   4. saves a CSV and a screenshot per winner, and can create them as ready-to-use cameras
/// For hands: press Play with a headset (or moving hands), tick "Auto-sample in Play Mode", and move the hands around.
/// Scores are averaged over all samples, so the winners work during real movement, not just in one pose.
/// IMPORTANT: visibility uses raycasts, so railings, walls and props need COLLIDERS. A prop without a collider is invisible to the check.
/// </summary>
public class AngleScout : EditorWindow
{
    [MenuItem("Tools/Spectator/Angle Scout")]
    static void Open() { GetWindow<AngleScout>("Angle Scout"); }

    // ---- settings ----
    public string targetPath = "", framePath = "";
    public bool mapMode = false;
    public string distancesText = "0.45, 0.7, 1.0", heightsText = "0.15, 0.4";
    public float yawStep = 30f, fov = 60f, targetSize = 0.22f, idealSize = 0.28f, probeRadius = 0.07f, idealElevation = 20f;
    public float minVisibility = 0.85f, clearance = 0.12f, minSeparation = 0.35f;
    public int keep = 5;
    public bool autoSample;
    public float sampleInterval = 1f;

    [Serializable]
    public class Acc
    {
        public int yi, di, hi;
        public float yaw, dist, height, score, vis, size;
        public int n;
        public Vector3 lastPos, local;
    }
    [Serializable]
    class Saved { public List<Acc> items = new List<Acc>(); public Vector3 center; public int scans; }

    readonly Dictionary<Vector3Int, Acc> acc = new Dictionary<Vector3Int, Acc>();
    readonly List<Acc> top = new List<Acc>();
    Vector3 lastCenter;
    double nextSample;
    int scans;
    string status = "";
    float usedProbe;
    Vector2 scroll;

    static string Dir { get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AngleScout"); } }
    static string LatestFile { get { return Path.Combine(Dir, "latest.json"); } }

    void OnEnable()
    {
        SceneView.duringSceneGui += OnScene;
        EditorApplication.update += Tick;
        LoadLatest();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnScene;
        EditorApplication.update -= Tick;
    }

    // ---------------------------------------------------------------- window
    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("Hand: set the target to the hand anchor and the frame to the head, then Auto-sample in Play Mode while the hands move.\nMap: select a mission object in the Hierarchy, use it as the target, press Map preset, then Scan once.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Hand preset (right hand)")) HandPreset();
        if (GUILayout.Button("Map preset")) MapPreset();
        EditorGUILayout.EndHorizontal();

        targetPath = EditorGUILayout.TextField("Target path", targetPath);
        framePath = EditorGUILayout.TextField("Frame path (head)", framePath);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Use selected as target")) targetPath = PathOf(Selection.activeTransform);
        if (GUILayout.Button("Use selected as frame")) framePath = PathOf(Selection.activeTransform);
        EditorGUILayout.EndHorizontal();

        mapMode = EditorGUILayout.Toggle("Map mode (fixed cameras)", mapMode);
        distancesText = EditorGUILayout.TextField("Distances (m)", distancesText);
        heightsText = EditorGUILayout.TextField("Heights above target (m)", heightsText);
        yawStep = EditorGUILayout.FloatField("Angle step (degrees)", yawStep);
        fov = EditorGUILayout.FloatField("Field of view", fov);
        targetSize = EditorGUILayout.FloatField("Target size (m, hand = 0.22)", targetSize);
        idealSize = EditorGUILayout.Slider("Ideal share of frame height", idealSize, 0.05f, 0.8f);
        idealElevation = EditorGUILayout.FloatField("Ideal elevation (degrees)", idealElevation);
        probeRadius = EditorGUILayout.FloatField("Probe radius (m)", probeRadius);
        minVisibility = EditorGUILayout.Slider("Minimum visibility", minVisibility, 0.3f, 1f);
        minSeparation = EditorGUILayout.Slider("Minimum difference between winners", minSeparation, 0.1f, 1.2f);
        keep = EditorGUILayout.IntSlider("Winners to keep", keep, 1, 12);

        EditorGUILayout.Space();
        autoSample = EditorGUILayout.Toggle("Auto-sample in Play Mode", autoSample);
        sampleInterval = EditorGUILayout.FloatField("Sample every (seconds)", sampleInterval);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan once")) { ScanOnce(); Repaint(); }
        if (GUILayout.Button("Reset results")) { acc.Clear(); top.Clear(); scans = 0; status = "Results cleared"; }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save CSV + screenshots")) Save();
        if (GUILayout.Button("Create cameras from winners")) CreateCameras();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("Status", status + "   (scans: " + scans + ", candidates: " + acc.Count + ")");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Winners", EditorStyles.boldLabel);
        int rank = 1;
        foreach (Acc a in top)
        {
            float m = a.score / a.n;
            EditorGUILayout.LabelField("#" + rank++ + "  angle " + a.yaw.ToString("F0") + "  dist " + a.dist.ToString("F2") + "  height " + a.height.ToString("F2") +
                "  score " + m.ToString("F2") + "  sees " + (a.vis / a.n).ToString("F2") + "  size " + (100f * a.size / a.n).ToString("F0") + "%");
            EditorGUILayout.LabelField("      offset (" + a.local.x.ToString("F2") + ", " + a.local.y.ToString("F2") + ", " + a.local.z.ToString("F2") + ")");
        }
        EditorGUILayout.EndScrollView();
    }

    void HandPreset()
    {
        mapMode = false;
        distancesText = "0.45, 0.7, 1.0"; heightsText = "0.15, 0.4";
        yawStep = 30f; fov = 60f; targetSize = 0.22f; idealSize = 0.28f; probeRadius = 0.07f; idealElevation = 20f;
        GameObject hand = GameObject.Find("RightHandAnchor"), head = GameObject.Find("CenterEyeAnchor");
        if (hand != null) targetPath = PathOf(hand.transform);
        if (head != null) framePath = PathOf(head.transform);
        status = hand != null ? "Hand preset applied" : "RightHandAnchor not found, set the target by hand";
    }

    void MapPreset()
    {
        mapMode = true;
        distancesText = "2, 3.5, 5"; heightsText = "1, 2, 3.5";
        yawStep = 30f; fov = 60f; idealSize = 0.30f; idealElevation = 25f; framePath = "";
        status = "Map preset applied: select a mission object and press 'Use selected as target'";
    }

    // ---------------------------------------------------------------- scanning
    void Tick()
    {
        if (!autoSample || !EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup < nextSample) return;
        nextSample = EditorApplication.timeSinceStartup + Mathf.Max(0.2f, sampleInterval);
        ScanOnce();
        Repaint();
    }

    void ScanOnce()
    {
        Transform target = Resolve(targetPath), frame = Resolve(framePath);
        if (target == null) { status = "Target not found: " + targetPath; return; }
        float[] dists = ParseList(distancesText), heights = ParseList(heightsText);
        if (dists.Length == 0 || heights.Length == 0) { status = "Distances or heights are empty"; return; }

        Vector3 center = target.position;
        float size = targetSize;
        usedProbe = probeRadius;
        Bounds b;
        if (mapMode && TryBounds(target, out b))
        {
            center = b.center;
            size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            usedProbe = Mathf.Clamp(b.extents.magnitude * 0.5f, 0.1f, 1f);
        }
        lastCenter = center;

        Vector3[] pts =
        {
            center,
            center + Vector3.right * usedProbe, center - Vector3.right * usedProbe,
            center + Vector3.up * usedProbe, center - Vector3.up * usedProbe,
            center + Vector3.forward * usedProbe, center - Vector3.forward * usedProbe
        };
        Quaternion fy = frame != null ? Quaternion.Euler(0f, frame.eulerAngles.y, 0f) : Quaternion.identity;
        int yawCount = Mathf.Max(1, Mathf.RoundToInt(360f / Mathf.Max(5f, yawStep)));

        for (int yi = 0; yi < yawCount; yi++)
            for (int di = 0; di < dists.Length; di++)
                for (int hi = 0; hi < heights.Length; hi++)
                {
                    float yaw = yi * 360f / yawCount;
                    Vector3 local = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, -dists[di]) + Vector3.up * heights[hi];
                    Vector3 pos = center + fy * local;
                    float vis, sizeFrac;
                    float s = Score(pos, center, pts, size, target, out vis, out sizeFrac);

                    Vector3Int key = new Vector3Int(yi, di, hi);
                    Acc a;
                    if (!acc.TryGetValue(key, out a))
                    {
                        a = new Acc { yi = yi, di = di, hi = hi, yaw = yaw, dist = dists[di], height = heights[hi], local = local };
                        acc[key] = a;
                    }
                    a.score += s; a.vis += vis; a.size += sizeFrac; a.n++; a.lastPos = pos;
                }
        scans++;
        RebuildTop();
        SaveLatest();
        status = "Scanned " + scans + " time(s)";
    }

    float Score(Vector3 pos, Vector3 center, Vector3[] pts, float size, Transform target, out float vis, out float sizeFrac)
    {
        vis = 0f; sizeFrac = 0f;
        if (Physics.CheckSphere(pos, clearance, ~0, QueryTriggerInteraction.Ignore)) return 0f;   // inside a wall or prop

        int ok = 0;
        foreach (Vector3 p in pts) if (CanSee(pos, p, target)) ok++;
        vis = ok / (float)pts.Length;

        Vector3 d = center - pos;
        float dist = d.magnitude;
        if (dist < 0.05f) return 0f;
        sizeFrac = size / (2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
        float sizeScore = Mathf.Exp(-Mathf.Pow((sizeFrac - idealSize) / (idealSize * 0.6f), 2f));

        float horiz = new Vector2(d.x, d.z).magnitude;
        float elev = Mathf.Atan2(pos.y - center.y, Mathf.Max(0.01f, horiz)) * Mathf.Rad2Deg;
        float elevScore = Mathf.Exp(-Mathf.Pow((elev - idealElevation) / 25f, 2f));

        if (vis < minVisibility) return 0f;
        return vis * vis * (0.55f * sizeScore + 0.45f * elevScore);
    }

    bool CanSee(Vector3 from, Vector3 to, Transform target)
    {
        RaycastHit hit;
        if (!Physics.Linecast(from, to, out hit, ~0, QueryTriggerInteraction.Ignore)) return true;
        if (Vector3.Distance(hit.point, to) <= usedProbe * 1.5f) return true;   // hit the target's own surface
        return hit.transform.IsChildOf(target);
    }

    void RebuildTop()
    {
        List<Acc> all = new List<Acc>(acc.Values);
        all.Sort((x, y) => (y.score / Mathf.Max(1, y.n)).CompareTo(x.score / Mathf.Max(1, x.n)));
        top.Clear();
        foreach (Acc c in all)
        {
            if (top.Count >= keep) break;
            if (c.score / Mathf.Max(1, c.n) <= 0.01f) break;
            bool different = true;
            foreach (Acc t in top) if (Separation(c, t) < minSeparation) { different = false; break; }
            if (different) top.Add(c);
        }
    }

    static float Separation(Acc a, Acc b)
    {
        float dy = Mathf.Abs(Mathf.DeltaAngle(a.yaw, b.yaw)) / 180f;
        float dd = Mathf.Abs(a.dist - b.dist) / Mathf.Max(a.dist, b.dist);
        float dh = Mathf.Abs(a.height - b.height) / Mathf.Max(0.5f, Mathf.Max(a.height, b.height));
        return dy + dd + dh;
    }

    // ---------------------------------------------------------------- output
    void Save()
    {
        if (acc.Count == 0) { status = "Nothing to save: scan first"; return; }
        Directory.CreateDirectory(Dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Transform target = Resolve(targetPath);
        string name = SafeName(target != null ? target.name : "target");
        CultureInfo ci = CultureInfo.InvariantCulture;

        var sb = new StringBuilder("rank,winner,angle,distance,height,meanScore,meanVisibility,meanSizeFraction,samples,offsetX,offsetY,offsetZ\n");
        List<Acc> all = new List<Acc>(acc.Values);
        all.Sort((x, y) => (y.score / Mathf.Max(1, y.n)).CompareTo(x.score / Mathf.Max(1, x.n)));
        int rank = 1;
        foreach (Acc a in all)
        {
            int n = Mathf.Max(1, a.n);
            sb.Append(rank++).Append(',').Append(top.Contains(a) ? 1 : 0).Append(',')
              .Append(a.yaw.ToString("F0", ci)).Append(',').Append(a.dist.ToString("F2", ci)).Append(',').Append(a.height.ToString("F2", ci)).Append(',')
              .Append((a.score / n).ToString("F3", ci)).Append(',').Append((a.vis / n).ToString("F3", ci)).Append(',').Append((a.size / n).ToString("F3", ci)).Append(',')
              .Append(a.n).Append(',').Append(a.local.x.ToString("F3", ci)).Append(',').Append(a.local.y.ToString("F3", ci)).Append(',').Append(a.local.z.ToString("F3", ci)).Append('\n');
        }
        File.WriteAllText(Path.Combine(Dir, name + "_" + stamp + ".csv"), sb.ToString());

        int k = 1;
        foreach (Acc a in top) Shoot(a, Path.Combine(Dir, name + "_" + stamp + "_winner" + (k++) + ".png"));
        status = "Saved CSV and " + top.Count + " screenshots to " + Dir;
        EditorUtility.RevealInFinder(Dir);
    }

    void Shoot(Acc a, string file)
    {
        const int W = 1280, H = 720;
        GameObject go = new GameObject("scout_tmp") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = go.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        go.transform.position = a.lastPos;
        Vector3 dir = lastCenter - a.lastPos;
        go.transform.rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity;

        RenderTexture rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(file, tex.EncodeToPNG());

        cam.targetTexture = null;
        DestroyImmediate(tex);
        rt.Release();
        DestroyImmediate(rt);
        DestroyImmediate(go);
    }

    void CreateCameras()
    {
        if (EditorApplication.isPlaying) { status = "Stop Play Mode first, or the cameras will vanish. Your results are saved."; return; }
        Transform target = Resolve(targetPath), frame = Resolve(framePath);
        if (target == null || top.Count == 0) { status = "Need a target and winners first"; return; }

        GameObject root = GameObject.Find("SpectatorCameras");
        if (root == null) { root = new GameObject("SpectatorCameras"); Undo.RegisterCreatedObjectUndo(root, "Create SpectatorCameras"); }

        int k = 1;
        foreach (Acc a in top)
        {
            string nm = "SCOUT_" + SafeName(target.name).ToUpperInvariant() + "_" + (k++);
            GameObject go = new GameObject(nm);
            go.transform.SetParent(root.transform, false);
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.stereoTargetEye = StereoTargetEyeMask.None;

            SpectatorFollowCamera f = go.AddComponent<SpectatorFollowCamera>();
            f.mode = mapMode ? SpectatorFollowCamera.Mode.Fixed : SpectatorFollowCamera.Mode.LookAtTarget;
            f.target = target; f.offsetFrame = frame; f.offset = a.local;
            f.positionSmooth = 0.25f; f.rotationSpeed = 6f;

            go.transform.position = a.lastPos;
            Vector3 dir = lastCenter - a.lastPos;
            if (dir.sqrMagnitude > 0.0001f) go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            Undo.RegisterCreatedObjectUndo(go, "Create scout camera");
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        status = "Created " + top.Count + " cameras under SpectatorCameras. Save the scene.";
    }

    // ---------------------------------------------------------------- scene drawing
    void OnScene(SceneView sv)
    {
        if (acc.Count == 0 || Event.current.type != EventType.Repaint) return;
        foreach (Acc a in acc.Values)
        {
            float m = a.score / Mathf.Max(1, a.n);
            Handles.color = Color.Lerp(Color.red, Color.green, Mathf.Clamp01(m * 1.2f));
            Handles.SphereHandleCap(0, a.lastPos, Quaternion.identity, 0.03f, EventType.Repaint);
        }
        int rank = 1;
        foreach (Acc a in top)
        {
            Handles.color = Color.white;
            Handles.SphereHandleCap(0, a.lastPos, Quaternion.identity, 0.09f, EventType.Repaint);
            Handles.DrawLine(a.lastPos, lastCenter);
            Handles.Label(a.lastPos + Vector3.up * 0.06f, "#" + rank++);
        }
    }

    // ---------------------------------------------------------------- persistence (results survive Play Mode)
    void SaveLatest()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            Saved s = new Saved { center = lastCenter, scans = scans };
            s.items.AddRange(acc.Values);
            File.WriteAllText(LatestFile, JsonUtility.ToJson(s));
        }
        catch { }
    }

    void LoadLatest()
    {
        try
        {
            if (!File.Exists(LatestFile)) return;
            Saved s = JsonUtility.FromJson<Saved>(File.ReadAllText(LatestFile));
            if (s == null) return;
            acc.Clear();
            foreach (Acc a in s.items) acc[new Vector3Int(a.yi, a.di, a.hi)] = a;
            lastCenter = s.center; scans = s.scans;
            RebuildTop();
        }
        catch { }
    }

    // ---------------------------------------------------------------- helpers
    static string PathOf(Transform t)
    {
        if (t == null) return "";
        string p = t.name;
        for (Transform x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
        return p;
    }

    static Transform Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        GameObject g = GameObject.Find(path);
        return g != null ? g.transform : null;
    }

    static bool TryBounds(Transform t, out Bounds b)
    {
        b = new Bounds(t.position, Vector3.zero);
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return true;
    }

    static float[] ParseList(string s)
    {
        List<float> list = new List<float>();
        foreach (string part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            float v;
            if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) list.Add(v);
        }
        return list.ToArray();
    }

    static string SafeName(string s) { return Regex.Replace(s, "[^A-Za-z0-9_]", "_"); }
}
#endif
