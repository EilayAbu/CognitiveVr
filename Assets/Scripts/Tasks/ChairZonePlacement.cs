using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the chair root. Tracks whether the chair is currently inside the
    /// placement zone trigger. When the chair is released from the hand while inside
    /// the zone (call <see cref="OnChairReleased"/> from the chair's
    /// InteractableUnityEventWrapper "When Unselect" event), the chair's collider is
    /// disabled and the hidden child object (with its second collider) is enabled.
    /// The zone cube must have a trigger collider and be tagged with <see cref="zoneTag"/>.
    /// </summary>
    public class ChairZonePlacement : MonoBehaviour
    {
        [Tooltip("Tag on the zone cube used to identify it during trigger callbacks.")]
        [SerializeField] private string zoneTag = "ChairZone";

        [Tooltip("The chair collider to disable once the chair is placed in the zone.")]
        [SerializeField] private Collider chairCollider;

        [Tooltip("The hidden child object (with the second collider) to enable once placed.")]
        [SerializeField] private GameObject secondaryObject;

        [SerializeField] private bool enableDebugLogs = true;

        private bool _isInZone;
        private bool _placed;

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.CompareTag("Untagged")) return;
            if ( other.CompareTag(zoneTag))
            {
                _isInZone = true;
                if (enableDebugLogs)
                    Debug.Log($"[ChairZonePlacement] Chair entered zone: {other.name}", this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.CompareTag("Untagged")) return;
            if ( other.CompareTag(zoneTag))
            {
                _isInZone = false;
                if (enableDebugLogs)
                    Debug.Log($"[ChairZonePlacement] Chair exited zone: {other.name}", this);
            }
        }

        /// <summary>
        /// Wire this to the chair's InteractableUnityEventWrapper "When Unselect" event.
        /// If the chair is inside the zone, disables the chair collider and enables the
        /// secondary object. Placement only happens once.
        /// </summary>
        public void OnChairReleased()
        {
            if (_placed)
                return;

            if (!_isInZone)
            {
                if (enableDebugLogs)
                    Debug.Log("[ChairZonePlacement] Chair released outside zone, ignoring.", this);
                return;
            }

            _placed = true;

            if (chairCollider != null)
                chairCollider.enabled = false;
            else if (enableDebugLogs)
                Debug.LogWarning("[ChairZonePlacement] Chair Collider is not assigned.", this);

            if (secondaryObject != null)
                secondaryObject.SetActive(true);
            else if (enableDebugLogs)
                Debug.LogWarning("[ChairZonePlacement] Secondary Object is not assigned.", this);

            if (enableDebugLogs)
                Debug.Log("[ChairZonePlacement] Chair placed in zone.", this);
        }
    }
}
