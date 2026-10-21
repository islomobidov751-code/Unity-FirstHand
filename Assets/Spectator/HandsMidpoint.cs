using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// Keeps this object at the midpoint between the two hands, so cameras can frame "both hands and the object"
    /// (two-hand grab, hand poses). Created automatically by Tools > Spectator > Create FirstHand Cameras.
    /// </summary>
    public class HandsMidpoint : MonoBehaviour
    {
        public Transform leftHand, rightHand;

        void LateUpdate()
        {
            if (leftHand == null || rightHand == null) return;
            transform.position = (leftHand.position + rightHand.position) * 0.5f;
        }
    }
}
