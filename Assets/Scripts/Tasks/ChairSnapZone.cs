using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the stool placement trigger (the same <c>zone</c> object as
    /// MaxStepTrigger / StoolStandZone). When the chair is released (Meta ISDK
    /// Unselect) while inside this trigger, it is snapped to
    /// <see cref="_snapAnchor"/> and frozen (kinematic, zero velocity) so it
    /// sits upright. Grabbing it again unfreezes it so the player can move it.
    ///
    /// Detects the chair via <see cref="ChairGrabState"/> / <see cref="Grabbable"/>
    /// on the collider or its parents — no tag required.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ChairSnapZone : MonoBehaviour
    {
        [Tooltip("Pose the chair snaps to when released inside the zone. Defaults to this object's transform when left empty.")]
        [SerializeField] private Transform _snapAnchor;

        [SerializeField] private bool enableDebugLogs;

        private GameObject _chair;
        private Grabbable _grabbable;
        private Rigidbody _chairRigidbody;
        private int _overlapCount;

        private bool _cachedRigidbodyState;
        private bool _originalUseGravity;
        private bool _originalIsKinematic;
        private bool _isFrozen;

        /// <summary>True while a chair is currently tracked inside this zone.</summary>
        public bool IsTrackingChair => _chair != null;

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (_snapAnchor == null)
            {
                _snapAnchor = transform;
            }
        }

        private void OnDisable()
        {
            UnfreezeChair();
            Unsubscribe();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!TryResolveChair(other, out Grabbable grabbable, out GameObject chairRoot, out Rigidbody rigidbody))
            {
                return;
            }

            if (_chair == chairRoot)
            {
                _overlapCount++;
                return;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[ChairSnapZone] Chair entered zone: {chairRoot.name}", chairRoot);
            }

            TrackChair(grabbable, chairRoot, rigidbody);
            _overlapCount = 1;
        }

        private void OnTriggerExit(Collider other)
        {
            if (_chair == null)
            {
                return;
            }

            if (!other.transform.IsChildOf(_chair.transform) && other.transform != _chair.transform)
            {
                return;
            }

            _overlapCount--;
            if (_overlapCount > 0)
            {
                return;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[ChairSnapZone] Chair exited zone: {_chair.name}", _chair);
            }

            UnfreezeChair();
            Unsubscribe();
        }

        private static bool TryResolveChair(
            Collider other,
            out Grabbable grabbable,
            out GameObject chairRoot,
            out Rigidbody rigidbody)
        {
            ChairGrabState grabState = other.GetComponentInParent<ChairGrabState>();
            if (grabState == null)
            {
                grabbable = null;
                chairRoot = null;
                rigidbody = null;
                return false;
            }

            grabbable = grabState.GetComponent<Grabbable>();
            rigidbody = grabState.GetComponent<Rigidbody>();
            chairRoot = grabState.gameObject;
            return true;
        }

        private void TrackChair(Grabbable grabbable, GameObject chairRoot, Rigidbody rigidbody)
        {
            Unsubscribe();

            _chair = chairRoot;
            _grabbable = grabbable;
            _chairRigidbody = rigidbody;
            _cachedRigidbodyState = false;
            _isFrozen = false;

            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
            else if (enableDebugLogs)
            {
                Debug.LogWarning($"[ChairSnapZone] Chair '{chairRoot.name}' has no Oculus.Interaction.Grabbable; snap-on-release disabled.", chairRoot);
            }
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Unselect:
                    SnapChairIntoPlace();
                    break;

                case PointerEventType.Select:
                    UnfreezeChair();
                    break;
            }
        }

        /// <summary>
        /// Snap the tracked chair to the anchor pose and freeze it in place.
        /// Also usable from a UnityEvent if wired in the Inspector.
        /// </summary>
        public void SnapChairIntoPlace()
        {
            if (_chair == null || _snapAnchor == null)
            {
                return;
            }

            if (_chairRigidbody != null)
            {
                CacheRigidbodyStateIfNeeded();
                _chairRigidbody.linearVelocity = Vector3.zero;
                _chairRigidbody.angularVelocity = Vector3.zero;
                _chairRigidbody.useGravity = false;
                _chairRigidbody.isKinematic = true;
            }

            _chair.transform.SetPositionAndRotation(_snapAnchor.position, _snapAnchor.rotation);

            _isFrozen = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[ChairSnapZone] Snapped chair '{_chair.name}' into place.", _chair);
            }
        }

        private void UnfreezeChair()
        {
            if (!_isFrozen || _chairRigidbody == null)
            {
                return;
            }

            if (_cachedRigidbodyState)
            {
                _chairRigidbody.isKinematic = _originalIsKinematic;
                _chairRigidbody.useGravity = _originalUseGravity;
            }

            _isFrozen = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[ChairSnapZone] Unfroze chair '{_chair?.name}'.", _chair);
            }
        }

        private void CacheRigidbodyStateIfNeeded()
        {
            if (_cachedRigidbodyState || _chairRigidbody == null)
            {
                return;
            }

            _originalUseGravity = _chairRigidbody.useGravity;
            _originalIsKinematic = _chairRigidbody.isKinematic;
            _cachedRigidbodyState = true;
        }

        private void Unsubscribe()
        {
            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }

            _grabbable = null;
            _chair = null;
            _chairRigidbody = null;
            _overlapCount = 0;
            _cachedRigidbodyState = false;
            _isFrozen = false;
        }
    }
}
