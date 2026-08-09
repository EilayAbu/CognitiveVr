using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the two-part lunch box base. The base starts static (kinematic
    /// Rigidbody) with a trigger collider, and the lid GameObject starts hidden.
    ///
    /// When the player drops the tagged toast inside the box and releases it
    /// (Meta ISDK Unselect), the toast is snapped to the anchor pose, parented to
    /// the box and frozen so it travels with it. The lid is then enabled and the
    /// box is converted into a normal grabbable item (dynamic Rigidbody, solid
    /// collider, previously-disabled Grabbable / InventoryItemMetaBridge enabled)
    /// so it can be dropped into the backpack. If a <see cref="ToasterController"/>
    /// is assigned, <c>RemoveToast()</c> is called on it when the box seals so the
    /// toaster's indicator lights turn off and its task step is completed.
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

        /// <summary>True while this specific toast root is tracked inside the box.</summary>
        public bool IsTracking(GameObject toast) =>
            _toast != null && toast != null && _toast == toast;

        private GameObject _toast;
        private Grabbable _grabbable;
        private Rigidbody _toastRigidbody;
        private bool _isClosed;

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
            Unsubscribe();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isClosed || !other.CompareTag(_toastTag))
                return;

            if (_enableDebugLogs)
                Debug.Log($"[LunchBoxController] Toast entered box: {other.name}", other);

            TrackToast(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_isClosed || !other.CompareTag(_toastTag))
                return;

            if (_toast != null && other.transform.IsChildOf(_toast.transform))
            {
                if (_enableDebugLogs)
                    Debug.Log($"[LunchBoxController] Toast exited box: {other.name}", other);

                Unsubscribe();
            }
        }

        private void TrackToast(Collider other)
        {
            Grabbable grabbable = other.GetComponentInParent<Grabbable>();
            GameObject toastRoot = grabbable != null
                ? grabbable.gameObject
                : other.attachedRigidbody != null
                    ? other.attachedRigidbody.gameObject
                    : other.gameObject;

            if (_toast == toastRoot && _grabbable == grabbable)
                return;

            Unsubscribe();

            _toast = toastRoot;
            _grabbable = grabbable;
            _toastRigidbody = other.GetComponentInParent<Rigidbody>();

            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
            else if (_enableDebugLogs)
            {
                Debug.LogWarning($"[LunchBoxController] Toast '{other.name}' has no Oculus.Interaction.Grabbable; place-on-release disabled.", other);
            }
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            if (pointerEvent.Type == PointerEventType.Unselect)
                PlaceToastAndSeal();
        }

        /// <summary>
        /// Snap the tracked toast into place, reveal the lid and convert the box
        /// into a grabbable item. Safe to call once; subsequent calls are ignored.
        /// </summary>
        public void PlaceToastAndSeal()
        {
            if (_isClosed || _toast == null)
                return;

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
            Unsubscribe();

            if (_enableDebugLogs)
                Debug.Log($"[LunchBoxController] Box sealed with toast '{_toast.name}'.", this);

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

        private void Unsubscribe()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised -= HandlePointerEvent;

            _grabbable = null;

            if (!_isClosed)
            {
                _toast = null;
                _toastRigidbody = null;
            }
        }
    }
}