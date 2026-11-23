// SpectatorCameras.cs — drives the 4 spectator cameras around the VR player (single-player games).
// ------------------------------------------------------------------------------------------------
// cam1 Player view       the headset view, smoothed so viewers don't get motion sick from head shake
// cam2 Hands / On deck   First Hand: close-up of the hands.  North Star: a fixed camera on the rigging (assign a mount)
// cam3 Over the shoulder behind the player, pulled closer when a wall is in the way
// cam4 Wide              a fixed establishing shot (assign a mount), or a slow orbit around the player
// Put it on the same GameObject as MultiViewCameraRig and drag in the 4 cameras.
using UnityEngine;

public class SpectatorCameras : MonoBehaviour
{
    public TelemetryBridge telemetry;
    public Camera playerView, secondCam, shoulderCam, wideCam;

    public enum SecondMode { HandsCloseUp, FixedMount }
    [Header("cam2")]
    public SecondMode secondMode = SecondMode.HandsCloseUp;   // First Hand: HandsCloseUp, North Star: FixedMount
    [Tooltip("For FixedMount: an empty placed where the camera should sit (e.g. up the mast). It keeps looking at the player.")]
    public Transform secondMount;

    [Header("cam4")]
    [Tooltip("An empty placed for the wide shot. Empty = orbit around the player.")]
    public Transform wideMount;
    public float orbitRadius = 6f, orbitHeight = 3f, orbitSpeed = 4f;

    [Header("Smoothing")]
    [Range(1, 20)] public float povSmoothing = 6f;
    [Range(1, 20)] public float followSmoothing = 4f;
    [Tooltip("Walls, rocks, ship parts that may block the over-the-shoulder camera")]
    public LayerMask obstacles = ~0;

    Quaternion _povRot; bool _povInit; Vector3 _v2, _v3;

    void LateUpdate()
    {
        var tb = telemetry ? telemetry : TelemetryBridge.Instance;
        var head = tb && tb.head ? tb.head : (Camera.main ? Camera.main.transform : null);
        if (!head) return;
        float dt = Time.deltaTime;
        var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        var right = Vector3.Cross(Vector3.up, fwd);

        if (playerView)
        {
            if (!_povInit) { _povRot = head.rotation; _povInit = true; }
            _povRot = Quaternion.Slerp(_povRot, head.rotation, 1 - Mathf.Exp(-dt * povSmoothing));
            playerView.transform.SetPositionAndRotation(head.position, _povRot);
        }
        if (secondCam)
        {
            Vector3 target, pos;
            if (secondMode == SecondMode.HandsCloseUp && tb && tb.leftHand && tb.rightHand)
            {
                target = (tb.leftHand.position + tb.rightHand.position) * 0.5f;
                pos = target + right * 0.5f + fwd * 0.55f + Vector3.up * 0.22f;
            }
            else
            {
                target = head.position - Vector3.up * 0.4f;
                pos = secondMount ? secondMount.position : head.position + Vector3.up * 3f - fwd * 4f;
            }
            var t = secondCam.transform;
            t.position = Vector3.SmoothDamp(t.position, pos, ref _v2, 1f / followSmoothing);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(target - t.position), 1 - Mathf.Exp(-dt * followSmoothing * 1.5f));
        }
        if (shoulderCam)
        {
            var pivot = head.position;
            var desired = pivot - fwd * 1.4f + right * 0.45f + Vector3.up * 0.35f;
            if (Physics.Linecast(pivot, desired, out var hit, obstacles, QueryTriggerInteraction.Ignore))
                desired = hit.point + (pivot - hit.point).normalized * 0.25f;
            var t = shoulderCam.transform;
            t.position = Vector3.SmoothDamp(t.position, desired, ref _v3, 1f / followSmoothing);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(pivot + fwd * 1.8f - Vector3.up * 0.45f - t.position), 1 - Mathf.Exp(-dt * followSmoothing));
        }
        if (wideCam)
        {
            var t = wideCam.transform;
            if (wideMount) { t.position = wideMount.position; t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(head.position - Vector3.up * 0.6f - t.position), 1 - Mathf.Exp(-dt)); }
            else
            {
                float a = Time.time * orbitSpeed * Mathf.Deg2Rad;
                t.position = head.position + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * orbitRadius + Vector3.up * orbitHeight;
                t.LookAt(head.position - Vector3.up * 0.6f);
            }
        }
    }
}
