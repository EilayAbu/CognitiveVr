using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the spatula tip (trigger collider). While the spatula is held
    /// (Meta ISDK Grabbable Select), toast that touches this tip is parented to
    /// the attach point so it can be lifted out of / into the toaster and lunch
    /// box. Hand grab on the toast stays available when the spatula is not
    /// carrying it.
    ///
    /// While carrying toast, the toast is NOT parented (avoids scale corruption
    /// under non-uniform spatula tip hierarchy). It follows the attach point pose
    /// each LateUpdate via position/rotation only.
    /// While carrying toast, if it enters the lunch box or toaster zone it is
    /// placed automatically (seal / snap) without needing to release the spatula.
    /// After any transfer (attach or place), the reverse transfer is blocked until
    /// the tip leaves that toast collision / the toast leaves the placement zone,
    /// each requiring <see cref="_edgeSettleDelay"/> of continuous settle so edge
    /// flicker (enter/exit spam) does not bounce toast back and forth.
    /// Releasing the spatula still drops the toast (place if in a zone, else free physics).
    ///
    /// Setup: Meta Grabbable + HandGrabInteractable on the spatula; tip child
    /// with a trigger Collider + this component; empty AttachPoint aligned to
    /// the blade; assign ToasterTriggerZone and LunchBoxController refs.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SpatulaToastGrab : MonoBehaviour
    {
        [Header("Spatula")]
        [Tooltip("Spatula Meta Grabbable. Auto-filled from parents when left empty.")]
        [SerializeField] private Grabbable _spatulaGrabbable;

        [Tooltip("Empty child on the blade. Toast snaps to this local pose while carried. Defaults to this tip transform when left empty.")]
        [SerializeField] private Transform _attachPoint;

        [Header("Toast")]
        [Tooltip("Must match the toast GameObject tag used by ToasterTriggerZone / LunchBoxController.")]
        [SerializeField] private string _toastTag = "toast";

        [Header("Placement Targets")]
        [Tooltip("Assign the toaster's ToastTrigger (ToasterTriggerZone). Unfreezes snapped toast on pickup; auto-snaps when carried toast re-enters the zone.")]
        [SerializeField] private ToasterTriggerZone _toasterZone;

        [Tooltip("Assign the food box LunchBoxController. Auto-seals when carried toast enters the box. Also wire that box's ToasterController for task Done.")]
        [SerializeField] private LunchBoxController _lunchBox;

        [Header("Edge Settle")]
        [Tooltip("Seconds the toast must stay continuously inside/outside a zone before attach/place toggles. Prevents edge flicker (enter/exit spam).")]
        [SerializeField] private float _edgeSettleDelay = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool _enableDebugLogs = true;

        /// <summary>True while the spatula Grabbable is selected by a hand/controller.</summary>
        public bool IsSpatulaHeld => _isSpatulaHeld;

        /// <summary>Toast currently parented to the spatula tip, or null.</summary>
        public GameObject AttachedToast => _attachedToast;

        private bool _isSpatulaHeld;
        private GameObject _attachedToast;
        private Rigidbody _attachedToastRigidbody;

        private bool _cachedRigidbodyState;
        private bool _originalUseGravity;
        private bool _originalIsKinematic;

        // After picking toast up inside a placement zone, ignore that zone for
        // auto-place until the toast fully leaves it once.
        private bool _suppressToasterAutoPlace;
        private bool _suppressLunchBoxAutoPlace;
        private float _toasterLeftTimer;
        private float _lunchBoxLeftTimer;

        // Auto-place only after continuous presence in the target zone.
        private float _toasterReadyTimer;
        private float _lunchBoxReadyTimer;

        // After placing toast down, do not re-attach the same toast until the
        // spatula tip has been outside its collider for _edgeSettleDelay.
        private GameObject _blockAttachToast;
        private bool _tipExitPending;
        private float _tipExitTimer;

        private readonly List<Behaviour> _disabledGrabBehaviours = new List<Behaviour>();
        private readonly List<bool> _disabledGrabWasEnabled = new List<bool>();

        private void Awake()
        {
            if (_spatulaGrabbable == null)
                _spatulaGrabbable = GetComponentInParent<Grabbable>();

            if (_attachPoint == null)
                _attachPoint = transform;

            Collider tipCollider = GetComponent<Collider>();
            if (tipCollider != null && !tipCollider.isTrigger)
            {
                Debug.LogWarning($"[SpatulaToastGrab] Tip collider on '{name}' should be a trigger.", this);
            }

            if (_enableDebugLogs && _spatulaGrabbable == null)
                Debug.LogWarning($"[SpatulaToastGrab] No spatula Grabbable found for '{name}'.", this);
        }

        private void OnEnable()
        {
            if (_spatulaGrabbable != null)
                _spatulaGrabbable.WhenPointerEventRaised += HandleSpatulaPointerEvent;
        }

        private void OnDisable()
        {
            if (_spatulaGrabbable != null)
                _spatulaGrabbable.WhenPointerEventRaised -= HandleSpatulaPointerEvent;

            if (_attachedToast != null)
                DetachToast();
        }

        private void Update()
        {
            UpdateTipExitSettle();

            if (_attachedToast == null)
                return;

            UpdateAutoPlaceSuppression();
            TryAutoPlaceAttachedToast();
        }

        private void LateUpdate()
        {
            FollowAttachedToastPose();
        }

        private void OnTriggerEnter(Collider other)
        {
            CancelTipExitIfOverlapping(other);
            TryAttachFromCollider(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // Covers the case where the tip was already overlapping the toast
            // when the spatula became held (Enter would have been missed).
            CancelTipExitIfOverlapping(other);
            TryAttachFromCollider(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null || !other.CompareTag(_toastTag))
                return;

            GameObject toastRoot = ResolveToastRoot(other);
            if (_blockAttachToast != null && toastRoot == _blockAttachToast)
            {
                // Don't clear immediately - wait _edgeSettleDelay so edge flicker
                // (exit/enter/exit) does not re-enable attach too soon.
                _tipExitPending = true;
                _tipExitTimer = 0f;

                if (_enableDebugLogs)
                    Debug.Log($"[SpatulaToastGrab] Tip exit started for '{toastRoot.name}' - settle {_edgeSettleDelay:0.##}s.", toastRoot);
            }
        }

        private void CancelTipExitIfOverlapping(Collider other)
        {
            if (!_tipExitPending || _blockAttachToast == null || other == null || !other.CompareTag(_toastTag))
                return;

            if (ResolveToastRoot(other) == _blockAttachToast)
            {
                _tipExitPending = false;
                _tipExitTimer = 0f;
            }
        }

        private void UpdateTipExitSettle()
        {
            if (!_tipExitPending || _blockAttachToast == null)
                return;

            _tipExitTimer += Time.deltaTime;
            if (_tipExitTimer < _edgeSettleDelay)
                return;

            if (_enableDebugLogs)
                Debug.Log($"[SpatulaToastGrab] Tip exit settled for '{_blockAttachToast.name}' - re-attach allowed.", _blockAttachToast);

            _blockAttachToast = null;
            _tipExitPending = false;
            _tipExitTimer = 0f;
        }

        private void HandleSpatulaPointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Select:
                    _isSpatulaHeld = true;
                    if (_enableDebugLogs)
                        Debug.Log("[SpatulaToastGrab] Spatula held.", this);
                    break;

                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    _isSpatulaHeld = false;
                    if (_enableDebugLogs)
                        Debug.Log("[SpatulaToastGrab] Spatula released.", this);
                    if (_attachedToast != null)
                        DetachToast();
                    break;
            }
        }

        private void TryAttachFromCollider(Collider other)
        {
            if (!_isSpatulaHeld || _attachedToast != null)
                return;

            if (other == null || !other.CompareTag(_toastTag))
                return;

            GameObject toastRoot = ResolveToastRoot(other);
            Grabbable toastGrabbable = other.GetComponentInParent<Grabbable>();

            if (_blockAttachToast != null && toastRoot == _blockAttachToast)
                return;

            if (IsToastHeldByHand(toastGrabbable))
            {
                if (_enableDebugLogs)
                    Debug.Log($"[SpatulaToastGrab] Ignoring toast '{toastRoot.name}' - already held by hand.", toastRoot);
                return;
            }

            AttachToast(toastRoot, other.GetComponentInParent<Rigidbody>());
        }

        private static GameObject ResolveToastRoot(Collider other)
        {
            Grabbable toastGrabbable = other.GetComponentInParent<Grabbable>();
            if (toastGrabbable != null)
                return toastGrabbable.gameObject;

            if (other.attachedRigidbody != null)
                return other.attachedRigidbody.gameObject;

            return other.gameObject;
        }

        private static bool IsToastHeldByHand(Grabbable toastGrabbable)
        {
            if (toastGrabbable == null)
                return false;

            List<Pose> grabPoints = toastGrabbable.GrabPoints;
            return grabPoints != null && grabPoints.Count > 0;
        }

        private void AttachToast(GameObject toastRoot, Rigidbody toastRigidbody)
        {
            if (toastRoot == null)
                return;

            if (_toasterZone != null && _toasterZone.IsTracking(toastRoot))
                _toasterZone.ReleaseSnapForPickup();

            _attachedToast = toastRoot;
            _attachedToastRigidbody = toastRigidbody;
            _blockAttachToast = null;

            // Avoid instantly re-placing toast that was already sitting in a zone.
            _suppressToasterAutoPlace = _toasterZone != null && _toasterZone.IsTracking(toastRoot);
            _suppressLunchBoxAutoPlace = _lunchBox != null && _lunchBox.IsTracking(toastRoot);
            _toasterLeftTimer = 0f;
            _lunchBoxLeftTimer = 0f;
            _toasterReadyTimer = 0f;
            _lunchBoxReadyTimer = 0f;
            _tipExitPending = false;
            _tipExitTimer = 0f;

            CacheAndFreezeRigidbody();
            DisableToastGrabInteractables(toastRoot);

            // Do not parent under the spatula tip — non-uniform tip/cup scale
            // corrupts toast localScale. Follow pose in LateUpdate instead.
            FollowAttachedToastPose();

            if (_enableDebugLogs)
                Debug.Log($"[SpatulaToastGrab] Attached toast '{_attachedToast.name}'.", _attachedToast);
        }

        private void FollowAttachedToastPose()
        {
            if (_attachedToast == null)
                return;

            Transform attach = _attachPoint != null ? _attachPoint : transform;
            _attachedToast.transform.SetPositionAndRotation(attach.position, attach.rotation);
        }

        private void UpdateAutoPlaceSuppression()
        {
            float delay = Mathf.Max(0f, _edgeSettleDelay);

            if (_suppressToasterAutoPlace)
            {
                bool stillInside = _toasterZone != null && _toasterZone.IsTracking(_attachedToast);
                if (stillInside)
                {
                    _toasterLeftTimer = 0f;
                }
                else
                {
                    _toasterLeftTimer += Time.deltaTime;
                    if (_toasterLeftTimer >= delay)
                    {
                        _suppressToasterAutoPlace = false;
                        _toasterLeftTimer = 0f;
                    }
                }
            }
            else
            {
                _toasterLeftTimer = 0f;
            }

            if (_suppressLunchBoxAutoPlace)
            {
                bool stillInside = _lunchBox != null && _lunchBox.IsTracking(_attachedToast);
                if (stillInside)
                {
                    _lunchBoxLeftTimer = 0f;
                }
                else
                {
                    _lunchBoxLeftTimer += Time.deltaTime;
                    if (_lunchBoxLeftTimer >= delay)
                    {
                        _suppressLunchBoxAutoPlace = false;
                        _lunchBoxLeftTimer = 0f;
                    }
                }
            }
            else
            {
                _lunchBoxLeftTimer = 0f;
            }
        }

        /// <summary>
        /// Places toast after it has stayed in a non-suppressed placement zone
        /// for <see cref="_edgeSettleDelay"/> (avoids edge enter/exit spam).
        /// </summary>
        private void TryAutoPlaceAttachedToast()
        {
            if (_attachedToast == null)
                return;

            float delay = Mathf.Max(0f, _edgeSettleDelay);

            bool lunchReady = _lunchBox != null
                && !_suppressLunchBoxAutoPlace
                && _lunchBox.IsTracking(_attachedToast);

            bool toasterReady = _toasterZone != null
                && !_suppressToasterAutoPlace
                && _toasterZone.IsTracking(_attachedToast);

            if (lunchReady)
                _lunchBoxReadyTimer += Time.deltaTime;
            else
                _lunchBoxReadyTimer = 0f;

            if (toasterReady)
                _toasterReadyTimer += Time.deltaTime;
            else
                _toasterReadyTimer = 0f;

            bool lunchSettled = lunchReady && _lunchBoxReadyTimer >= delay;
            bool toasterSettled = toasterReady && _toasterReadyTimer >= delay;

            if (!lunchSettled && !toasterSettled)
                return;

            if (_enableDebugLogs)
            {
                Debug.Log(
                    $"[SpatulaToastGrab] Auto-placing toast '{_attachedToast.name}' " +
                    $"({(lunchSettled ? "lunch box" : "toaster")}) after {delay:0.##}s settle.",
                    _attachedToast);
            }

            DetachToast();
        }

        private void DetachToast()
        {
            GameObject toast = _attachedToast;
            if (toast == null)
                return;

            RestoreToastGrabInteractables();

            bool placed = false;

            if (_lunchBox != null && _lunchBox.IsTracking(toast))
            {
                _lunchBox.PlaceToastAndSeal();
                placed = true;
                if (_enableDebugLogs)
                    Debug.Log($"[SpatulaToastGrab] Released toast '{toast.name}' into lunch box.", toast);
            }
            else if (_toasterZone != null && _toasterZone.IsTracking(toast))
            {
                _toasterZone.SnapToastIntoPlace();
                placed = true;
                if (_enableDebugLogs)
                    Debug.Log($"[SpatulaToastGrab] Released toast '{toast.name}' into toaster.", toast);
            }

            if (!placed)
                RestoreRigidbody();

            // Block reverse attach while the tip is still overlapping this toast.
            // Cleared only after tip exit has settled for _edgeSettleDelay.
            if (placed)
            {
                _blockAttachToast = toast;
                _tipExitPending = false;
                _tipExitTimer = 0f;
            }

            ClearAttachedState();

            if (_enableDebugLogs && !placed)
                Debug.Log($"[SpatulaToastGrab] Dropped toast '{toast.name}' with free physics.", toast);
        }

        private void CacheAndFreezeRigidbody()
        {
            _cachedRigidbodyState = false;

            if (_attachedToastRigidbody == null)
                return;

            _originalUseGravity = _attachedToastRigidbody.useGravity;
            _originalIsKinematic = _attachedToastRigidbody.isKinematic;
            _cachedRigidbodyState = true;

            _attachedToastRigidbody.linearVelocity = Vector3.zero;
            _attachedToastRigidbody.angularVelocity = Vector3.zero;
            _attachedToastRigidbody.useGravity = false;
            _attachedToastRigidbody.isKinematic = true;
        }

        private void RestoreRigidbody()
        {
            if (!_cachedRigidbodyState || _attachedToastRigidbody == null)
                return;

            _attachedToastRigidbody.isKinematic = _originalIsKinematic;
            _attachedToastRigidbody.useGravity = _originalUseGravity;
            _attachedToastRigidbody.linearVelocity = Vector3.zero;
            _attachedToastRigidbody.angularVelocity = Vector3.zero;
        }

        private void DisableToastGrabInteractables(GameObject toastRoot)
        {
            _disabledGrabBehaviours.Clear();
            _disabledGrabWasEnabled.Clear();

            HandGrabInteractable[] handGrabs = toastRoot.GetComponentsInChildren<HandGrabInteractable>(true);
            for (int i = 0; i < handGrabs.Length; i++)
                CacheAndDisable(handGrabs[i]);

            GrabInteractable[] grabInteractables = toastRoot.GetComponentsInChildren<GrabInteractable>(true);
            for (int i = 0; i < grabInteractables.Length; i++)
                CacheAndDisable(grabInteractables[i]);
        }

        private void CacheAndDisable(Behaviour behaviour)
        {
            if (behaviour == null)
                return;

            _disabledGrabBehaviours.Add(behaviour);
            _disabledGrabWasEnabled.Add(behaviour.enabled);
            behaviour.enabled = false;
        }

        private void RestoreToastGrabInteractables()
        {
            for (int i = 0; i < _disabledGrabBehaviours.Count; i++)
            {
                Behaviour behaviour = _disabledGrabBehaviours[i];
                if (behaviour != null)
                    behaviour.enabled = _disabledGrabWasEnabled[i];
            }

            _disabledGrabBehaviours.Clear();
            _disabledGrabWasEnabled.Clear();
        }

        private void ClearAttachedState()
        {
            _attachedToast = null;
            _attachedToastRigidbody = null;
            _cachedRigidbodyState = false;
            _suppressToasterAutoPlace = false;
            _suppressLunchBoxAutoPlace = false;
            _toasterLeftTimer = 0f;
            _lunchBoxLeftTimer = 0f;
            _toasterReadyTimer = 0f;
            _lunchBoxReadyTimer = 0f;
            // Keep _blockAttachToast / tip-exit settle - survives until tip exits that toast.
        }
    }
}
