using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the lunch box's trigger child (ToastTrigger). The lid GameObject
    /// starts hidden; the box body (Rigidbody + solid collider + Grabbable) is a
    /// normal grabbable prop from the start.
    ///
    /// The toast (tagged GameObject) is placed when any of these happens while it
    /// is inside the trigger:
    ///   release - the LAST hand holding it lets go (Meta ISDK Unselect with no
    ///             selecting points left, so a two-hand hold or a hand swap does
    ///             not seal early),
    ///   drop_in - it is inside, nobody holds it and it is a free physics body
    ///             (released above the box and fell in),
    ///   external - <see cref="PlaceToastAndSeal"/> was called directly (the
    ///             spatula does this after its own settle delay).
    /// Release/drop-in seals run on the next Update, outside the ISDK pointer
    /// callback, so disabling the toast never re-enters the Grabbable mid-event.
    ///
    /// While the toast is tracked its colliders ignore the box body's colliders,
    /// so pushing it into the box neither shoves the box away nor bounces the
    /// toast out of the trigger. The ignore is undone if the toast leaves again.
    ///
    /// Sealing snaps the toast to the anchor, parents and hides it, enables the
    /// lid and the <see cref="_componentsToEnableWhenReady"/>, and calls
    /// <c>RemoveToast()</c> on the assigned <see cref="ToasterController"/> so
    /// its lights turn off and its task step completes.
    ///
    /// The sequence is one-shot: once the box is sealed it will not re-trigger.
    /// </summary>
    public class LunchBoxController : MonoBehaviour
    {
        [Header("Toast")]
        [Tooltip("Tag the toast GameObject must have to be accepted by the box.")]
        [SerializeField] private string _toastTag = "toast";

        [Tooltip("Pose the toast snaps to when released inside the box. Defaults to this object's transform when left empty.")]
        [SerializeField] private Transform _snapAnchor;

        [Tooltip("If true, the toast is parented to the box so it travels with it into the backpack.")]
        [SerializeField] private bool _reparentToastToBox = true;

        [Tooltip("Seal when the toast is inside the box, nobody holds it and it is a free physics body (released above the box and fell in).")]
        [SerializeField] private bool _sealOnDropIn = true;

        [Tooltip("While the toast is inside the trigger, ignore collisions between it and the box body so it can't shove the box or bounce out.")]
        [SerializeField] private bool _ignoreBoxCollisionWhileInside = true;

        [Header("Lid")]
        [Tooltip("Lid GameObject that starts inactive and is enabled when the toast is placed.")]
        [SerializeField] private GameObject _lid;

        [Header("Box Body")]
        [Tooltip("The box's own Rigidbody. Auto-filled from this GameObject when left empty.")]
        [SerializeField] private Rigidbody _boxRigidbody;

        [Tooltip("The box's trigger collider. Auto-filled from this GameObject when left empty.")]
        [SerializeField] private Collider _triggerCollider;

        [Tooltip("Components enabled once the box is sealed (e.g. the box's disabled Grabbable + InventoryItemMetaBridge) so it can be grabbed and stored in the backpack.")]
        [SerializeField] private List<Behaviour> _componentsToEnableWhenReady = new List<Behaviour>();

        [Header("Toaster Integration")]
        [Tooltip("Optional. When the box is sealed the toaster's RemoveToast() is called so its ready/burnt lights turn off and its task step completes. Leave empty to skip.")]
        [SerializeField] private ToasterController _toasterController;

        [Header("Events")]
        [Tooltip("Invoked after the box is sealed (toast placed, lid on, box grabbable). Useful for audio / task reporting.")]
        [SerializeField] private UnityEvent onBoxSealed;

        [Header("Debug")]
        [SerializeField] private bool _enableDebugLogs = true;

        /// <summary>Fired after the box is sealed. Parameters: toast name, whether a ToasterController was wired up.</summary>
        public event System.Action<string, bool> OnBoxSealed;

        /// <summary>True once the box has been sealed.</summary>
        public bool IsClosed => _isClosed;

        /// <summary>True while a toast is being tracked and waiting for release.</summary>
        public bool HasToastTracked => _toast != null;

        /// <summary>How the box was sealed: "release", "drop_in" or "external". Empty until sealed.</summary>
        public string SealSource => _sealSource;

        /// <summary>True while this specific toast root is tracked inside the box.</summary>
        public bool IsTracking(GameObject toast) =>
            _toast != null && toast != null && _toast == toast;

        private GameObject _toast;
        private Grabbable _grabbable;
        private Rigidbody _toastRigidbody;
        private bool _isClosed;
        private string _sealSource = "";
        private string _pendingSealSource;

        // Toast colliders currently overlapping the trigger. The toast is only
        // dropped when the last one leaves, so one collider's exit can't untrack it.
        private readonly HashSet<Collider> _toastCollidersInside = new HashSet<Collider>();
        private readonly List<Collider> _ignoredToastColliders = new List<Collider>();
        private readonly List<Collider> _ignoredBoxColliders = new List<Collider>();

        private void Awake()
        {
            if (_snapAnchor == null)
                _snapAnchor = transform;

            if (_boxRigidbody == null)
                _boxRigidbody = GetComponent<Rigidbody>();

            if (_triggerCollider == null)
                _triggerCollider = GetComponent<Collider>();
        }

        private void OnDisable()
        {
            _pendingSealSource = null;
            ClearTracking();
        }

        private void Update()
        {
            if (_isClosed || _toast == null)
                return;

            if (_pendingSealSource == null && _sealOnDropIn && IsFreeAndUnheld())
                _pendingSealSource = "drop_in";

            if (_pendingSealSource != null)
            {
                string source = _pendingSealSource;
                _pendingSealSource = null;
                Seal(source);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isClosed || !other.CompareTag(_toastTag))
                return;

            if (_enableDebugLogs && !_toastCollidersInside.Contains(other))
                Debug.Log($"[LunchBoxController] Toast entered box: {other.name}", other);

            TrackToast(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // Recovers a toast that is inside but not tracked (e.g. it was
            // already overlapping when this component got enabled).
            if (_isClosed || !other.CompareTag(_toastTag) || _toastCollidersInside.Contains(other))
                return;

            TrackToast(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_isClosed || !other.CompareTag(_toastTag))
                return;

            if (!_toastCollidersInside.Remove(other) || _toastCollidersInside.Count > 0)
                return;

            if (_enableDebugLogs)
                Debug.Log($"[LunchBoxController] Toast exited box: {other.name}", other);

            _pendingSealSource = null;
            ClearTracking();
        }

        private void TrackToast(Collider other)
        {
            Grabbable grabbable = other.GetComponentInParent<Grabbable>();
            GameObject toastRoot = grabbable != null
                ? grabbable.gameObject
                : other.attachedRigidbody != null
                    ? other.attachedRigidbody.gameObject
                    : other.gameObject;

            if (_toast != null && _toast != toastRoot)
                ClearTracking();

            _toastCollidersInside.Add(other);

            if (_toast == toastRoot)
                return;

            _toast = toastRoot;
            _grabbable = grabbable;
            _toastRigidbody = other.GetComponentInParent<Rigidbody>();

            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
            else if (_enableDebugLogs)
            {
                Debug.LogWarning($"[LunchBoxController] Toast '{other.name}' has no Oculus.Interaction.Grabbable; only drop-in / spatula placement will work.", other);
            }

            if (_ignoreBoxCollisionWhileInside)
                SetBoxCollisionIgnored(true);
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            if (pointerEvent.Type != PointerEventType.Unselect && pointerEvent.Type != PointerEventType.Cancel)
                return;

            // PointableElement removes the point before raising the event, so 0
            // here means the last hand just let go.
            if (_grabbable != null && _grabbable.SelectingPointsCount > 0)
                return;

            // Deferred to Update: sealing deactivates the toast, which must not
            // happen inside the Grabbable's own pointer-event processing.
            _pendingSealSource = "release";
        }

        /// <summary>
        /// True when the tracked toast is lying loose in the box. A kinematic toast
        /// is carried by the spatula or snapped somewhere and is left to that code.
        /// </summary>
        private bool IsFreeAndUnheld()
        {
            if (_grabbable != null && _grabbable.SelectingPointsCount > 0)
                return false;

            return _toastRigidbody != null && !_toastRigidbody.isKinematic;
        }

        /// <summary>
        /// Snap the tracked toast into place, reveal the lid and convert the box
        /// into a grabbable item. Safe to call once; subsequent calls are ignored.
        /// </summary>
        public void PlaceToastAndSeal()
        {
            Seal("external");
        }

        private void Seal(string source)
        {
            if (_isClosed || _toast == null)
                return;

            _sealSource = source;

            if (_snapAnchor != null)
                _toast.transform.SetPositionAndRotation(_snapAnchor.position, _snapAnchor.rotation);

            if (_reparentToastToBox)
            {
                _toast.transform.SetParent(transform, true);
            }

            // NOTE: this deliberately runs whether or not the toast was
            // reparented - it used to sit under the if() by indentation only,
            // which is a classic missing-brace trap. Behaviour is unchanged.
            _toast.SetActive(false);

            if (_toastRigidbody != null)
            {
                _toastRigidbody.linearVelocity = Vector3.zero;
                _toastRigidbody.angularVelocity = Vector3.zero;
                _toastRigidbody.useGravity = false;
                _toastRigidbody.isKinematic = true;
            }

            if (_lid != null)
                _lid.SetActive(true);

            if (_toasterController != null)
            {
                _toasterController.RemoveToast();
            }
            else
            {
                Debug.LogWarning($"[LunchBoxController] Box sealed but no ToasterController is assigned - "
                    + "RemoveToast() was not called, so the toaster never reaches Done and no burn severity is recorded.", this);
            }

            EnableReadyComponents();
            MakeBoxGrabbable();

            _isClosed = true;
            _pendingSealSource = null;
            UnsubscribeGrabbable();

            // The toast is hidden for good; dropping the ignore list is enough
            // (Unity already reset the ignores when its colliders deactivated).
            _ignoredToastColliders.Clear();
            _ignoredBoxColliders.Clear();
            _toastCollidersInside.Clear();

            if (_enableDebugLogs)
                Debug.Log($"[LunchBoxController] Box sealed with toast '{_toast.name}' ({source}).", this);

            onBoxSealed?.Invoke();
            OnBoxSealed?.Invoke(_toast != null ? _toast.name : "", _toasterController != null);
        }

        private void EnableReadyComponents()
        {
            foreach (Behaviour behaviour in _componentsToEnableWhenReady)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }
        }

        private void MakeBoxGrabbable()
        {
            if (_triggerCollider != null)
                _triggerCollider.isTrigger = false;

            if (_boxRigidbody != null)
            {
                _boxRigidbody.isKinematic = false;
                _boxRigidbody.useGravity = true;
            }
        }

        private void SetBoxCollisionIgnored(bool ignore)
        {
            if (ignore)
            {
                if (_toast == null || _boxRigidbody == null)
                    return;

                foreach (Collider toastCol in _toast.GetComponentsInChildren<Collider>())
                {
                    if (!toastCol.isTrigger)
                        _ignoredToastColliders.Add(toastCol);
                }

                foreach (Collider boxCol in _boxRigidbody.GetComponentsInChildren<Collider>())
                {
                    // The lid, if it has colliders, is inactive here and skipped.
                    if (!boxCol.isTrigger && boxCol.enabled && boxCol.gameObject.activeInHierarchy)
                        _ignoredBoxColliders.Add(boxCol);
                }
            }

            foreach (Collider toastCol in _ignoredToastColliders)
            {
                if (toastCol == null) continue;

                foreach (Collider boxCol in _ignoredBoxColliders)
                {
                    if (boxCol != null && boxCol != toastCol)
                        Physics.IgnoreCollision(toastCol, boxCol, ignore);
                }
            }

            if (!ignore)
            {
                _ignoredToastColliders.Clear();
                _ignoredBoxColliders.Clear();
            }
        }

        private void UnsubscribeGrabbable()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised -= HandlePointerEvent;

            _grabbable = null;
        }

        /// <summary>Stops tracking an unsealed toast and restores its collisions with the box.</summary>
        private void ClearTracking()
        {
            UnsubscribeGrabbable();

            if (_isClosed)
                return;

            SetBoxCollisionIgnored(false);
            _toastCollidersInside.Clear();
            _toast = null;
            _toastRigidbody = null;
        }
    }
}
