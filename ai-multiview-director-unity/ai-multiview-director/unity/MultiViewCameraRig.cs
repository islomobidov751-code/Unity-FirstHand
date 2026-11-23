// MultiViewCameraRig.cs — renders 2–4 spectator cameras into RenderTextures WITHOUT tanking VR fps.
// -------------------------------------------------------------------------------------------------
// The headset must hold 72–120 fps. Rendering 4 extra full cameras every frame roughly multiplies
// your GPU cost, so this rig:
//   1. renders spectator cams at a lower resolution (720p) and lower rate (30 fps),
//   2. STAGGERS them across frames: at 90 Hz VR with 4 cams @30 fps you render ~1.3 extra cams/frame
//      instead of 4,
//   3. keeps them out of the HMD (stereoTargetEye = None) and lets you strip expensive layers
//      (e.g. first-person hands, UI) via a culling mask.
//
// Each RenderTexture is then streamed by FFmpegStreamer.cs to MediaMTX (rtsp://host:8554/<feed id>).
// Feed ids must be cam1..cam4 so they match the dashboard (dashboard/shared.js → CONFIG.cams).
using System.Collections.Generic;
using UnityEngine;

public class MultiViewCameraRig : MonoBehaviour
{
    [System.Serializable]
    public class Feed
    {
        public string id = "cam1";           // must match the MediaMTX path / dashboard tile
        public Camera camera;
        [HideInInspector] public RenderTexture texture;
    }

    public List<Feed> feeds = new List<Feed>();
    public Vector2Int resolution = new Vector2Int(1280, 720);
    [Range(10, 60)] public int targetFps = 30;
    public LayerMask spectatorCullingMask = ~0;
    [Tooltip("Render the director's 'program' camera EVERY frame (smoothest live feed, costs +1 camera per frame). Others stay staggered.")]
    public bool prioritiseActiveCamera = false;

    public string ActiveCameraId { get; private set; }
    public IReadOnlyList<Feed> Feeds => feeds;

    float _accum;
    int _cursor;

    void Awake()
    {
        foreach (var f in feeds)
        {
            f.texture = new RenderTexture(resolution.x, resolution.y, 24, RenderTextureFormat.ARGB32)
            { name = "RT_" + f.id, antiAliasing = 1 };
            f.texture.Create();
            f.camera.targetTexture = f.texture;
            f.camera.stereoTargetEye = StereoTargetEyeMask.None; // never draw into the headset
            f.camera.cullingMask = spectatorCullingMask;
            f.camera.enabled = false;                             // we decide when it renders
        }
        if (feeds.Count > 0) ActiveCameraId = feeds[0].id;
    }

    public void SetActive(string camId)
    {
        if (feeds.Exists(f => f.id == camId)) ActiveCameraId = camId;
    }

    void LateUpdate()
    {
        foreach (var f in feeds) f.camera.enabled = false;
        if (feeds.Count == 0) return;

        // Budget: how many camera renders are "owed" this frame to hit targetFps per camera.
        _accum += Time.unscaledDeltaTime * targetFps * feeds.Count;
        int renders = Mathf.Min(Mathf.FloorToInt(_accum), feeds.Count);
        _accum -= renders;
        _accum = Mathf.Min(_accum, feeds.Count); // don't build up a backlog after a hitch

        for (int i = 0; i < renders; i++)
        {
            feeds[_cursor].camera.enabled = true; // renders once this frame (works in Built-in, URP and HDRP)
            _cursor = (_cursor + 1) % feeds.Count;
        }

        if (prioritiseActiveCamera)
        {
            var active = feeds.Find(f => f.id == ActiveCameraId);
            if (active != null) active.camera.enabled = true;
        }
    }

    void OnDestroy()
    {
        foreach (var f in feeds) if (f.texture) f.texture.Release();
    }
}
