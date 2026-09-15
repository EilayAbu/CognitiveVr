using CognitiveVR.Interaction;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Core
{
    /// <summary>
    /// Put this on every prop that must never get lost: the backpack itself and
    /// each item the participant can pick up. The component remembers the pose
    /// the prop started the session in and sends it back there whenever it ends
    /// up inside an <see cref="OutOfBoundsVolume"/> or below the volume's floor
    /// threshold.
    ///
    /// Two cases are deliberately left alone:
    ///  - while the prop is held (any interactor selects its Grabbable), so the
    ///    object is never yanked out of the participant's hand; the check keeps
    ///    running and the prop goes home the moment it is released.
    ///  - while the prop is stored in a backpack slot
    ///    (<see cref="InventoryItemMetaBridge.IsStoredInInventory"/>): the
    ///    backpack carries its contents, so only the backpack itself respawns
    ///    and the stored items travel with it.
    ///
    /// Everything Meta-ISDK-specific is optional, so the component also works on
    /// plain physics props without a Grabbable.
    /// </summary>
    [DisallowMultipleComponent]
    public class RespawnableObject : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Seconds between out-of-bounds checks.")]
        [SerializeField] private float checkInterval = 0.25f;

        [Tooltip("Sample the center of the object's collider bounds instead of its pivot. Useful for props whose pivot sits far from the mesh.")]
        [SerializeField] private bool useColliderCenter = true;

        [Tooltip("Also check right after the session starts (off by default, so a prop that legitimately starts low is not sent home immediately).")]
        [SerializeField] private bool respawnOnStartIfOutOfBounds;

        [Header("Floor Override")]
        [Tooltip("Ignore the floor height configured on the OutOfBoundsVolume and use the value below for this prop only.")]
        [SerializeField] private bool overrideFloorY;

        [Tooltip("World Y height under which THIS prop is sent home. Only used when Override Floor Y is on.")]
        [SerializeField] private float floorYOverride = -0.5f;

        [Header("Respawn")]
        [Tooltip("Minimum seconds between two respawns of this prop.")]
        [SerializeField] private float respawnCooldown = 0.5f;

        [Tooltip("Keep the Rigidbody kinematic for this many frames after a respawn, to avoid ghost collisions at the home position.")]
        [SerializeField] private int sleepFrames = 3;

        [Tooltip("Invoked after the prop has been placed back at its starting pose (sound, task logic...).")]
        [SerializeField] private UnityEvent whenRespawned = new UnityEvent();

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        /// <summary>Raised after the prop has been placed back at its starting pose.</summary>
        public UnityEvent WhenRespawned => whenRespawned;

        /// <summary>How many times this prop was sent home this session.</summary>
        public int RespawnCount { get; private set; }

        private Rigidbody _rigidbody;
        private Grabbable _grabbable;
        private PointableElement _pointableElement;
        private InventoryItemMetaBridge _inventoryItem;
        private Collider[] _colliders;

        private Transform _homeParent;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Vector3 _homeLocalScale;
        private bool _homeCaptured;
        private bool _homeOutOfBounds;

        private float _nextCheckTime;
        private float _lastRespawnTime = float.NegativeInfinity;
        private int _sleepCountDown;
        private bool _pendingRespawn;
        private string _pendingReason;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _grabbable = GetComponent<Grabbable>();
            _pointableElement = _grabbable != null ? _grabbable : GetComponent<PointableElement>();
            _inventoryItem = GetComponent<InventoryItemMetaBridge>();
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        // Start (not OnEnable) so editor/runtime setup passes that reposition or
        // reparent props have already finished when the home pose is captured.
        private void Start()
        {
            CaptureHome();

            // Volumes register in OnEnable, so by Start they can already be
            // queried. A home pose that is itself out of bounds would make every
            // respawn a no-op and fire the event forever, so give up (loudly)
            // instead of looping.
            if (OutOfBoundsVolume.IsOutOfBounds(SamplePoint(), out string homeReason))
            {
                _homeOutOfBounds = true;
                Debug.LogWarning(
                    $"[{nameof(RespawnableObject)}] '{name}' starts out of bounds ({homeReason}), so it has nowhere to return to. " +
                    "Move it inside the play area or shrink the OutOfBoundsVolume. Checks are disabled for this prop.",
                    this);
                return;
            }

            _nextCheckTime = Time.time + (respawnOnStartIfOutOfBounds ? 0f : checkInterval);
        }

        /// <summary>
        /// Records the current pose as the pose the prop returns to. Call it if
        /// the prop is legitimately moved to a new "starting" place at runtime.
        /// </summary>
        public void CaptureHome()
        {
            _homeParent = transform.parent;
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _homeLocalScale = transform.localScale;
            _homeCaptured = true;
        }

        private void Update()
        {
            if (_homeOutOfBounds || Time.time < _nextCheckTime)
            {
                return;
            }

            _nextCheckTime = Time.time + Mathf.Max(0.02f, checkInterval);

            if (IsBusy())
            {
                return;
            }

            if (_pendingRespawn)
            {
                string reason = _pendingReason;
                _pendingRespawn = false;
                _pendingReason = null;
                Respawn(reason);
                return;
            }

            if (IsOutOfBounds(out string outReason))
            {
                Respawn(outReason);
            }
        }

        private void FixedUpdate()
        {
            if (_sleepCountDown > 0 && --_sleepCountDown == 0 && _rigidbody != null)
            {
                _rigidbody.isKinematic = false;
            }
        }

        /// <summary>
        /// Entry point for <see cref="OutOfBoundsVolume"/>'s trigger fast path.
        /// Respawns immediately when allowed, otherwise remembers the request and
        /// handles it on the next check (i.e. once the prop is released).
        /// </summary>
        public void RequestRespawn(string reason = null)
        {
            if (_homeOutOfBounds)
            {
                return;
            }

            if (IsBusy())
            {
                _pendingRespawn = true;
                _pendingReason = reason;
                return;
            }

            Respawn(reason);
        }

        /// <summary>Sends the prop back to its starting pose. Safe to wire to a UnityEvent.</summary>
        public void Respawn()
        {
            Respawn(null);
        }

        private void Respawn(string reason)
        {
            if (!_homeCaptured)
            {
                CaptureHome();
                return;
            }

            // A prop can end up parented under a backpack slot, so the parent has
            // to be restored before the pose is applied.
            if (transform.parent != _homeParent)
            {
                transform.SetParent(_homeParent, true);
            }

            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            transform.localScale = _homeLocalScale;

            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;

                if (!_rigidbody.isKinematic && sleepFrames > 0)
                {
                    _sleepCountDown = sleepFrames;
                    _rigidbody.isKinematic = true;
                }
                else if (!_rigidbody.isKinematic)
                {
                    _rigidbody.Sleep();
                }
            }

            // GrabFreeTransformer clamps localScale to whatever it captured in
            // Initialize, so it has to re-anchor on the restored scale - the same
            // reason InventoryItemMetaBridge re-initializes its transformers.
            if (_grabbable != null)
            {
                foreach (GrabFreeTransformer transformer in GetComponents<GrabFreeTransformer>())
                {
                    transformer.Initialize(_grabbable);
                }
            }

            _lastRespawnTime = Time.time;
            RespawnCount++;
            whenRespawned.Invoke();

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RespawnableObject)}] '{name}' returned to its starting pose ({reason ?? "manual"}).", this);
            }
        }

        /// <summary>
        /// True while the prop must not be touched: held by an interactor, stored
        /// in a backpack slot, or still inside the respawn cooldown.
        /// </summary>
        private bool IsBusy()
        {
            if (Time.time - _lastRespawnTime < respawnCooldown)
            {
                return true;
            }

            if (_inventoryItem != null && _inventoryItem.IsStoredInInventory)
            {
                return true;
            }

            // Covers both a hand holding the prop and the SnapInteractor holding
            // it inside a slot.
            return _pointableElement != null && _pointableElement.SelectingPointsCount > 0;
        }

        private bool IsOutOfBounds(out string reason)
        {
            Vector3 point = SamplePoint();

            if (overrideFloorY)
            {
                if (point.y < floorYOverride)
                {
                    reason = $"below_floor_override:{floorYOverride:F2}";
                    return true;
                }

                return OutOfBoundsVolume.IsInsideAnyVolume(point, out reason);
            }

            return OutOfBoundsVolume.IsOutOfBounds(point, out reason);
        }

        private Vector3 SamplePoint()
        {
            if (!useColliderCenter || _colliders == null)
            {
                return transform.position;
            }

            foreach (Collider col in _colliders)
            {
                if (col != null && col.enabled && col.gameObject.activeInHierarchy)
                {
                    return col.bounds.center;
                }
            }

            return transform.position;
        }
    }
}
