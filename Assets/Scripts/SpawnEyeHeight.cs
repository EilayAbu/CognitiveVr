using System.Collections;
using UnityEngine;

/// <summary>
/// Forces the player's eyes to a fixed height at spawn, regardless of
/// real-world height or sitting/standing. Works with Floor Level or
/// Eye Level tracking origin. Attach to the [BuildingBlock] Camera Rig.
/// </summary>
public class SpawnEyeHeight : MonoBehaviour
{
    [SerializeField] private OVRCameraRig rig;

    [Tooltip("Desired eye height above the rig root, in meters")]
    [SerializeField] private float eyeHeight = 1.4f;

    private void OnEnable()
    {
        OVRManager.HMDMounted += OnHmdEvent;               // headset put back on
        if (OVRManager.display != null)
            OVRManager.display.RecenteredPose += OnHmdEvent; // user long-pressed Meta button
        StartCoroutine(Calibrate());
    }

    private void OnDisable()
    {
        OVRManager.HMDMounted -= OnHmdEvent;
        if (OVRManager.display != null)
            OVRManager.display.RecenteredPose -= OnHmdEvent;
    }

    private void OnHmdEvent() => StartCoroutine(Calibrate());

    private IEnumerator Calibrate()
    {
        // let tracking deliver a real head pose first
        yield return null;
        yield return null;

        Transform ts = rig.trackingSpace;
        float y = eyeHeight - rig.centerEyeAnchor.localPosition.y;
        ts.localPosition = new Vector3(ts.localPosition.x, y, ts.localPosition.z);
    }
}
