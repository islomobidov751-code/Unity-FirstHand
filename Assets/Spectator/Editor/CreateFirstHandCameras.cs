#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using NorthStar;

/// <summary>
/// Put this file in Assets/Editor/. Two menu items appear under Tools > Spectator:
///   Create FirstHand Cameras   builds every spectator angle from the First Hand report under one "SpectatorCameras" object
///   Capture All Angles         saves one PNG per angle to <project folder>/AngleCaptures (evidence for the report)
/// Target names (CenterEyeAnchor, LeftHandAnchor, RightHandAnchor) must be confirmed in the real project;
/// a warning in the Console names any that were not found.
/// </summary>
public static class CreateFirstHandCameras
{
    class Spec
    {
        public string name, target, frame;
        public SpectatorFollowCamera.Mode mode;
        public Vector3 offset;
        public float fov, posSmooth, rotSpeed, orbit;
        public bool attach;
    }

    const string Eye = "CenterEyeAnchor", Left = "LeftHandAnchor", Right = "RightHandAnchor", Mid = "HandsMidpoint";
    const SpectatorFollowCamera.Mode Look = SpectatorFollowCamera.Mode.LookAtTarget;
    const SpectatorFollowCamera.Mode Yaw = SpectatorFollowCamera.Mode.FollowTargetYaw;
    const SpectatorFollowCamera.Mode Fix = SpectatorFollowCamera.Mode.Fixed;
    const SpectatorFollowCamera.Mode Orb = SpectatorFollowCamera.Mode.Orbit;
    const SpectatorFollowCamera.Mode Cpy = SpectatorFollowCamera.Mode.CopyTarget;

    static Spec S(string name, SpectatorFollowCamera.Mode mode, string target, string frame,
                  float x, float y, float z, float fov, float pos = 0.25f, float rot = 6f, bool attach = false, float orbit = 8f)
    {
        return new Spec { name = name, mode = mode, target = target, frame = frame, offset = new Vector3(x, y, z),
                          fov = fov, posSmooth = pos, rotSpeed = rot, attach = attach, orbit = orbit };
    }

    // Offsets are starting values (metres, relative to the target, in the head's left/right direction). Tune in Play Mode.
    static Spec[] Specs()
    {
        return new[]
        {
            // The five cameras of the report's rule-based director
            S("PLAYER_STABILIZED",   Yaw,  Eye,   null, 0f,    0f,    0f,    70f, 0.15f, 3f),     // IDLE
            S("OTS_HAND",            Look, Right, Eye,  0.35f, 0.35f, -0.70f, 60f),                // GRAB_ONE_HAND
            S("OTS_CLOSE",           Look, Right, Eye,  0.25f, 0.20f, -0.45f, 50f, 0.2f),          // INTERACT_POKE
            S("CLOSE_BOTH_HANDS",    Look, Mid,   Eye,  0f,    0.35f, -0.80f, 55f),                // GRAB_TWO_HAND
            S("WIDE_FOLLOW",         Yaw,  Eye,   null, 0f,    1.50f, -3.00f, 75f, 0.8f, 2f),      // REESTABLISH (after teleport)
            // Extra views from the report's taxonomy
            S("RAW_FPV",             Cpy,  Eye,   null, 0f,    0f,    0f,    90f),                  // baseline for comparison
            S("CROPPED_FPV",         Cpy,  Eye,   null, 0f,    0f,    0f,    50f),                  // report: risky, comparison only
            S("TORSO_ANCHORED",      Yaw,  Eye,   null, 0f,    0.20f, -0.60f, 70f, 0.3f, 1.5f),    // hands stay framed while head moves
            S("OTS_HAND_ALT",        Look, Right, Eye, -0.50f, 0.40f, -0.70f, 60f),                // occlusion recovery (other side)
            S("POSE_HANDS",          Look, Mid,   Eye,  0f,    0.20f, -1.10f, 65f),                // Palm Blast / Shield pose: both hands visible
            S("ORBIT_TWO_HAND",      Orb,  Mid,   null, 0f,    0.35f, -0.90f, 55f, 0.25f, 6f, false, 8f), // slow orbit around the manipulation
            S("DYNAMIC_TRACKING",    Look, Right, Eye,  0.80f, 0.60f, -1.80f, 65f, 0.5f, 3f),      // distance grab (Kinesis Modules)
            S("WIDE_SCENE_FIXED",    Fix,  Eye,   null, 0f,    1.50f, -3.50f, 75f),                // establishing; move it by hand per scene
        };
    }

