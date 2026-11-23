using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Moves one fly camera for one viewer (W A S D, Q / E, Shift, mouse). Created by FlyCameraPool, so you never add it by hand.
    /// It slides along walls and cannot go farther than Max Distance from the player.
    /// </summary>
    public class FreeFlyCamera : MonoBehaviour
    {
        [HideInInspector] public FlySlot slot;
        public Transform anchor;
        public float speed = 4f;
        public float fastMultiplier = 3f;
        public float smoothing = 0.12f;
        public float radius = 0.25f;           // camera size for wall checks
        public float maxDistance = 40f;        // meters from the player
        public LayerMask blockers = ~0;

        Vector3 vel, velSmooth;
        bool wasActive;

        void ResetToPlayer()
        {
            if (anchor != null) transform.position = anchor.position + new Vector3(0f, 0.5f, -3f);
            vel = Vector3.zero; velSmooth = Vector3.zero;
        }

        void LateUpdate()
        {
            if (slot == null || slot.owner == null) { wasActive = false; return; }
            if (slot.resetRequested || !wasActive) { slot.resetRequested = false; ResetToPlayer(); slot.yaw = 0f; slot.pitch = 10f; wasActive = true; }

            transform.rotation = Quaternion.Euler(Mathf.Clamp(slot.pitch, -85f, 85f), slot.yaw, 0f);

            Vector3 input = new Vector3(slot.moveX, slot.moveY, slot.moveZ);
            if (input.sqrMagnitude > 1f) input.Normalize();
            Vector3 want = (transform.right * input.x + Vector3.up * input.y + transform.forward * input.z)
                           * speed * (slot.fast ? fastMultiplier : 1f);
            vel = Vector3.SmoothDamp(vel, want, ref velSmooth, smoothing);

            Vector3 delta = vel * Time.deltaTime;
            float d = delta.magnitude;
            if (d > 1e-5f)
            {
                RaycastHit hit;
                if (Physics.SphereCast(transform.position, radius, delta / d, out hit, d + 0.02f, blockers, QueryTriggerInteraction.Ignore))
                {
                    delta = Vector3.ProjectOnPlane(delta, hit.normal);   // slide along the wall
                    float d2 = delta.magnitude;
                    if (d2 < 1e-5f || Physics.SphereCast(transform.position, radius, delta / d2, out hit, d2 + 0.02f, blockers, QueryTriggerInteraction.Ignore))
                        delta = Vector3.zero;
                }
                transform.position += delta;
            }

            if (anchor != null)
            {
                Vector3 off = transform.position - anchor.position;
                if (off.magnitude > maxDistance) transform.position = anchor.position + off.normalized * maxDistance;
            }
        }
    }
}
