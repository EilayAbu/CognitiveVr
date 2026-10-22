using System.Collections;
using CognitiveVR.Core;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.XR;

namespace CognitiveVR.Tasks
{
    /// <summary>Calibrates the shelf once, before the participant starts solving the task.</summary>
    [DisallowMultipleComponent]
    public class KeyTaskHeight : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig rig;
        [SerializeField] private Transform shelf;
        [SerializeField] private KeyKnockZone keyZone;
        [SerializeField] private KeyReachZone reachZone;
        [Tooltip("Shelf surface above the participant's initial eye position, in metres.")]
        [SerializeField, Min(0f)] private float heightAboveEyes = 0.55f;
        [Tooltip("Distance from the shelf pivot to its top surface, in world metres.")]
        [SerializeField, Min(0f)] private float shelfTopOffset = 0.04f;
        [Tooltip("World-space shelf surface limits, keeping it clear of furniture and the ceiling.")]
        [SerializeField] private Vector2 shelfSurfaceLimits = new Vector2(1.9f, 2.9f);
        [SerializeField, Min(0.1f)] private float stableSeconds = 0.75f;
        [SerializeField, Min(0.001f)] private float heightTolerance = 0.03f;

        public bool IsCalibrated { get; private set; }

        private IEnumerator Start()
        {
            if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null || shelf == null || keyZone == null || keyZone.key == null)
            {
                Debug.LogWarning("[KeyTaskHeight] Missing rig, shelf or key; keeping the authored height.", this);
                yield break;
            }

            Transform key = keyZone.key;
            var grabbable = key.GetComponent<Grabbable>();
            Vector3 initialKeyPosition = key.position;
            float referenceHeight = 0f;
            float stableTime = 0f;

            while (!IsCalibrated)
            {
                yield return null;
                // A late headset connection must never move a key already grabbed or knocked down.
                if (keyZone == null || keyZone.IsConsumed || key == null ||
                    (grabbable != null && grabbable.SelectingPointsCount > 0) ||
                    Vector3.Distance(key.position, initialKeyPosition) > 0.15f)
                    yield break;

                var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                if (!head.isValid || !head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                    (head.TryGetFeatureValue(CommonUsages.userPresence, out bool present) && !present) ||
                    rig.centerEyeAnchor == null || rig.trackingSpace == null)
                {
                    stableTime = 0f;
                    continue;
                }

                // Measure relative to the rig so locomotion cannot be mistaken for user height.
                float eyeHeight = rig.transform.InverseTransformPoint(rig.centerEyeAnchor.position).y;
                if (eyeHeight < 0.4f || eyeHeight > 2.5f || float.IsNaN(eyeHeight))
                {
                    stableTime = 0f;
                    continue;
                }

                if (stableTime == 0f || Mathf.Abs(eyeHeight - referenceHeight) > heightTolerance)
                {
                    referenceHeight = eyeHeight;
                    stableTime = 0f;
                }
                stableTime += Time.unscaledDeltaTime;
                if (stableTime >= stableSeconds) Calibrate(rig.centerEyeAnchor.position.y);
            }
        }

        private void Calibrate(float eyeWorldY)
        {
            float surfaceY = Mathf.Clamp(eyeWorldY + heightAboveEyes,
                shelfSurfaceLimits.x, shelfSurfaceLimits.y);
            float deltaY = surfaceY - (shelf.position.y + shelfTopOffset);
            Vector3 delta = Vector3.up * deltaY;
            // These are independent scene transforms. The stool and landing point stay on the floor/table.
            shelf.position += delta;
            keyZone.transform.position += delta;
            if (reachZone != null) reachZone.transform.position += delta;
            Transform key = keyZone.key;
            key.position += delta;
            var body = key.GetComponent<Rigidbody>();
            if (body != null) body.position = key.position;
            Physics.SyncTransforms();
            key.GetComponent<RespawnableObject>()?.CaptureHome();
            IsCalibrated = true;
        }

        private void OnDisable() => StopAllCoroutines();
    }
}
