using UnityEngine;

public class FirstHandCameraDirector : MonoBehaviour
{
    public enum CameraState { IDLE, OTS_HAND }

    [Header("Cinemachine Virtual Cameras (GameObjects)")]
    public GameObject vCamPlayerStabilized;
    public GameObject vCamOtsHand;

    [Header("Gating Rules")]
    public float minShotSeconds = 2.0f;

    private CameraState currentState = CameraState.IDLE;
    private float stateTimer = 0f;

    void Start()
    {
        ChangeState(CameraState.IDLE);
    }

    void Update()
    {
        stateTimer += Time.deltaTime;

        // Hotkey transitions for testing
        if (stateTimer >= minShotSeconds)
        {
            if (Input.GetKeyDown(KeyCode.G)) ChangeState(CameraState.OTS_HAND);
            if (Input.GetKeyDown(KeyCode.R)) ChangeState(CameraState.IDLE);
        }
    }

    public void ChangeState(CameraState newState)
    {
        if (stateTimer < minShotSeconds && currentState != CameraState.IDLE) return;

        currentState = newState;
        stateTimer = 0f;

        // Set Priorities (High value 20 means active view, low value 10 means inactive)
        if (currentState == CameraState.IDLE)
        {
            SetPriorityValue(vCamPlayerStabilized, 20);
            SetPriorityValue(vCamOtsHand, 10);
        }
        else if (currentState == CameraState.OTS_HAND)
        {
            SetPriorityValue(vCamPlayerStabilized, 10);
            SetPriorityValue(vCamOtsHand, 20);
        }
        Debug.Log("Cinemachine blending smoothly to state: " + currentState);
    }

    private void SetPriorityValue(GameObject vCamObj, int val)
    {
        if (vCamObj != null)
        {
            var vCamComponent = vCamObj.GetComponent("CinemachineVirtualCamera");
            if (vCamComponent != null)
            {
                var priorityProp = vCamComponent.GetType().GetProperty("Priority");
                if (priorityProp != null)
                {
                    priorityProp.SetValue(vCamComponent, val, null);
                }
            }
        }
    }
}

