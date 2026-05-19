using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Per-slot grab zone inside the backpack. When the hand performs a grab
    /// gesture on this slot's collider (via Meta HandGrabInteractable), the
    /// stored item bound to this slot is released back to the world.
    ///
    /// The component is intentionally lightweight: it only requires a
    /// <see cref="Grabbable"/> on the same GameObject for the hand-grab path
    /// to fire. The full Meta stack (HandGrabInteractable / RayInteractable /
    /// kinematic Rigidbody) is added by <c>PhoneSetupEditor</c>; if those are
    /// missing at runtime the slot still acts as a passive position marker.
    /// </summary>
    [DisallowMultipleComponent]
    public class BackpackSlot : MonoBehaviour
    {
        [SerializeField] private BackpackInventoryZone zone;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private BoxCollider boxCollider;
        [SerializeField] private Vector3 colliderSize = Vector3.one;

        private InventoryItemMetaBridge _storedItem;
        private bool _warnedMissingGrabbable;
        private bool _isHovering;

        public bool HasItem => _storedItem != null;
        public InventoryItemMetaBridge StoredItem => _storedItem;

        private void Awake()
        {
            EnsureBoxCollider();
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }
        }

        private void OnEnable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
        }

        private void OnDisable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }

            if (_isHovering && zone != null)
            {
                zone.NotifyHoverExit(this);
            }
            _isHovering = false;
        }

        public void Bind(BackpackInventoryZone owningZone, Vector3 desiredColliderSize)
        {
            zone = owningZone;
            colliderSize = desiredColliderSize;
            EnsureBoxCollider();
        }

        public void SetStoredItem(InventoryItemMetaBridge item)
        {
            _storedItem = item;
        }

        public void ClearStoredItem()
        {
            _storedItem = null;
        }

        private void EnsureBoxCollider()
        {
            if (boxCollider == null)
            {
                boxCollider = GetComponent<BoxCollider>();
            }
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }

            // Non-trigger so Meta HandGrabInteractable can detect the grab
            // gesture against a solid surface. The parent volume's collider
            // remains the trigger for OnTriggerEnter-based item storage.
            boxCollider.isTrigger = false;
            boxCollider.size = colliderSize;
            boxCollider.center = Vector3.zero;
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Hover:
                    if (!_isHovering)
                    {
                        _isHovering = true;
                        if (zone != null)
                        {
                            zone.NotifyHoverEnter(this);
                        }
                    }
                    break;

                case PointerEventType.Unhover:
                    if (_isHovering)
                    {
                        _isHovering = false;
                        if (zone != null)
                        {
                            zone.NotifyHoverExit(this);
                        }
                    }
                    break;

                case PointerEventType.Select:
                    if (_storedItem != null && zone != null)
                    {
                        zone.ReleaseItemFromSlot(this);
                    }
                    break;
            }
        }

        private void Reset()
        {
            grabbable = GetComponent<Grabbable>();
            boxCollider = GetComponent<BoxCollider>();
            EnsureBoxCollider();
        }

        private void OnValidate()
        {
            if (colliderSize.x < 0.05f || colliderSize.y < 0.05f || colliderSize.z < 0.05f)
            {
                colliderSize = Vector3.one;
            }
        }

        private void Start()
        {
            // Defer the warning to Start so the editor setup pass has a chance
            // to add the Grabbable before we complain.
            if (grabbable == null && !_warnedMissingGrabbable)
            {
                _warnedMissingGrabbable = true;
                Debug.LogWarning(
                    $"[{nameof(BackpackSlot)}] No Oculus.Interaction.Grabbable on slot '{name}'. " +
                    "Per-slot hand-grab release is disabled until you run 'CognitiveVR > Setup Smartphone In Scene'.",
                    this);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = HasItem ? new Color(0.3f, 1f, 0.3f, 0.9f) : new Color(1f, 0.85f, 0.2f, 0.6f);
            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, colliderSize);
            Gizmos.matrix = prev;
        }
#endif
    }
}
