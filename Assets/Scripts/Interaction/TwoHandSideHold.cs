using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Lets the player hold a box the way you hold a real one: press both palms
    /// (or both controllers) against two OPPOSITE sides and lift. The box follows
    /// both hands, tilts when one hand goes higher, and drops (with throw velocity)
    /// as soon as the hands move apart.
    ///
    /// Put it on the same GameObject as the box's Grabbable + BoxCollider + Rigidbody.
    /// The normal pinch / palm / controller grab keeps working untouched.
    ///
    /// The side-hold is fed into the Grabbable as two ordinary grab points
    /// (Hover/Select/Move/Unselect pointer events), so everything already
    /// listening to the Grabbable reacts as if it were a normal two-hand grab:
    /// ChairGrabState.IsHeld / HandsHolding, ChairSnapZone (snap on release),
    /// MaxStepTrigger / StoolStandZone held-gates, TutorialFlow "Touch Any".
    /// The transform is driven by the Grabbable's own transformer (GrabFreeTransformer
    /// by default, which keeps scale locked at 1).
    ///
    /// Not seen by ItemUsageTracker: it listens to interactables, and this hold
    /// doesn't go through one, so side-holds won't appear as select rows in the CSV.
    /// </summary>
    [DisallowMultipleComponent]
    public class TwoHandSideHold : MonoBehaviour
    {
        [Header("References (auto-filled from this GameObject)")]
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private BoxCollider box;
        [SerializeField] private Rigidbody body;

        [Header("Which sides count")]
        [Tooltip("Left/right faces of the box (local X).")]
        [SerializeField] private bool useLocalXFaces = true;

        [Tooltip("Front/back faces of the box (local Z).")]
        [SerializeField] private bool useLocalZFaces = true;

        [Tooltip("Top/bottom faces (local Y). Off = only real 'from the sides' holds.")]
        [SerializeField] private bool useLocalYFaces = false;

        [Header("Detection (meters / seconds)")]
        [Tooltip("Each hand must be this close to its side of the box to START the hold. " +
                 "Measured from the hand's closest joint (finger bones / knuckles / wrist), which sit ~1 cm under the skin.")]
        [SerializeField] private float startDistance = 0.06f;

        [Tooltip("Both hands must stay on opposite sides this long before the hold starts (filters brushing past).")]
        [SerializeField] private float startDwell = 0.08f;

        [Tooltip("MAIN release: let go once the hands open this much wider than they were when the hold started. " +
                 "Smaller = lets go more easily.")]
        [SerializeField] private float letGoSpread = 0.035f;

        [Tooltip("Backup release: let go once either hand is this far from the box surface.")]
        [SerializeField] private float letGoDistance = 0.10f;

        [Tooltip("How long the release condition must last before the box drops (filters tracking jitter).")]
        [SerializeField] private float letGoDelay = 0.04f;

        [Tooltip("If a hand loses tracking mid-hold, keep holding this long before dropping the box.")]
        [SerializeField] private float trackingLossGrace = 0.35f;

        [Header("Input")]
        [SerializeField] private bool useHands = true;
        [SerializeField] private bool useControllers = true;

        [Header("Behaviour")]
        [Tooltip("Don't start a side-hold while the box is already grabbed the normal way.")]
        [SerializeField] private bool skipWhenAlreadyGrabbed = true;

        [Tooltip("Make the rigidbody kinematic during the side-hold so gravity doesn't fight the hands. Restored before release, so throwing still works.")]
        [SerializeField] private bool kinematicWhileHeld = true;

        [Header("Events")]
        [SerializeField] private UnityEvent onSideHoldStart = new UnityEvent();
        [SerializeField] private UnityEvent onSideHoldEnd = new UnityEvent();

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;
        [SerializeField] private bool drawGizmos = true;

        /// <summary>True while the box is held by pressing on two sides.</summary>
        public bool IsSideHeld => _held;

        private const float RefreshSourcesEvery = 2f;

        private readonly List<IHand> _hands = new List<IHand>();
        private readonly List<Vector3> _jointsLeft = new List<Vector3>(32);
        private readonly List<Vector3> _jointsRight = new List<Vector3>(32);
        private readonly List<IController> _controllers = new List<IController>();
        private float _nextRefresh;

        private UniqueIdentifier _leftId;
        private UniqueIdentifier _rightId;

        private bool _held;
        private float _dwell;
        private float _separated;
        private float _startSpread;
        private float _lost;
        private Pose _lastLeft;
        private Pose _lastRight;

        private bool _savedKinematic;
        private bool _kinematicSaved;
        private bool _savedTransfer;

        // ------------------------------------------------------------------ lifecycle

        private void Reset() => AutoFill();

        private void Awake()
        {
            AutoFill();
            _leftId = UniqueIdentifier.Generate();
            _rightId = UniqueIdentifier.Generate();
        }

        private void OnDisable()
        {
            if (_held) EndHold("disabled");
            _dwell = 0f;
        }

        private void OnDestroy()
        {
            if (_leftId != null) UniqueIdentifier.Release(_leftId);
            if (_rightId != null) UniqueIdentifier.Release(_rightId);
        }

        private void AutoFill()
        {
            if (grabbable == null) grabbable = GetComponent<Grabbable>();
            if (box == null) box = GetComponent<BoxCollider>();
            if (body == null) body = GetComponent<Rigidbody>();
        }

        // ------------------------------------------------------------------ main loop

        // LateUpdate: hand poses for this frame are final, and the box moves before rendering.
        private void LateUpdate()
        {
            if (grabbable == null || box == null || !grabbable.isActiveAndEnabled || !box.enabled)
            {
                if (_held) EndHold("grabbable/collider disabled");
                return;
            }

            RefreshSourcesIfNeeded();

            bool hasLeft = TryGetHand(Handedness.Left, _jointsLeft, out Pose left);
            bool hasRight = TryGetHand(Handedness.Right, _jointsRight, out Pose right);

            if (_held) UpdateHold(hasLeft, left, hasRight, right);
            else UpdateIdle(hasLeft, left, hasRight, right);
        }

        private void UpdateIdle(bool hasLeft, Pose left, bool hasRight, Pose right)
        {
            if (!hasLeft || !hasRight || (skipWhenAlreadyGrabbed && grabbable.SelectingPointsCount > 0))
            {
                _dwell = 0f;
                return;
            }

            Classify(_jointsLeft, out int axisL, out int signL, out float distL);
            Classify(_jointsRight, out int axisR, out int signR, out float distR);

            bool onOppositeSides = axisL == axisR && signL == -signR && AxisAllowed(axisL);
            bool closeEnough = distL <= startDistance && distR <= startDistance;

            if (!onOppositeSides || !closeEnough)
            {
                _dwell = 0f;
                return;
            }

            _dwell += Time.deltaTime;
            if (_dwell >= startDwell) BeginHold(left, right);
        }

        private void UpdateHold(bool hasLeft, Pose left, bool hasRight, Pose right)
        {
            // Lost tracking: hold the last pose for a moment instead of dropping instantly.
            if (!hasLeft || !hasRight)
            {
                _lost += Time.deltaTime;
                if (_lost >= trackingLossGrace)
                {
                    EndHold("tracking lost");
                    return;
                }
                if (!hasLeft) left = _lastLeft;
                if (!hasRight) right = _lastRight;
            }
            else
            {
                _lost = 0f;
            }

            // Hands opened -> let go.
            // The box follows the midpoint of the hands, so pulling one hand away only
            // moves that hand half as far from the surface. The spread between the
            // hands reacts to the full movement, so it's the main release signal.
            float spread = Vector3.Distance(left.position, right.position) - _startSpread;
            float distL = hasLeft ? ClosestDistance(_jointsLeft) : 0f;
            float distR = hasRight ? ClosestDistance(_jointsRight) : 0f;
            if (spread > letGoSpread || distL > letGoDistance || distR > letGoDistance)
            {
                _separated += Time.deltaTime;
                if (_separated >= letGoDelay)
                {
                    EndHold($"hands opened (spread +{spread:0.000}m, L={distL:0.00}m R={distR:0.00}m)");
                    return;
                }
            }
            else
            {
                _separated = 0f;
            }

            _lastLeft = left;
            _lastRight = right;
            Send(_leftId, PointerEventType.Move, left);
            Send(_rightId, PointerEventType.Move, right);
        }

        // ------------------------------------------------------------------ begin / end

        private void BeginHold(Pose left, Pose right)
        {
            _held = true;
            _dwell = 0f;
            _separated = 0f;
            _lost = 0f;
            _lastLeft = left;
            _lastRight = right;
            _startSpread = Vector3.Distance(left.position, right.position);

            // A second Select would otherwise kick the first one out.
            _savedTransfer = grabbable.TransferOnSecondSelection;
            grabbable.TransferOnSecondSelection = false;

            Send(_leftId, PointerEventType.Hover, left);
            Send(_rightId, PointerEventType.Hover, right);
            Send(_leftId, PointerEventType.Select, left);
            Send(_rightId, PointerEventType.Select, right);

            // After Select, so ChairSnapZone has already un-frozen the box.
            if (kinematicWhileHeld && body != null)
            {
                _savedKinematic = body.isKinematic;
                _kinematicSaved = true;
                body.isKinematic = true;
            }

            Log("side-hold START");
            onSideHoldStart?.Invoke();
        }

        private void EndHold(string reason)
        {
            if (!_held) return;
            _held = false;
            _separated = 0f;
            _lost = 0f;

            // Before Unselect, so the Grabbable's throw velocity lands on a dynamic body
            // and ChairSnapZone caches the real (non-kinematic) state.
            if (_kinematicSaved && body != null)
            {
                body.isKinematic = _savedKinematic;
                _kinematicSaved = false;
            }

            if (grabbable != null)
            {
                Send(_leftId, PointerEventType.Unselect, _lastLeft);
                Send(_rightId, PointerEventType.Unselect, _lastRight);
                Send(_leftId, PointerEventType.Unhover, _lastLeft);
                Send(_rightId, PointerEventType.Unhover, _lastRight);
                grabbable.TransferOnSecondSelection = _savedTransfer;
            }

            Log($"side-hold END ({reason})");
            onSideHoldEnd?.Invoke();
        }

        private void Send(UniqueIdentifier id, PointerEventType type, Pose pose)
        {
            grabbable.ProcessPointerEvent(new PointerEvent(id.ID, type, pose));
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// Uses the hand point nearest to the box: which face it is on, and how far it is
        /// from the surface (0 when inside).
        /// </summary>
        private void Classify(List<Vector3> points, out int axis, out int sign, out float distance)
        {
            Vector3 world = points[0];
            distance = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                float d = SurfaceDistance(points[i]);
                if (d < distance)
                {
                    distance = d;
                    world = points[i];
                }
            }

            Vector3 local = box.transform.InverseTransformPoint(world) - box.center;
            Vector3 half = box.size * 0.5f;

            float nx = half.x > 1e-5f ? local.x / half.x : 0f;
            float ny = half.y > 1e-5f ? local.y / half.y : 0f;
            float nz = half.z > 1e-5f ? local.z / half.z : 0f;

            float ax = Mathf.Abs(nx), ay = Mathf.Abs(ny), az = Mathf.Abs(nz);
            if (ax >= ay && ax >= az) { axis = 0; sign = nx >= 0f ? 1 : -1; }
            else if (az >= ay) { axis = 2; sign = nz >= 0f ? 1 : -1; }
            else { axis = 1; sign = ny >= 0f ? 1 : -1; }
        }

        private float ClosestDistance(List<Vector3> points)
        {
            float best = float.MaxValue;
            for (int i = 0; i < points.Count; i++) best = Mathf.Min(best, SurfaceDistance(points[i]));
            return best;
        }

        private float SurfaceDistance(Vector3 world)
        {
            return (box.ClosestPoint(world) - world).magnitude;
        }

        private bool AxisAllowed(int axis)
        {
            switch (axis)
            {
                case 0: return useLocalXFaces;
                case 1: return useLocalYFaces;
                default: return useLocalZFaces;
            }
        }

        // ------------------------------------------------------------------ input sources

        private void RefreshSourcesIfNeeded()
        {
            if (Time.time < _nextRefresh && (_hands.Count > 0 || _controllers.Count > 0)) return;
            _nextRefresh = Time.time + RefreshSourcesEvery;

            _hands.Clear();
            _controllers.Clear();

            if (useHands)
            {
                foreach (Hand hand in FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    _hands.Add(hand);
                }
            }

            if (useControllers)
            {
                foreach (Controller controller in FindObjectsByType<Controller>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    _controllers.Add(controller);
                }
            }
        }

        /// <summary>
        /// Tracked hand: every joint position (fingers, knuckles, wrist) goes into <paramref name="points"/>,
        /// so contact is measured from the actual hand, not a single palm point. The grab pose is the
        /// wrist pose.
        /// Controller: its grip pose is the single point.
        /// </summary>
        private bool TryGetHand(Handedness handedness, List<Vector3> points, out Pose pose)
        {
            points.Clear();

            for (int i = 0; i < _hands.Count; i++)
            {
                IHand hand = _hands[i];
                if (hand == null || !(hand is MonoBehaviour mb) || mb == null || !mb.isActiveAndEnabled) continue;
                if (hand.Handedness != handedness || !hand.IsTrackedDataValid) continue;
                if (!hand.GetRootPose(out Pose root)) continue;
                if (!hand.GetJointPosesFromWrist(out var fromWrist)) continue;

                for (int j = 0; j < fromWrist.Count; j++)
                {
                    if (hand.GetJointPose((HandJointId)j, out Pose joint)) points.Add(joint.position);
                }

                if (points.Count == 0) continue;

                // Wrist: steady while the fingers move, so the box doesn't wobble when you squeeze.
                pose = root;
                return true;
            }

            for (int i = 0; i < _controllers.Count; i++)
            {
                IController controller = _controllers[i];
                if (controller == null || !(controller is MonoBehaviour mb) || mb == null || !mb.isActiveAndEnabled) continue;
                if (controller.Handedness != handedness || !controller.IsConnected) continue;
                if (!controller.TryGetPose(out pose)) continue;

                points.Add(pose.position);
                return true;
            }

            pose = Pose.identity;
            return false;
        }

        // ------------------------------------------------------------------ debug

        private void Log(string message)
        {
            if (enableDebugLogs) Debug.Log($"[TwoHandSideHold] {name}: {message}", this);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying || box == null) return;

            Gizmos.color = _held ? Color.green : new Color(1f, 0.6f, 0f);
            foreach (Vector3 p in _jointsLeft) Gizmos.DrawWireSphere(p, 0.01f);
            foreach (Vector3 p in _jointsRight) Gizmos.DrawWireSphere(p, 0.01f);
        }
    }
}
