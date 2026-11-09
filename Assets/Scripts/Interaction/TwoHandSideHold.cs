using System;
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
    /// both hands and drops as soon as the hands move apart.
    ///
    /// Put it on the same GameObject as the box's Grabbable + BoxCollider + Rigidbody.
    /// The normal pinch / palm / controller grab keeps working untouched.
    ///
    /// The side-hold is fed into the Grabbable as ordinary grab points
    /// (Hover/Select/Move/Unselect pointer events), so everything already
    /// listening to the Grabbable reacts as if it were a normal grab:
    /// ChairGrabState.IsHeld / HandsHolding, ChairSnapZone (snap on release),
    /// MaxStepTrigger / StoolStandZone held-gates, TutorialFlow "Touch Any".
    ///
    /// Also implements IInteractableView, so ItemUsageTracker logs side-holds as
    /// select / unselect rows like any other grab.
    ///
    /// Optional extras (ported from the TwoPalmSqueezeGrab proposal):
    ///  - open palm required (a fist pressed on the box does not grab);
    ///  - keep the box level while carried (no tilt when one hand is higher);
    ///  - the box never sinks into the floor / a table, and pushing it down onto
    ///    a support counts as "placed" and releases it;
    ///  - one-hand mode for patients with one working hand;
    ///  - after any release, wait until the box comes to rest, stand it upright if
    ///    it fell on its side, and freeze it so the player can step on it;
    ///  - events with a reason for every release and for touches that did not grab.
    /// </summary>
    [DisallowMultipleComponent]
    public class TwoHandSideHold : MonoBehaviour, IInteractableView
    {
        public enum HoldMode { TwoHands, OneHandLeft, OneHandRight, OneHandAny }

        [Serializable] public class StringEvent : UnityEvent<string> { }

        [Header("References (auto-filled from this GameObject)")]
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private BoxCollider box;
        [SerializeField] private Rigidbody body;

        [Header("Mode (set per patient)")]
        [Tooltip("TwoHands: palms on two opposite sides. OneHand*: a single open palm on a side face (for patients with one working hand). " +
                 "In one-hand mode the box only moves, never rotates, and is released by placing it on a support or by losing tracking.")]
        [SerializeField] private HoldMode mode = HoldMode.TwoHands;

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

        [Tooltip("Same for a one-hand hold. Longer, so an accidental touch does not count as a grab.")]
        [SerializeField] private float oneHandDwell = 0.3f;

        [Header("Open palm (tracked hands only; controllers always pass)")]
        [Tooltip("Only start the hold when the hand is open, so a fist pressed on the box does not grab it.")]
        [SerializeField] private bool requireOpenHand = true;

        [Tooltip("Fingertip-to-palm distance at which a finger counts as extended, in meters. Multiplied by the hand scale.")]
        [SerializeField] private float openFingerDistance = 0.065f;

        [Tooltip("How many of index / middle / ring must be extended. 1 is lenient and was the value that felt right on Quest 3.")]
        [SerializeField, Range(1, 3)] private int minOpenFingers = 1;

        [Header("Release (meters / seconds)")]
        [Tooltip("MAIN release (two hands): let go once the hands open this much wider than they were when the hold started. " +
                 "Smaller = lets go more easily.")]
        [SerializeField] private float letGoSpread = 0.035f;

        [Tooltip("Backup release (two hands): let go once either hand is this far from the box surface.")]
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

        [Tooltip("Keep the box level while carried: it turns with the hands around the vertical axis only, and does not tilt when one hand is higher.")]
        [SerializeField] private bool keepLevel = true;

        [Header("Support (floor, table, step)")]
        [Tooltip("While side-held, the box is lifted so it never sinks into whatever is underneath it.")]
        [SerializeField] private bool clampAboveSupport = true;

        [Tooltip("If the box already rests on a support and the hands keep pushing it down, it counts as placed and is released. Always on in one-hand mode.")]
        [SerializeField] private bool releaseWhenPlaced = true;

        [Tooltip("How far the hands must push the box into the support (meters) before it counts as placed.")]
        [SerializeField] private float placeReleaseDepth = 0.04f;

        [Tooltip("Layers that count as support. Exclude hand / player layers if they have colliders. Triggers are always ignored.")]
        [SerializeField] private LayerMask supportLayers = ~0;

        [Header("After release (any grab: side-hold, pinch or palm)")]
        [Tooltip("Once the box comes to rest after being released, freeze it in place (all rigidbody constraints) so the player can step on it. " +
                 "Grabbing it again unfreezes it. Skipped while ChairSnapZone holds the box.")]
        [SerializeField] private bool freezeWhenSettled = false;

        [Tooltip("Once the box comes to rest tilted or lying on its side, stand it upright in place, keeping its heading.")]
        [SerializeField] private bool standUpWhenSettled = false;

        [Tooltip("Speed below which the box counts as resting, m/s.")]
        [SerializeField] private float settleSpeed = 0.02f;

        [Tooltip("How long the box must rest before it is stood up / frozen, seconds.")]
        [SerializeField] private float settleTime = 0.5f;

        [Header("Events")]
        [SerializeField] private UnityEvent onSideHoldStart = new UnityEvent();
        [SerializeField] private UnityEvent onSideHoldEnd = new UnityEvent();

        [Tooltip("Raised with the reason on every side-hold release: hands_opened, placed_on_support, tracking_lost, handed_over, disabled.")]
        [SerializeField] private StringEvent onSideHoldReleased = new StringEvent();

        [Tooltip("Raised when a touch on the box ended without a hold. Reason: one_hand_only, hand_closed, not_opposite, from_top, wrong_hand.")]
        [SerializeField] private StringEvent onAttemptFailed = new StringEvent();

        [Tooltip("A touch shorter than this does not count as a failed attempt, seconds.")]
        [SerializeField] private float attemptMinTime = 0.2f;

        [Tooltip("Raised when the box has come to rest after a release and was stood up / frozen.")]
        [SerializeField] private UnityEvent onSettled = new UnityEvent();

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;
        [SerializeField] private bool drawGizmos = true;

        /// <summary>True while the box is held by pressing on its sides.</summary>
        public bool IsSideHeld => _held;

        /// <summary>True while the box is frozen in place after settling.</summary>
        public bool IsFrozen => _frozen;

        /// <summary>Reason of the last touch that did not become a hold.</summary>
        public string LastFailReason { get; private set; } = "";

        public HoldMode Mode { get => mode; set => mode = value; }

        private bool IsOneHandMode => mode != HoldMode.TwoHands;

        public StringEvent OnSideHoldReleased => onSideHoldReleased;
        public StringEvent OnAttemptFailed => onAttemptFailed;

        private const float RefreshSourcesEvery = 2f;

        private readonly List<IHand> _hands = new List<IHand>();
        private readonly List<Vector3> _jointsLeft = new List<Vector3>(32);
        private readonly List<Vector3> _jointsRight = new List<Vector3>(32);
        private readonly List<IController> _controllers = new List<IController>();
        private float _nextRefresh;

        private UniqueIdentifier _leftId;
        private UniqueIdentifier _rightId;

        private bool _held;
        private bool _usesLeft;
        private bool _usesRight;
        private int _otherPointsAtStart;
        private float _dwell;
        private float _separated;
        private float _startSpread;
        private float _lost;
        private Pose _lastLeft;
        private Pose _lastRight;

        // Pose bookkeeping for keepLevel / one-hand: the box relative to a yaw-only frame between the hands.
        private Vector3 _localPos;
        private Quaternion _localRot;
        private Quaternion _lastFrameRot = Quaternion.identity;
        private Vector3 _oneHandOffset;
        private Quaternion _oneHandRot;

        private bool _savedKinematic;
        private bool _kinematicSaved;
        private bool _savedTransfer;

        private bool _attemptActive;
        private float _attemptTimer;
        private string _attemptReason = "";

        private bool _watchSettle;
        private float _settleTimer;
        private bool _frozen;
        private RigidbodyConstraints _savedConstraints;

        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private readonly Vector3[] _topPoints = new Vector3[5];

        // ------------------------------------------------------------------ IInteractableView (for ItemUsageTracker)

        private static readonly IInteractorView[] NoInteractors = new IInteractorView[0];
        private InteractableState _state = InteractableState.Normal;

        public object Data => this;
        public InteractableState State => _state;
        public event Action<InteractableStateChangeArgs> WhenStateChanged = delegate { };
        public int MaxInteractors => 2;
        public int MaxSelectingInteractors => 2;
        public IEnumerable<IInteractorView> InteractorViews => NoInteractors;
        public IEnumerable<IInteractorView> SelectingInteractorViews => NoInteractors;
        public event Action<IInteractorView> WhenInteractorViewAdded { add { } remove { } }
        public event Action<IInteractorView> WhenInteractorViewRemoved { add { } remove { } }
        public event Action<IInteractorView> WhenSelectingInteractorViewAdded { add { } remove { } }
        public event Action<IInteractorView> WhenSelectingInteractorViewRemoved { add { } remove { } }

        private void SetState(InteractableState newState)
        {
            if (_state == newState) return;
            InteractableState previous = _state;
            _state = newState;
            WhenStateChanged(new InteractableStateChangeArgs(previous, newState));
        }

        // ------------------------------------------------------------------ lifecycle

        private void Reset() => AutoFill();

        private void Awake()
        {
            AutoFill();
            _leftId = UniqueIdentifier.Generate();
            _rightId = UniqueIdentifier.Generate();
        }

        private void OnEnable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised += HandleGrabbableEvent;
        }

        private void OnDisable()
        {
            if (_held) EndHold("disabled");
            if (grabbable != null) grabbable.WhenPointerEventRaised -= HandleGrabbableEvent;
            Unfreeze();
            _watchSettle = false;
            _dwell = 0f;
            _attemptActive = false;
            SetState(InteractableState.Normal);
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
                if (_held) EndHold("disabled");
                return;
            }

            RefreshSourcesIfNeeded();

            bool hasLeft = TryGetHand(Handedness.Left, _jointsLeft, out Pose left, out IHand leftHand);
            bool hasRight = TryGetHand(Handedness.Right, _jointsRight, out Pose right, out IHand rightHand);

            if (_held)
            {
                UpdateHold(hasLeft, left, hasRight, right);
            }
            else
            {
                UpdateIdle(hasLeft, left, leftHand, hasRight, right, rightHand);
                UpdateSettle();
            }
        }

        private void UpdateIdle(bool hasLeft, Pose left, IHand leftHand, bool hasRight, Pose right, IHand rightHand)
        {
            if (skipWhenAlreadyGrabbed && grabbable.SelectingPointsCount > 0)
            {
                _dwell = 0f;
                _attemptActive = false;
                _attemptReason = "";
                return;
            }

            int axisL = -1, signL = 0, axisR = -1, signR = 0;
            float distL = float.MaxValue, distR = float.MaxValue;
            if (hasLeft) Classify(_jointsLeft, out axisL, out signL, out distL);
            if (hasRight) Classify(_jointsRight, out axisR, out signR, out distR);

            bool touchL = hasLeft && distL <= startDistance;
            bool touchR = hasRight && distR <= startDistance;

            bool can;
            string reason;
            bool useLeft, useRight;

            if (!IsOneHandMode)
            {
                can = CheckTwoHands(touchL, touchR, axisL, signL, axisR, signR, leftHand, rightHand, out reason);
                useLeft = useRight = can;
            }
            else
            {
                can = CheckOneHand(touchL, touchR, axisL, axisR, leftHand, rightHand, out reason, out useLeft, out useRight);
            }

            TrackAttempt(touchL || touchR, can, reason);

            if (!can)
            {
                _dwell = 0f;
                return;
            }

            _dwell += Time.deltaTime;
            float need = IsOneHandMode ? oneHandDwell : startDwell;
            if (_dwell >= need) BeginHold(useLeft, useRight, left, right);
        }

        private bool CheckTwoHands(bool touchL, bool touchR, int axisL, int signL, int axisR, int signR,
                                   IHand leftHand, IHand rightHand, out string reason)
        {
            reason = null;
            if (!touchL && !touchR) return false;
            if (!touchL || !touchR) { reason = "one_hand_only"; return false; }
            if (!AxisAllowed(axisL) || !AxisAllowed(axisR)) { reason = "from_top"; return false; }
            if (axisL != axisR || signL != -signR) { reason = "not_opposite"; return false; }
            if (!IsHandOpen(leftHand) || !IsHandOpen(rightHand)) { reason = "hand_closed"; return false; }
            return true;
        }

        private bool CheckOneHand(bool touchL, bool touchR, int axisL, int axisR, IHand leftHand, IHand rightHand,
                                  out string reason, out bool useLeft, out bool useRight)
        {
            reason = null;
            useLeft = false;
            useRight = false;

            switch (mode)
            {
                case HoldMode.OneHandLeft:
                    if (touchR && !touchL) { reason = "wrong_hand"; return false; }
                    useLeft = OneHandOk(touchL, axisL, leftHand, ref reason);
                    return useLeft;

                case HoldMode.OneHandRight:
                    if (touchL && !touchR) { reason = "wrong_hand"; return false; }
                    useRight = OneHandOk(touchR, axisR, rightHand, ref reason);
                    return useRight;

                default: // OneHandAny
                    if (OneHandOk(touchL, axisL, leftHand, ref reason)) { useLeft = true; return true; }
                    if (OneHandOk(touchR, axisR, rightHand, ref reason)) { useRight = true; return true; }
                    return false;
            }
        }

        private bool OneHandOk(bool touching, int axis, IHand hand, ref string reason)
        {
            if (!touching) return false;
            if (!AxisAllowed(axis)) { reason = "from_top"; return false; }
            if (!IsHandOpen(hand)) { reason = "hand_closed"; return false; }
            return true;
        }

        /// <summary>A touch that ended without a hold is reported once, with the last reason it failed.</summary>
        private void TrackAttempt(bool touching, bool can, string reason)
        {
            if (touching)
            {
                if (!_attemptActive)
                {
                    _attemptActive = true;
                    _attemptTimer = 0f;
                    _attemptReason = "";
                }
                _attemptTimer += Time.deltaTime;
                if (!can && reason != null) _attemptReason = reason;
                return;
            }

            if (!_attemptActive) return;
            _attemptActive = false;

            if (_attemptTimer >= attemptMinTime && !string.IsNullOrEmpty(_attemptReason))
            {
                LastFailReason = _attemptReason;
                Log($"attempt failed ({_attemptReason})");
                onAttemptFailed?.Invoke(_attemptReason);
            }
            _attemptReason = "";
        }

        private void UpdateHold(bool hasLeft, Pose left, bool hasRight, Pose right)
        {
            // A pinch / palm grab joined in: let it take over.
            int ours = (_usesLeft ? 1 : 0) + (_usesRight ? 1 : 0);
            if (grabbable.SelectingPointsCount > ours + _otherPointsAtStart)
            {
                EndHold("handed_over");
                return;
            }

            // Lost tracking: hold the last pose for a moment instead of dropping instantly.
            bool missing = (_usesLeft && !hasLeft) || (_usesRight && !hasRight);
            if (missing)
            {
                _lost += Time.deltaTime;
                if (_lost >= trackingLossGrace)
                {
                    EndHold("tracking_lost");
                    return;
                }
                if (!hasLeft) left = _lastLeft;
                if (!hasRight) right = _lastRight;
            }
            else
            {
                _lost = 0f;
            }

            if (_usesLeft && _usesRight)
            {
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
                        Log($"hands opened (spread +{spread:0.000}m, L={distL:0.00}m R={distR:0.00}m)");
                        EndHold("hands_opened");
                        return;
                    }
                }
                else
                {
                    _separated = 0f;
                }
            }

            _lastLeft = left;
            _lastRight = right;

            bool two = _usesLeft && _usesRight;
            if (_usesLeft) Send(_leftId, PointerEventType.Move, PointerPose(left, two));
            if (_usesRight) Send(_rightId, PointerEventType.Move, PointerPose(right, two));

            // The Grabbable's transformer has moved the box. Override it where our options ask for it.
            if (two)
            {
                if (keepLevel)
                {
                    Pose frame = LevelFrame(left.position, right.position);
                    transform.SetPositionAndRotation(frame.position + frame.rotation * _localPos, frame.rotation * _localRot);
                }
            }
            else
            {
                Vector3 hand = _usesLeft ? left.position : right.position;
                transform.SetPositionAndRotation(hand + _oneHandOffset, _oneHandRot);
            }

            // Keep the box above its support. If the hands push deeper, the box has been placed.
            if (clampAboveSupport || releaseWhenPlaced || IsOneHandMode)
            {
                float lift = SupportLift();
                if (lift > 0f && (clampAboveSupport || IsOneHandMode)) transform.position += Vector3.up * lift;
                if ((releaseWhenPlaced || IsOneHandMode) && lift > placeReleaseDepth) EndHold("placed_on_support");
            }
        }

        /// <summary>One-hand holds send a fixed rotation, so the transformer only translates the box.</summary>
        private Pose PointerPose(Pose hand, bool twoHands)
        {
            return twoHands ? hand : new Pose(hand.position, _oneHandRot);
        }

        // ------------------------------------------------------------------ begin / end

        private void BeginHold(bool useLeft, bool useRight, Pose left, Pose right)
        {
            _held = true;
            _usesLeft = useLeft;
            _usesRight = useRight;
            _dwell = 0f;
            _separated = 0f;
            _lost = 0f;
            _lastLeft = left;
            _lastRight = right;
            _attemptActive = false;
            _attemptReason = "";

            if (useLeft && useRight)
            {
                _startSpread = Vector3.Distance(left.position, right.position);
                Pose frame = LevelFrame(left.position, right.position);
                Quaternion inv = Quaternion.Inverse(frame.rotation);
                _localPos = inv * (transform.position - frame.position);
                _localRot = inv * transform.rotation;
            }
            else
            {
                Vector3 hand = useLeft ? left.position : right.position;
                _oneHandOffset = transform.position - hand;
                _oneHandRot = transform.rotation;
            }

            // A second Select would otherwise kick the first one out.
            _savedTransfer = grabbable.TransferOnSecondSelection;
            grabbable.TransferOnSecondSelection = false;

            // Select unfreezes the box (HandleGrabbableEvent / ChairSnapZone) before we go kinematic.
            bool two = useLeft && useRight;
            if (useLeft)
            {
                Send(_leftId, PointerEventType.Hover, PointerPose(left, two));
                Send(_leftId, PointerEventType.Select, PointerPose(left, two));
            }
            if (useRight)
            {
                Send(_rightId, PointerEventType.Hover, PointerPose(right, two));
                Send(_rightId, PointerEventType.Select, PointerPose(right, two));
            }
            _otherPointsAtStart = grabbable.SelectingPointsCount - (two ? 2 : 1);

            // After Select, so ChairSnapZone has already un-frozen the box.
            if (kinematicWhileHeld && body != null)
            {
                _savedKinematic = body.isKinematic;
                _kinematicSaved = true;
                body.isKinematic = true;
            }

            SetState(InteractableState.Select);
            Log($"side-hold START ({(useLeft && useRight ? "two hands" : useLeft ? "left hand" : "right hand")})");
            onSideHoldStart?.Invoke();
        }

        private void EndHold(string reason)
        {
            if (!_held) return;
            _held = false;
            _separated = 0f;
            _lost = 0f;

            bool usedLeft = _usesLeft;
            bool usedRight = _usesRight;
            _usesLeft = false;
            _usesRight = false;

            // Before Unselect, so the Grabbable's throw velocity lands on a dynamic body
            // and ChairSnapZone caches the real (non-kinematic) state.
            if (_kinematicSaved && body != null)
            {
                body.isKinematic = _savedKinematic;
                _kinematicSaved = false;
            }

            if (grabbable != null)
            {
                // Unselect makes the transformer do one last move, which would undo
                // keepLevel / the support clamp. Put the pose we chose back afterwards,
                // unless another grab now owns the box.
                transform.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);

                bool two = usedLeft && usedRight;
                if (usedLeft) Send(_leftId, PointerEventType.Unselect, PointerPose(_lastLeft, two));
                if (usedRight) Send(_rightId, PointerEventType.Unselect, PointerPose(_lastRight, two));
                if (usedLeft) Send(_leftId, PointerEventType.Unhover, PointerPose(_lastLeft, two));
                if (usedRight) Send(_rightId, PointerEventType.Unhover, PointerPose(_lastRight, two));
                grabbable.TransferOnSecondSelection = _savedTransfer;

                if (grabbable.SelectingPointsCount == 0 && !SnapZoneHoldsBox())
                    transform.SetPositionAndRotation(pos, rot);
            }

            SetState(InteractableState.Normal);
            Log($"side-hold END ({reason})");
            onSideHoldEnd?.Invoke();
            onSideHoldReleased?.Invoke(reason);
        }

        /// <summary>
        /// ChairSnapZone snaps and freezes the box on Unselect (kinematic + FreezeAll).
        /// When it did, its pose wins. Our own freeze is always lifted on Select, so it can't be ours.
        /// </summary>
        private bool SnapZoneHoldsBox()
        {
            return body != null && body.isKinematic && body.constraints == RigidbodyConstraints.FreezeAll;
        }

        private void Send(UniqueIdentifier id, PointerEventType type, Pose pose)
        {
            grabbable.ProcessPointerEvent(new PointerEvent(id.ID, type, pose));
        }

        // ------------------------------------------------------------------ settle / freeze after release

        /// <summary>Sees every grab on the box (side-hold, pinch, palm, controller).</summary>
        private void HandleGrabbableEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    _watchSettle = false;
                    Unfreeze();
                    break;

                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    if (grabbable.SelectingPointsCount == 0 && (freezeWhenSettled || standUpWhenSettled))
                    {
                        _watchSettle = true;
                        _settleTimer = 0f;
                    }
                    break;
            }
        }

        private void UpdateSettle()
        {
            if (!_watchSettle || body == null) return;

            // Kinematic after release = something else (ChairSnapZone) already placed it.
            if (body.isKinematic || grabbable.SelectingPointsCount > 0)
            {
                _watchSettle = false;
                return;
            }

            bool still = body.linearVelocity.magnitude < settleSpeed
                         && body.angularVelocity.magnitude < settleSpeed * 10f;

            _settleTimer = still ? _settleTimer + Time.deltaTime : 0f;
            if (_settleTimer < settleTime) return;

            _watchSettle = false;

            if (standUpWhenSettled && Vector3.Angle(transform.up, Vector3.up) > 0.1f)
                StandUpright();

            if (freezeWhenSettled) Freeze();

            Log($"settled{(standUpWhenSettled ? ", upright" : "")}{(freezeWhenSettled ? ", frozen" : "")}");
            onSettled?.Invoke();
        }

        /// <summary>
        /// Freezes with constraints rather than isKinematic: the Grabbable restores isKinematic
        /// to whatever it saw at grab time, so a kinematic freeze would leave the box floating after the next grab.
        /// </summary>
        private void Freeze()
        {
            if (_frozen || body == null) return;
            _savedConstraints = body.constraints;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.Sleep();
            _frozen = true;
        }

        private void Unfreeze()
        {
            if (!_frozen) return;
            _frozen = false;
            if (body == null) return;
            body.constraints = _savedConstraints;
            body.WakeUp();
        }

        /// <summary>
        /// Rotates the box upright around its collider center, keeping its heading,
        /// then lifts it out of the support so it rests on its bottom face at the same spot.
        /// </summary>
        private void StandUpright()
        {
            Vector3 centerBefore = BoxCenter();

            // Keep the current heading: use whichever local axis is still horizontal.
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.right, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            transform.position += centerBefore - BoxCenter();

            float lift = SupportLift();
            if (lift > 0f) transform.position += Vector3.up * lift;

            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ geometry

        private Vector3 BoxCenter() => box.transform.TransformPoint(box.center);

        /// <summary>A frame at the hands' midpoint that turns with them around the vertical axis only.</summary>
        private Pose LevelFrame(Vector3 leftPos, Vector3 rightPos)
        {
            Vector3 mid = (leftPos + rightPos) * 0.5f;
            Vector3 dir = rightPos - leftPos;
            dir.y = 0f;

            // Hands almost one above the other: keep the previous heading so the box doesn't spin.
            if (dir.sqrMagnitude > 0.0004f)
                _lastFrameRot = Quaternion.LookRotation(dir.normalized, Vector3.up);

            return new Pose(mid, _lastFrameRot);
        }

        /// <summary>
        /// How far the box must be lifted so it does not sink into a support. 0 if nothing is underneath.
        /// Rays go down from five points of the top face (center and corners), so an edge resting on a step counts too.
        /// </summary>
        private float SupportLift()
        {
            const float inset = 0.01f;
            const float start = 0.005f;

            Transform bt = box.transform;
            Vector3 c = box.center;
            Vector3 half = box.size * 0.5f;
            float hx = Mathf.Max(half.x - inset, 0f);
            float hz = Mathf.Max(half.z - inset, 0f);

            Vector3 topCenter = bt.TransformPoint(c + new Vector3(0f, half.y, 0f));
            float height = Vector3.Distance(topCenter, bt.TransformPoint(c - new Vector3(0f, half.y, 0f)));

            _topPoints[0] = topCenter;
            _topPoints[1] = bt.TransformPoint(c + new Vector3(hx, half.y, hz));
            _topPoints[2] = bt.TransformPoint(c + new Vector3(-hx, half.y, hz));
            _topPoints[3] = bt.TransformPoint(c + new Vector3(hx, half.y, -hz));
            _topPoints[4] = bt.TransformPoint(c + new Vector3(-hx, half.y, -hz));

            float lift = 0f;
            for (int i = 0; i < _topPoints.Length; i++)
            {
                int n = Physics.RaycastNonAlloc(_topPoints[i] + Vector3.up * start, Vector3.down, _hits,
                                                height + start, supportLayers, QueryTriggerInteraction.Ignore);

                float nearest = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    Collider col = _hits[k].collider;
                    if (col == null) continue;
                    if ((body != null && col.attachedRigidbody == body) || col.transform.IsChildOf(transform)) continue;
                    if (_hits[k].distance < nearest) nearest = _hits[k].distance;
                }
                if (nearest == float.MaxValue) continue;

                float fromTop = nearest - start;
                if (fromTop < height) lift = Mathf.Max(lift, height - fromTop);
            }
            return lift;
        }

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
                case 2: return useLocalZFaces;
                default: return false;
            }
        }

        /// <summary>Controllers (hand == null) always count as open.</summary>
        private bool IsHandOpen(IHand hand)
        {
            if (!requireOpenHand || hand == null) return true;
            if (!hand.GetJointPose(HandJointId.HandPalm, out Pose palm)) return true;

            float threshold = openFingerDistance * Mathf.Max(hand.Scale, 0.5f);
            int open = 0;
            if (IsFingerOpen(hand, HandJointId.HandIndexTip, palm.position, threshold)) open++;
            if (IsFingerOpen(hand, HandJointId.HandMiddleTip, palm.position, threshold)) open++;
            if (IsFingerOpen(hand, HandJointId.HandRingTip, palm.position, threshold)) open++;
            return open >= minOpenFingers;
        }

        private static bool IsFingerOpen(IHand hand, HandJointId tip, Vector3 palm, float threshold)
        {
            return hand.GetJointPose(tip, out Pose tipPose)
                   && Vector3.Distance(tipPose.position, palm) > threshold;
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
        /// Controller: its grip pose is the single point, and <paramref name="trackedHand"/> is null.
        /// </summary>
        private bool TryGetHand(Handedness handedness, List<Vector3> points, out Pose pose, out IHand trackedHand)
        {
            points.Clear();
            trackedHand = null;

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
                trackedHand = hand;
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

            Gizmos.color = _held ? Color.green : (_frozen ? Color.cyan : new Color(1f, 0.6f, 0f));
            foreach (Vector3 p in _jointsLeft) Gizmos.DrawWireSphere(p, 0.01f);
            foreach (Vector3 p in _jointsRight) Gizmos.DrawWireSphere(p, 0.01f);
        }
    }
}