    [MenuItem("Tools/Spectator/Create FirstHand Cameras")]
    static void Create()
    {
        if (GameObject.Find("SpectatorCameras") != null)
        {
            Debug.LogWarning("SpectatorCameras already exists in this scene. Delete it first if you want to recreate the cameras.");
            return;
        }

        Transform left = Find(Left), right = Find(Right);
        GameObject root = new GameObject("SpectatorCameras");
        Undo.RegisterCreatedObjectUndo(root, "Create spectator cameras");

        GameObject mid = new GameObject(Mid);
        mid.transform.SetParent(root.transform, false);
        HandsMidpoint hm = mid.AddComponent<HandsMidpoint>();
        hm.leftHand = left; hm.rightHand = right;
        if (left != null && right != null) mid.transform.position = (left.position + right.position) * 0.5f;

        int made = 0;
        foreach (Spec s in Specs())
        {
            Transform target = s.target == Mid ? mid.transform : Find(s.target);
            Transform frame = string.IsNullOrEmpty(s.frame) ? null : Find(s.frame);

            GameObject go = new GameObject(s.name);
            go.transform.SetParent(s.attach && target != null ? target : root.transform, false);

            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = s.fov;
            cam.nearClipPlane = 0.05f;                              // close hand shots must not be clipped
            cam.stereoTargetEye = StereoTargetEyeMask.None;         // spectator camera: never renders to the headset

            SpectatorFollowCamera f = go.AddComponent<SpectatorFollowCamera>();
            f.mode = s.mode; f.target = target; f.offsetFrame = frame; f.offset = s.offset;
            f.positionSmooth = s.posSmooth; f.rotationSpeed = s.rotSpeed; f.orbitDegreesPerSecond = s.orbit;

            if (target == null) Debug.LogWarning("Target '" + s.target + "' not found for " + s.name + ": assign it by hand in the Inspector.");
            else if (!s.attach) Place(go.transform, target, frame, s);
            made++;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("Created " + made + " spectator cameras under 'SpectatorCameras'. Check the Console for any 'not found' warnings.");
    }

    static void Place(Transform t, Transform target, Transform frame, Spec s)
    {
        Transform fr = frame != null ? frame : target;
        Quaternion yaw = Quaternion.Euler(0f, fr.eulerAngles.y, 0f);
        t.position = target.position + yaw * s.offset;
        Vector3 dir = target.position - t.position;
        if (s.mode != Yaw && dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir, Vector3.up);
        else t.rotation = yaw;
    }

    static Transform Find(string name)
    {
        GameObject g = GameObject.Find(name);
        return g != null ? g.transform : null;
    }

    [MenuItem("Tools/Spectator/Capture All Angles")]
    static void CaptureAll()
    {
        GameObject root = GameObject.Find("SpectatorCameras");
        if (root == null) { Debug.LogWarning("No 'SpectatorCameras' object found. Run Create FirstHand Cameras first."); return; }

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AngleCaptures");
        Directory.CreateDirectory(dir);
        const int W = 1280, H = 720;
        int saved = 0;

        foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
        {
            RenderTexture rt = new RenderTexture(W, H, 24);
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = previousActive;

            File.WriteAllBytes(Path.Combine(dir, cam.name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = previousTarget;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            saved++;
        }
        Debug.Log("Saved " + saved + " angle captures to " + dir);
        EditorUtility.RevealInFinder(dir);
    }
}
#endif
