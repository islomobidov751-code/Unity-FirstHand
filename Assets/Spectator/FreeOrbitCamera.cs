using UnityEngine;

namespace NorthStar
{
    /// <summary>Values the web page sets (through StreamServer's /free address). Shared by all viewers: the last one wins.</summary>
    public static class FreeCamControl
    {
        public static volatile float yaw = 0f;      // degrees around the player
        public static volatile float pitch = 20f;   // degrees above the horizon
        public static volatile float dist = 3f;     // meters from the head
    }

    /// <summary>Orbit camera for viewers. Put it on a camera object named FREE_ORBIT (under SpectatorCameras).</summary>
    public class FreeOrbitCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Used when Target is empty. Check the real name in the Hierarchy")]
        public string targetName = "CenterEyeAnchor";
        public float minDist = 1f, maxDist = 8f, minPitch = -10f, maxPitch = 80f;
        public float smooth = 0.15f;
        public LayerMask blockers = ~0;

        float yaw, pitch, dist, vy, vp, vd;

        void Start()
        {
            if (target == null)
            {
                var go = GameObject.Find(targetName);
                if (go != null) target = go.transform;
                else Debug.LogWarning("FreeOrbitCamera: no object named " + targetName + ". Drag the player's head into Target.");
            }
            yaw = FreeCamControl.yaw; pitch = FreeCamControl.pitch; dist = FreeCamControl.dist;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float ty = FreeCamControl.yaw;
            float tp = Mathf.Clamp(FreeCamControl.pitch, minPitch, maxPitch);
            float td = Mathf.Clamp(FreeCamControl.dist, minDist, maxDist);
            yaw = Mathf.SmoothDampAngle(yaw, ty, ref vy, smooth);
            pitch = Mathf.SmoothDamp(pitch, tp, ref vp, smooth);
            dist = Mathf.SmoothDamp(dist, td, ref vd, smooth);

            Vector3 focus = target.position;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus - rot * Vector3.forward * dist;
            RaycastHit hit;
            if (Physics.Linecast(focus, pos, out hit, blockers, QueryTriggerInteraction.Ignore)) pos = hit.point + hit.normal * 0.1f;   // stay inside the room
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(focus - pos, Vector3.up));
        }
    }
}
