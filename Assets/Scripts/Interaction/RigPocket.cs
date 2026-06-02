using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Self-contained pocket / holster that can be attached anywhere on the
    /// character rig (hip, chest, belt, etc.). Any object carrying a Meta ISDK
    /// <see cref="Grabbable"/> can be stored: release it inside this pocket's
    /// trigger volume and it snaps into the anchor; grab it again and it pops
    /// back out to its original parent.
    ///
    /// The component does NOT depend on the project's inventory scripts
    /// (InventoryItemMetaBridge / BackpackInventoryZone), so it works with any
    /// grabbable object. All per-item state (parent, scale, rigidbody flags) is
    /// cached inside this pocket for its single stored item, so items require no
    /// extra component.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class RigPocket : MonoBehaviour
    {
        [Header("Snap Target")]
        [Tooltip("Where a stored item snaps to. If empty, this transform is used.")]
        [SerializeField] private Transform holsterAnchor;

        [Header("Stored Appearance")]
        [Tooltip("Multiplier applied to the stored item's original world size (1 = keep size, 0.2 = shrink to 20%).")]
        [SerializeField] private float storedScale = 1f;

        [Tooltip("Freeze the stored item's rigidbody (kinematic + gravity off) so it stays put inside the pocket.")]
        [SerializeField] private bool freezeRigidbody = true;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip itemInClip;
        [SerializeField] private AudioClip itemOutClip;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        private readonly HashSet<Grabbable> _grabbablesInside = new HashSet<Grabbable>();

        private Grabbable _storedGrabbable;
        private Rigidbody _storedRigidbody;
        private Transform _storedOriginalParent;
        private Vector3 _storedOriginalLocalScale;
        private bool _storedOriginalUseGravity;
        private bool _storedOriginalIsKinematic;

        private Transform ActiveAnchor => holsterAnchor != null ? holsterAnchor : transform;

        public bool HasItem => _storedGrabbable != null;

        private void Awake()
        {
            ForceTriggerCollider();
        }

        private void OnValidate()
        {
            if (storedScale <= 0f)
            {
                storedScale = 1f;
            }
        }

        private void OnDisable()
        {
            foreach (Grabbable grabbable in _grabbablesInside)
            {
                if (grabbable != null)
                {
                    grabbable.WhenPointerEventRaised -= HandlePointerEvent;
                }
            }
            _grabbablesInside.Clear();
        }

        private void ForceTriggerCollider()
        {
            Collider pocketCollider = GetComponent<Collider>();
            if (pocketCollider == null)
            {
                Debug.LogError($"[{nameof(RigPocket)}] Missing collider on {name}.", this);
                return;
            }

            if (!pocketCollider.isTrigger)
            {
                pocketCollider.isTrigger = true;
                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(RigPocket)}] Forced collider to trigger on {name}.", this);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Grabbable grabbable = other.GetComponentInParent<Grabbable>();
            if (grabbable == null || _grabbablesInside.Contains(grabbable))
            {
                return;
            }

            _grabbablesInside.Add(grabbable);
            grabbable.WhenPointerEventRaised += HandlePointerEvent;

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Grabbable entered pocket volume: {grabbable.name}", grabbable);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            Grabbable grabbable = other.GetComponentInParent<Grabbable>();
            if (grabbable == null || !_grabbablesInside.Contains(grabbable))
            {
                return;
            }

            // Keep tracking the currently stored item even if its collider
            // technically leaves the trigger after re-anchoring; we still need
            // its Select event to release it.
            if (grabbable == _storedGrabbable)
            {
                return;
            }

            _grabbablesInside.Remove(grabbable);
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Grabbable exited pocket volume: {grabbable.name}", grabbable);
            }
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            // We do not know which grabbable fired the event from the payload
            // alone, so resolve via the active hover/select set. The simplest
            // robust approach: react on Select/Unselect and reconcile against
            // our tracked set.
            switch (pointerEvent.Type)
            {
                case PointerEventType.Unselect:
                    TryStoreReleasedItem();
                    break;

                case PointerEventType.Select:
                    TryReleaseStoredItem();
                    break;
            }
        }

        private void TryStoreReleasedItem()
        {
            if (HasItem)
            {
                return;
            }

            // Store the first tracked grabbable that is no longer being held.
            foreach (Grabbable grabbable in _grabbablesInside)
            {
                if (grabbable == null)
                {
                    continue;
                }

                if (grabbable.SelectingPointsCount > 0)
                {
                    continue;
                }

                StoreItem(grabbable);
                return;
            }
        }

        private void TryReleaseStoredItem()
        {
            if (!HasItem)
            {
                return;
            }

            if (_storedGrabbable.SelectingPointsCount > 0)
            {
                ReleaseItem();
            }
        }

        private void StoreItem(Grabbable grabbable)
        {
            _storedGrabbable = grabbable;
            _storedRigidbody = grabbable.GetComponent<Rigidbody>();
            if (_storedRigidbody == null)
            {
                _storedRigidbody = grabbable.GetComponentInParent<Rigidbody>();
            }

            Transform itemTransform = grabbable.transform;
            _storedOriginalParent = itemTransform.parent;
            _storedOriginalLocalScale = itemTransform.localScale;

            Transform anchor = ActiveAnchor;

            // Compensate for differences in parent lossy scale so the stored
            // item ends up at originalWorldSize * storedScale regardless of how
            // the anchor itself is scaled.
            Vector3 oldLossy = _storedOriginalParent != null ? _storedOriginalParent.lossyScale : Vector3.one;
            Vector3 newLossy = anchor != null ? anchor.lossyScale : Vector3.one;
            Vector3 ratio = new Vector3(
                SafeDiv(oldLossy.x, newLossy.x),
                SafeDiv(oldLossy.y, newLossy.y),
                SafeDiv(oldLossy.z, newLossy.z));

            itemTransform.SetParent(anchor, true);
            itemTransform.SetPositionAndRotation(anchor.position, anchor.rotation);
            itemTransform.localScale = Vector3.Scale(_storedOriginalLocalScale, ratio) * storedScale;

            if (_storedRigidbody != null)
            {
                _storedOriginalUseGravity = _storedRigidbody.useGravity;
                _storedOriginalIsKinematic = _storedRigidbody.isKinematic;

                if (freezeRigidbody)
                {
                    _storedRigidbody.linearVelocity = Vector3.zero;
                    _storedRigidbody.angularVelocity = Vector3.zero;
                    _storedRigidbody.useGravity = false;
                    _storedRigidbody.isKinematic = true;
                }
            }

            PlayOneShot(itemInClip);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Stored {grabbable.name} (scale x{storedScale:F2}).", grabbable);
            }
        }

        private void ReleaseItem()
        {
            Grabbable released = _storedGrabbable;
            Transform itemTransform = released.transform;

            itemTransform.SetParent(_storedOriginalParent, true);
            itemTransform.localScale = _storedOriginalLocalScale;

            if (_storedRigidbody != null && freezeRigidbody)
            {
                _storedRigidbody.useGravity = _storedOriginalUseGravity;
                _storedRigidbody.isKinematic = _storedOriginalIsKinematic;
            }

            PlayOneShot(itemOutClip);

            _storedGrabbable = null;
            _storedRigidbody = null;
            _storedOriginalParent = null;

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Released {released.name} from pocket.", released);
            }
        }

        private static float SafeDiv(float a, float b)
        {
            return Mathf.Abs(b) > Mathf.Epsilon ? a / b : 1f;
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (audioSource == null || clip == null)
            {
                return;
            }

            audioSource.PlayOneShot(clip);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Collider c = GetComponent<Collider>();
            if (c is BoxCollider box)
            {
                Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.2f);
                Matrix4x4 prev = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = new Color(0.4f, 1f, 0.6f, 1f);
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = prev;
            }

            Transform anchor = ActiveAnchor;
            if (anchor != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(anchor.position, 0.03f);
            }
        }
#endif
    }
}
