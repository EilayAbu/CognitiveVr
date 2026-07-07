using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the child trigger collider inside the Toaster.
    /// Forwards toast enter/exit events to the parent ToasterController.
    /// The toast GameObject must be tagged "toast".
    ///
    /// When the toast is released (Meta ISDK Unselect) while inside this zone,
    /// it is snapped to the anchor pose and frozen (kinematic, zero velocity)
    /// so it does not jitter or drift out of place. Grabbing it again unfreezes
    /// it so the player can pull it back out.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ToasterTriggerZone : MonoBehaviour
    {
        [Tooltip("Pose the toast snaps to when released inside the zone. Defaults to this object's transform when left empty.")]
        [SerializeField] private Transform _snapAnchor;
        [SerializeField] private bool enableDebugLogs = true;

        private ToasterController _controller;

        private GameObject _toast;
        private Grabbable _grabbable;
        private Rigidbody _toastRigidbody;

        private bool _cachedRigidbodyState;
        private bool _originalUseGravity;
        private bool _originalIsKinematic;
        private bool _isFrozen;

        private void Awake()
        {
            _controller = GetComponentInParent<ToasterController>();
            if (enableDebugLogs && _controller == null)
                Debug.LogWarning($"[ToasterTriggerZone] No ToasterController found in parents for {name}.", this);

            if (_snapAnchor == null)
                _snapAnchor = transform;
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("toast"))
                return;

            if (enableDebugLogs)
                Debug.Log($"[ToasterTriggerZone] Toast entered trigger: {other.name}", other);

            TrackToast(other);
            _controller?.NotifyToastEntered(_toast != null ? _toast : other.gameObject);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("toast"))
                return;

            if (enableDebugLogs)
                Debug.Log($"[ToasterTriggerZone] Toast exited trigger: {other.name}", other);

            Unsubscribe();
            _controller?.NotifyToastExited();
        }

        private void TrackToast(Collider other)
        {
            Grabbable grabbable = other.GetComponentInParent<Grabbable>();
            GameObject toastRoot = grabbable != null ? grabbable.gameObject : other.attachedRigidbody != null
                ? other.attachedRigidbody.gameObject
                : other.gameObject;

            if (_toast == toastRoot && _grabbable == grabbable)
                return;

            Unsubscribe();

            _toast = toastRoot;
            _grabbable = grabbable;
            _toastRigidbody = other.GetComponentInParent<Rigidbody>();
            _cachedRigidbodyState = false;
            _isFrozen = false;

            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
            else if (enableDebugLogs)
            {
                Debug.LogWarning($"[ToasterTriggerZone] Toast '{other.name}' has no Oculus.Interaction.Grabbable; snap-on-release disabled.", other);
            }
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Unselect:
                    SnapToastIntoPlace();
                    break;

                case PointerEventType.Select:
                    UnfreezeToast();
                    break;
            }
        }

        /// <summary>
        /// Snap the tracked toast to the anchor pose and freeze it in place.
        /// Also usable from a UnityEvent (e.g. a Grabbable pointer event or
        /// InteractableUnityEventWrapper.WhenUnselect) if wired in the Inspector.
        /// </summary>
        public void SnapToastIntoPlace()
        {
            if (_toast == null || _snapAnchor == null)
                return;

            _toast.transform.SetPositionAndRotation(_snapAnchor.position, _snapAnchor.rotation);

            if (_toastRigidbody != null)
            {
                CacheRigidbodyStateIfNeeded();
                _toastRigidbody.linearVelocity = Vector3.zero;
                _toastRigidbody.angularVelocity = Vector3.zero;
                _toastRigidbody.useGravity = false;
                _toastRigidbody.isKinematic = true;
            }

            _isFrozen = true;

            if (enableDebugLogs)
                Debug.Log($"[ToasterTriggerZone] Snapped toast '{_toast.name}' into place.", _toast);
        }

        private void UnfreezeToast()
        {
            if (!_isFrozen || _toastRigidbody == null)
                return;

            if (_cachedRigidbodyState)
            {
                _toastRigidbody.isKinematic = _originalIsKinematic;
                _toastRigidbody.useGravity = _originalUseGravity;
            }

            _isFrozen = false;

            if (enableDebugLogs)
                Debug.Log($"[ToasterTriggerZone] Unfroze toast '{_toast?.name}'.", _toast);
        }

        private void CacheRigidbodyStateIfNeeded()
        {
            if (_cachedRigidbodyState || _toastRigidbody == null)
                return;

            _originalUseGravity = _toastRigidbody.useGravity;
            _originalIsKinematic = _toastRigidbody.isKinematic;
            _cachedRigidbodyState = true;
        }

        private void Unsubscribe()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised -= HandlePointerEvent;

            _grabbable = null;
            _toast = null;
            _toastRigidbody = null;
            _cachedRigidbodyState = false;
            _isFrozen = false;
        }
    }
}
