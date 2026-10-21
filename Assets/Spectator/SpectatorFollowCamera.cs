using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Put on a spectator Camera (v2). Modes:
    ///  - LookAtTarget:    sits at an offset from the target and looks at it (over-the-shoulder, close, trailing)
    ///  - FollowTargetYaw: copies only the target's left/right direction (stabilized or horizon-stable first-person)
    ///  - Orbit:           slowly circles the target (keep Orbit Degrees Per Second low for viewer comfort)
    ///  - CopyTarget:      copies the target's exact position and rotation (raw first-person camera, kept under SpectatorCameras)
    ///  - Fixed:           does not move (wide establishing, or a raw first-person camera parented to the head)
    /// Offset is expressed in the Offset Frame's orientation (default: the target), relative to the target's position.
    /// Yaw Only Frame (default ON) ignores the frame's tilt, so a tilting head or boat doesn't swing the camera.
    /// </summary>
    public class SpectatorFollowCamera : MonoBehaviour
    {
        public enum Mode { LookAtTarget, FollowTargetYaw, Fixed, Orbit, CopyTarget }

        public Mode mode = Mode.LookAtTarget;
        public Transform target;
        [Tooltip("Optional. Orientation used for the offset (e.g. the head or the Boat).")]
        public Transform offsetFrame;
        public bool yawOnlyFrame = true;
        public Vector3 offset = new Vector3(0f, 1f, -3f);
        public float positionSmooth = 0.25f;
        public float rotationSpeed = 6f;
        public float orbitDegreesPerSecond = 8f;

        Vector3 velocity;
        float orbitAngle;

        void LateUpdate()
        {
            if (mode == Mode.Fixed || target == null) return;
            if (mode == Mode.CopyTarget) { transform.SetPositionAndRotation(target.TransformPoint(offset), target.rotation); return; }

            Transform fr = offsetFrame != null ? offsetFrame : target;
            Quaternion frame = yawOnlyFrame ? Quaternion.Euler(0f, fr.eulerAngles.y, 0f) : fr.rotation;
            Quaternion targetYaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);

            Vector3 wanted;
            if (mode == Mode.FollowTargetYaw)
                wanted = target.position + targetYaw * offset;
            else if (mode == Mode.Orbit)
            {
                orbitAngle += orbitDegreesPerSecond * Time.deltaTime;
                wanted = target.position + Quaternion.Euler(0f, orbitAngle, 0f) * offset;
            }
            else
                wanted = target.position + frame * offset;

            transform.position = Vector3.SmoothDamp(transform.position, wanted, ref velocity, positionSmooth);

            Quaternion wantedRot = targetYaw;
            if (mode != Mode.FollowTargetYaw)
            {
                Vector3 dir = target.position - transform.position;
                if (dir.sqrMagnitude < 0.0001f) return;
                wantedRot = Quaternion.LookRotation(dir, Vector3.up);
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, wantedRot, rotationSpeed * Time.deltaTime);
        }
    }
}
