using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Interaction;
using Oculus.Interaction.Input;

/// Grabbing a large object with open palms, without pinch or fist gestures.
///
/// Two-hand mode: palms pressed against opposite sides of the object.
/// One-hand mode: one open palm pressed against a side face (for patients with a single working hand).
///
/// Release:
///   • palms spread apart (two-hand mode only);
///   • object placed: it already rests on a support (floor, table, step) and the hands keep pushing it down (both modes);
///   • hand tracking lost for too long (both modes).
///
/// While held, the object never sinks below the floor or a table: it is clamped to the support underneath.
/// After release the object falls, comes to rest, is stood upright if it landed on its side, and frozen, so the user can stand on it.
/// Palm grabs work only on an upright object; a lying object can still be taken with the Meta grabs.
///
/// Visual hands: while held, the visible palm stays on the object's face even when the real hand pushes inside.
/// Option A: assign the SyntheticHand that drives the visible ISDK hand model.
/// Option B: assign the root transform of the visible hand model (e.g. the Hand Tracking building block);
/// it is shifted while holding and restored on release.
///
/// Put this on the object that has the Rigidbody. The collider may be on the same object or on a child.
///
/// Coexistence with Meta grabs (pinch / palm via HandGrabInteractable, GrabInteractable):
///   • while any other interactable on this object is selecting, this script stands aside;
///   • if a Meta grab starts while the object is held by palms, the palm grab hands over to it;
///   • after a Meta release the object is allowed to settle and freeze exactly like after a palm release.
///
/// Compatibility with the project's logging:
///   • implements IInteractableView, so ItemUsageTracker treats it like a regular Meta interactable
///     and logs select / unselect by itself; floor drops are still detected through the Rigidbody;
///   • if a ChairGrabState is present on the object, its OnChairGrabbed / OnChairUngrabbed are called automatically.
///
/// Extra logging hooks: a failed-attempt event with a reason, and a reason for every release.
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(1000)]
public class TwoPalmSqueezeGrab : MonoBehaviour, IInteractableView
{
    public enum GrabMode { TwoHands, OneHandLeft, OneHandRight, OneHandAny }

    [System.Serializable] public class StringEvent : UnityEvent<string> { }

    [Header("Grab mode (set per patient)")]
    [SerializeField] private GrabMode _mode = GrabMode.TwoHands;

    [Header("Hands (objects with a Hand component)")]
    [SerializeField, Interface(typeof(IHand))] private UnityEngine.Object _leftHand;
    [SerializeField, Interface(typeof(IHand))] private UnityEngine.Object _rightHand;

    [Header("Visual hands (optional). Use A or B, whichever renders the hands you see")]
    [Tooltip("Option A: the SyntheticHand that drives the visible ISDK hand model. When set, the visible palm stays on the object's face even when the real hand goes inside")]
    [SerializeField] private SyntheticHand _leftSyntheticHand;

    [Tooltip("Option A: the SyntheticHand that drives the visible ISDK hand model, right hand")]
    [SerializeField] private SyntheticHand _rightSyntheticHand;

    [Tooltip("Option B: the root object of the visible left hand model (for example '[BuildingBlock] Hand Tracking left'). While the object is held, this transform is shifted so the palm stays on the face, and restored on release")]
    [SerializeField] private Transform _leftHandVisualRoot;

    [Tooltip("Option B: the root object of the visible right hand model")]
    [SerializeField] private Transform _rightHandVisualRoot;

    [Tooltip("Gap between the palm joint and the object's face, in meters: roughly half the thickness of a hand")]
    [SerializeField] private float _handSurfaceGap = 0.015f;

    [Tooltip("Maximum offset of the visual hand from the real hand, in meters")]
    [SerializeField] private float _maxVisualShift = 0.12f;

    [Header("Object collider (empty = taken from this object or a child)")]
    [Tooltip("BoxCollider, SphereCollider, CapsuleCollider or convex MeshCollider. May live on a child object")]
    [SerializeField] private Collider _cubeCollider;

    [Header("Grab conditions")]
    [Tooltip("How close the palm joint must be to the object's surface, in meters")]
    [SerializeField] private float _contactDistance = 0.04f;

    [Tooltip("Fingertip-to-palm distance at which a finger counts as extended, in meters. Multiplied by the SDK hand scale automatically")]
    [SerializeField] private float _openFingerDistance = 0.065f;

    [Tooltip("How many of the three fingers (index, middle, ring) must be extended")]
    [SerializeField, Range(1, 3)] private int _minOpenFingers = 2;

    [Tooltip("Grab only while hand tracking is high confidence. Turn off if grabs trigger too rarely")]
    [SerializeField] private bool _requireHighConfidence = true;

    [Tooltip("How opposite the palms must be (in the horizontal plane): -1 exactly opposite, 0 at a right angle")]
    [SerializeField, Range(-1f, 0f)] private float _oppositeThreshold = -0.5f;

    [Tooltip("Minimum horizontal distance from the object's center to the palm, in meters. Rejects grabs from the top or bottom")]
    [SerializeField] private float _minSideOffset = 0.05f;

    [Tooltip("How many seconds the conditions must hold continuously before a two-hand grab triggers")]
    [SerializeField] private float _grabConfirmTime = 0.1f;

    [Tooltip("Same for a one-hand grab. Longer, so an accidental touch does not count as a grab")]
    [SerializeField] private float _oneHandConfirmTime = 0.3f;

    [Header("Release")]
    [Tooltip("How much farther apart the palms must move compared to the grab moment for the object to drop, in meters (two-hand mode only)")]
    [SerializeField] private float _releaseExtraDistance = 0.08f;

    [Tooltip("How many seconds the palms must stay spread before the object drops. Protects against tracking jitter")]
    [SerializeField] private float _releaseConfirmTime = 0.1f;

    [Tooltip("If the object already rests on a support and the hands keep pushing it deeper than this distance, in meters, it counts as placed")]
    [SerializeField] private float _placeReleaseDepth = 0.04f;

    [Tooltip("How many seconds a hand may be lost from tracking before the object drops")]
    [SerializeField] private float _trackingLossGrace = 0.4f;

    [Tooltip("Pause after release during which a new grab is impossible, in seconds")]
    [SerializeField] private float _regrabCooldown = 0.3f;

    [Header("Support (floor, table, step)")]
    [Tooltip("Layers that count as support. Exclude hand and player layers if they have colliders. Triggers are always ignored")]
    [SerializeField] private LayerMask _supportLayers = ~0;

    [Header("After placement")]
    [Tooltip("Freeze the object once it comes to rest after release")]
    [SerializeField] private bool _freezeWhenSettled = true;

    [Tooltip("Speed below which the object counts as stationary, m/s")]
    [SerializeField] private float _settleSpeed = 0.02f;

    [Tooltip("How many seconds the object must stay stationary before freezing")]
    [SerializeField] private float _settleTime = 0.5f;

    [Tooltip("Tilt up to this angle still counts as upright, in degrees. A tilted object is leveled on freeze; palm grabs require an upright object")]
    [SerializeField] private float _uprightMaxAngle = 10f;

    [Tooltip("When the object comes to rest lying on its side (or upside down), stand it upright in place so the user can step on it")]
    [SerializeField] private bool _standUpWhenSettled = true;

    [Header("Attempts (for logs)")]
    [Tooltip("A touch shorter than this does not count as an attempt, in seconds")]
    [SerializeField] private float _attemptMinTime = 0.2f;

    [Header("Events")]
    public UnityEvent OnGrabbed;

    [Tooltip("Reason: palms_spread, placed_on_support, tracking_lost, handed_over (a Meta grab took over), disabled")]
    public StringEvent OnReleased;

    public UnityEvent OnSettled;

    [Tooltip("Touch without a grab. Reason: not_upright, one_hand_only, hand_closed, low_confidence, from_top, not_opposite, wrong_hand")]
    public StringEvent OnAttemptFailed;

    public bool IsHeld => _held;
    public bool IsSettled => _settled;
    public bool IsUpright => Vector3.Angle(transform.up, Vector3.up) <= Mathf.Max(_uprightMaxAngle, 1f);
    public GrabMode Mode { get => _mode; set => _mode = value; }
    public string LastFailReason { get; private set; } = "";

    // ---------- IInteractableView: ItemUsageTracker subscribes to WhenStateChanged and logs select / unselect ----------

    private static readonly IInteractorView[] NoInteractors = new IInteractorView[0];
    private InteractableState _state = InteractableState.Normal;

    public object Data => this;
    public InteractableState State => _state;
    public event Action<InteractableStateChangeArgs> WhenStateChanged = delegate { };
    public int MaxInteractors => 2;
    public int MaxSelectingInteractors => 2;
    public IEnumerable<IInteractorView> InteractorViews => NoInteractors;
    public IEnumerable<IInteractorView> SelectingInteractorViews => NoInteractors;
    public event Action<IInteractorView> WhenInteractorViewAdded = delegate { };
    public event Action<IInteractorView> WhenInteractorViewRemoved = delegate { };
    public event Action<IInteractorView> WhenSelectingInteractorViewAdded = delegate { };
    public event Action<IInteractorView> WhenSelectingInteractorViewRemoved = delegate { };

    private void SetState(InteractableState newState)
    {
        if (_state == newState) return;
        InteractableState previous = _state;
        _state = newState;
        WhenStateChanged(new InteractableStateChangeArgs(previous, newState));
    }

    // ---------- Internal state ----------

    private IHand LeftHand;
    private IHand RightHand;
    private Rigidbody _rb;
    private BoxCollider _box;
    private CognitiveVR.Tasks.ChairGrabState _chairGrabState;

    private bool _held;
    private bool _holdLeft;
    private bool _holdRight;
    private float _conditionTimer;
    private float _grabPalmDistance;
    private Vector3 _localPos;
    private Quaternion _localRot;
    private Vector3 _oneHandOffset;
    private Quaternion _lastFrameRotation = Quaternion.identity;
    private float _trackingLostTimer;
    private float _releaseTimer;

    private float _cooldownTimer;
    private bool _waitingForHandsToLeave;

    private bool _attemptActive;
    private float _attemptTimer;
    private string _attemptReason = "";

    private bool _waitingToSettle;
    private bool _settled;
    private float _settleTimer;

    private bool _leftVisualLocked;
    private bool _rightVisualLocked;

    // Bookkeeping for Option B: what shift we applied to a visual root, so it can be undone or re-based safely
    private struct VisualShift
    {
        public bool active;
        public Vector3 applied;   // shift in the root's parent space
        public Vector3 lastSet;   // local position we wrote last time
    }

    private VisualShift _leftRootShift;
    private VisualShift _rightRootShift;

    // Other Meta interactables on this object (HandGrabInteractable, GrabInteractable...). Found once, checked every frame.
    private readonly List<IInteractableView> _otherViews = new List<IInteractableView>();
    private bool _otherWasSelecting;

    private readonly RaycastHit[] _hits = new RaycastHit[8];
    private readonly Vector3[] _topPoints = new Vector3[5];

    private void Awake()
    {
        LeftHand = _leftHand as IHand;
        RightHand = _rightHand as IHand;
        _rb = GetComponent<Rigidbody>();
        _chairGrabState = GetComponent<CognitiveVR.Tasks.ChairGrabState>();

        if (_cubeCollider == null) _cubeCollider = GetComponent<Collider>();
        if (_cubeCollider == null) _cubeCollider = GetComponentInChildren<Collider>();
        _box = _cubeCollider as BoxCollider;

        // Collect the Meta interactables on this object and its children, including disabled ones
        // (they may be enabled later). Their active state is checked at use time.
        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb is IInteractableView view && !ReferenceEquals(view, this))
                _otherViews.Add(view);
        }

        if (LeftHand == null || RightHand == null)
            Debug.LogWarning($"[TwoPalmSqueezeGrab] Hands are not assigned on '{name}'.");
        if (_cubeCollider == null)
            Debug.LogWarning($"[TwoPalmSqueezeGrab] No collider found on '{name}'.");
        else if (_cubeCollider.isTrigger)
            Debug.LogWarning($"[TwoPalmSqueezeGrab] Collider '{_cubeCollider.name}' is a trigger: the object will not rest on the floor.");
    }

    private void OnDisable()
    {
        if (_held) Release("disabled");
        FreeVisualHands();
        SetState(InteractableState.Normal);
    }

    private void Update()
    {
        // A Meta grab (pinch / palm) owns the object: stand aside until it lets go
        bool otherSelecting = OtherInteractableSelecting();
        if (otherSelecting)
        {
            if (_held) Release("handed_over", restorePhysics: false);
            _otherWasSelecting = true;
            _conditionTimer = 0f;
            _attemptActive = false;
            _attemptReason = "";
            _waitingToSettle = false;
            SetState(InteractableState.Normal);
            return;
        }

        if (_otherWasSelecting)
        {
            // The Meta grab just let go: treat it like our own release
            _otherWasSelecting = false;
            _rb.isKinematic = false;
            _cooldownTimer = _regrabCooldown;
            _waitingForHandsToLeave = true;
            _waitingToSettle = _freezeWhenSettled;
            _settleTimer = 0f;
            _settled = false;
        }

        bool haveLeft = TryGetPalm(LeftHand, out Pose left);
        bool haveRight = TryGetPalm(RightHand, out Pose right);

        if (_held)
        {
            UpdateHeld(haveLeft, haveRight, left, right);
            return;
        }

        UpdateNotHeld(haveLeft, haveRight, left, right);
        UpdateSettle();
    }

    private bool OtherInteractableSelecting()
    {
        for (int i = 0; i < _otherViews.Count; i++)
        {
            IInteractableView view = _otherViews[i];
            MonoBehaviour mb = view as MonoBehaviour;
            if (mb == null || !mb.isActiveAndEnabled) continue;
            if (view.State == InteractableState.Select) return true;
        }
        return false;
    }

    // Visual hands are updated after every other script in the frame, so nothing else can undo the lock
    private void LateUpdate()
    {
        if (_held)
        {
            if (_holdLeft) PinVisualHand(LeftHand, _leftSyntheticHand, _leftHandVisualRoot, ref _leftVisualLocked, ref _leftRootShift);
            if (_holdRight) PinVisualHand(RightHand, _rightSyntheticHand, _rightHandVisualRoot, ref _rightVisualLocked, ref _rightRootShift);
        }
        else
        {
            FreeVisualHands();
        }
    }

    // ---------- Object held ----------

    private void UpdateHeld(bool haveLeft, bool haveRight, Pose left, Pose right)
    {
        bool handsOk = (!_holdLeft || haveLeft) && (!_holdRight || haveRight);
        if (!handsOk)
        {
            // Brief tracking loss: keep the object where it is and wait
            _trackingLostTimer += Time.deltaTime;
            if (_trackingLostTimer >= _trackingLossGrace) Release("tracking_lost");
            return;
        }
        _trackingLostTimer = 0f;

        Vector3 pos;
        Quaternion rot;

        if (_holdLeft && _holdRight)
        {
            float distance = Vector3.Distance(left.position, right.position);
            if (distance > _grabPalmDistance + _releaseExtraDistance)
            {
                _releaseTimer += Time.deltaTime;
                if (_releaseTimer >= _releaseConfirmTime)
                {
                    Release("palms_spread");
                    return;
                }
            }
            else
            {
                _releaseTimer = 0f;
            }

            Pose frame = GetFrame(left.position, right.position);
            pos = frame.position + frame.rotation * _localPos;
            rot = frame.rotation * _localRot;
        }
        else
        {
            // One hand: the object is translated only, never rotated
            Vector3 palm = _holdLeft ? left.position : right.position;
            pos = palm + _oneHandOffset;
            rot = transform.rotation;
        }

        // Keep the object above its support. If the hands push deeper, the object has been placed
        float lift = SupportLift(pos, rot);
        if (lift > 0f) pos += Vector3.up * lift;
        transform.SetPositionAndRotation(pos, rot);

        if (lift > _placeReleaseDepth) Release("placed_on_support");
    }

    // ---------- Object not held ----------

    private void UpdateNotHeld(bool haveLeft, bool haveRight, Pose left, Pose right)
    {
        bool touchL = haveLeft && IsTouching(left.position);
        bool touchR = haveRight && IsTouching(right.position);

        // For ItemUsageTracker: a palm at the object = Hover, otherwise Normal
        SetState((touchL || touchR) ? InteractableState.Hover : InteractableState.Normal);

        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
            _conditionTimer = 0f;
            return;
        }

        if (_waitingForHandsToLeave)
        {
            // A new grab is allowed only after the palms have moved away from the object
            if (!touchL && !touchR) _waitingForHandsToLeave = false;
            _conditionTimer = 0f;
            return;
        }

        bool can = CanGrab(left, right, touchL, touchR, out string reason, out bool useLeft, out bool useRight);

        // Attempt bookkeeping for logs: a touch that did not end in a grab
        if (touchL || touchR)
        {
            if (!_attemptActive)
            {
                _attemptActive = true;
                _attemptTimer = 0f;
            }
            _attemptTimer += Time.deltaTime;
            if (!can && reason != null) _attemptReason = reason;
        }
        else if (_attemptActive)
        {
            _attemptActive = false;
            if (_attemptTimer >= _attemptMinTime && !string.IsNullOrEmpty(_attemptReason))
            {
                LastFailReason = _attemptReason;
                OnAttemptFailed?.Invoke(_attemptReason);
            }
            _attemptReason = "";
        }

        if (can)
        {
            _conditionTimer += Time.deltaTime;
            float need = (useLeft && useRight) ? _grabConfirmTime : _oneHandConfirmTime;
            if (_conditionTimer >= need) Grab(useLeft, useRight, left, right);
        }
        else
        {
            _conditionTimer = 0f;
        }
    }

    // ---------- Checks ----------

    private bool TryGetPalm(IHand hand, out Pose pose)
    {
        pose = default;
        return hand != null
               && hand.IsTrackedDataValid
               && hand.GetJointPose(HandJointId.HandPalm, out pose);
    }

    private bool IsHandOpen(IHand hand, Vector3 palm)
    {
        float threshold = _openFingerDistance * Mathf.Max(hand.Scale, 0.5f);
        int open = 0;
        if (IsFingerOpen(hand, HandJointId.HandIndexTip, palm, threshold)) open++;
        if (IsFingerOpen(hand, HandJointId.HandMiddleTip, palm, threshold)) open++;
        if (IsFingerOpen(hand, HandJointId.HandRingTip, palm, threshold)) open++;
        return open >= _minOpenFingers;
    }

    private bool IsFingerOpen(IHand hand, HandJointId tip, Vector3 palm, float threshold)
    {
        return hand.GetJointPose(tip, out Pose tipPose)
               && Vector3.Distance(tipPose.position, palm) > threshold;
    }

    private bool IsTouching(Vector3 palm)
    {
        if (_cubeCollider == null) return false;
        // If the palm is inside the collider, ClosestPoint returns the point itself (distance 0): that counts as touching too
        Vector3 closest = _cubeCollider.ClosestPoint(palm);
        return Vector3.Distance(closest, palm) <= _contactDistance;
    }

    private Vector3 ColliderCenter()
    {
        return _box != null ? _box.transform.TransformPoint(_box.center) : _cubeCollider.bounds.center;
    }

    private Vector3 HorizontalOffset(Vector3 palm)
    {
        Vector3 offset = palm - ColliderCenter();
        offset.y = 0f;
        return offset;
    }

    private bool IsOnSide(Vector3 palm)
    {
        return HorizontalOffset(palm).magnitude >= _minSideOffset;
    }

    private bool AreOpposite(Vector3 a, Vector3 b)
    {
        return Vector3.Dot(HorizontalOffset(a).normalized, HorizontalOffset(b).normalized) <= _oppositeThreshold;
    }

    private bool CanGrab(Pose left, Pose right, bool touchL, bool touchR,
                         out string reason, out bool useLeft, out bool useRight)
    {
        reason = null;
        useLeft = false;
        useRight = false;
        if (_cubeCollider == null) return false;

        // Side faces are only where we expect them while the object stands upright.
        // A lying object can still be taken with the Meta grabs (pinch / palm) and will stand up once it settles.
        if (!IsUpright)
        {
            if (touchL || touchR) reason = "not_upright";
            return false;
        }

        switch (_mode)
        {
            case GrabMode.TwoHands:
                if (!touchL && !touchR) return false;
                if (!touchL || !touchR) { reason = "one_hand_only"; return false; }
                if (!IsHandOpen(LeftHand, left.position) || !IsHandOpen(RightHand, right.position)) { reason = "hand_closed"; return false; }
                if (_requireHighConfidence && (!LeftHand.IsHighConfidence || !RightHand.IsHighConfidence)) { reason = "low_confidence"; return false; }
                if (!IsOnSide(left.position) || !IsOnSide(right.position)) { reason = "from_top"; return false; }
                if (!AreOpposite(left.position, right.position)) { reason = "not_opposite"; return false; }
                useLeft = true;
                useRight = true;
                return true;

            case GrabMode.OneHandLeft:
                if (touchR && !touchL) { reason = "wrong_hand"; return false; }
                useLeft = CheckOneHand(LeftHand, touchL, left, out reason);
                return useLeft;

            case GrabMode.OneHandRight:
                if (touchL && !touchR) { reason = "wrong_hand"; return false; }
                useRight = CheckOneHand(RightHand, touchR, right, out reason);
                return useRight;

            default: // OneHandAny
                if (CheckOneHand(LeftHand, touchL, left, out reason)) { useLeft = true; return true; }
                string leftReason = reason;
                if (CheckOneHand(RightHand, touchR, right, out reason)) { useRight = true; return true; }
                if (reason == null) reason = leftReason;
                return false;
        }
    }

    private bool CheckOneHand(IHand hand, bool touching, Pose palm, out string reason)
    {
        reason = null;
        if (!touching) return false;
        if (!IsHandOpen(hand, palm.position)) { reason = "hand_closed"; return false; }
        if (_requireHighConfidence && !hand.IsHighConfidence) { reason = "low_confidence"; return false; }
        if (!IsOnSide(palm.position)) { reason = "from_top"; return false; }
        return true;
    }

    // ---------- Grab, hold, release ----------

    private Pose GetFrame(Vector3 leftPalm, Vector3 rightPalm)
    {
        Vector3 mid = (leftPalm + rightPalm) * 0.5f;
        Vector3 dir = rightPalm - leftPalm;
        dir.y = 0f;

        // If the palms are almost vertically aligned, keep the previous rotation so the object does not spin
        if (dir.sqrMagnitude < 0.0004f)
            return new Pose(mid, _lastFrameRotation);

        _lastFrameRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        return new Pose(mid, _lastFrameRotation);
    }

    private void Grab(bool useLeft, bool useRight, Pose left, Pose right)
    {
        _holdLeft = useLeft;
        _holdRight = useRight;

        if (useLeft && useRight)
        {
            Pose frame = GetFrame(left.position, right.position);
            Quaternion inv = Quaternion.Inverse(frame.rotation);
            _localPos = inv * (transform.position - frame.position);
            _localRot = inv * transform.rotation;
            _grabPalmDistance = Vector3.Distance(left.position, right.position);
        }
        else
        {
            Vector3 palm = useLeft ? left.position : right.position;
            _oneHandOffset = transform.position - palm;
        }

        _rb.isKinematic = true;
        _held = true;
        _settled = false;
        _conditionTimer = 0f;
        _trackingLostTimer = 0f;
        _releaseTimer = 0f;
        _waitingToSettle = false;
        _attemptActive = false;
        _attemptReason = "";

        // Logging and task state first, then the inspector event
        SetState(InteractableState.Select);
        if (_chairGrabState != null) _chairGrabState.OnChairGrabbed();
        OnGrabbed?.Invoke();
    }

    /// restorePhysics = false when another grab takes over: the object stays kinematic and the other grab drives it.
    private void Release(string reason, bool restorePhysics = true)
    {
        _held = false;
        _holdLeft = false;
        _holdRight = false;

        if (restorePhysics)
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        _cooldownTimer = _regrabCooldown;
        _waitingForHandsToLeave = true;
        _trackingLostTimer = 0f;
        _releaseTimer = 0f;

        _waitingToSettle = restorePhysics && _freezeWhenSettled;
        _settleTimer = 0f;
        _settled = false;

        FreeVisualHands();

        SetState(InteractableState.Normal);
        if (_chairGrabState != null) _chairGrabState.OnChairUngrabbed();
        OnReleased?.Invoke(reason);
    }

    // ---------- Visual hands ----------

    /// Finds the side face the palm is pressed against: its outward normal and the distance from the center to that face.
    private bool TryGetSideFace(Vector3 palm, out Vector3 normal, out float faceDistance)
    {
        normal = Vector3.zero;
        faceDistance = 0f;
        if (_cubeCollider == null) return false;

        Vector3 dir = HorizontalOffset(palm);
        if (dir.sqrMagnitude < 1e-6f) return false;

        if (_box != null)
        {
            Transform bt = _box.transform;
            Vector3 scale = bt.lossyScale;
            Vector3 ax = bt.right;
            Vector3 az = bt.forward;
            ax.y = 0f;
            az.y = 0f;
            if (ax.sqrMagnitude > 1e-6f && az.sqrMagnitude > 1e-6f)
            {
                ax.Normalize();
                az.Normalize();
                float hx = Mathf.Max(_box.size.x * 0.5f * Mathf.Abs(scale.x), 0.001f);
                float hz = Mathf.Max(_box.size.z * 0.5f * Mathf.Abs(scale.z), 0.001f);
                float dx = Vector3.Dot(dir, ax);
                float dz = Vector3.Dot(dir, az);

                // Compare relative to the face sizes so the right face is picked on an elongated object
                if (Mathf.Abs(dx) / hx >= Mathf.Abs(dz) / hz)
                {
                    normal = ax * Mathf.Sign(dx);
                    faceDistance = hx;
                }
                else
                {
                    normal = az * Mathf.Sign(dz);
                    faceDistance = hz;
                }
                return true;
            }
        }

        Bounds b = _cubeCollider.bounds;
        float ex = Mathf.Max(b.extents.x, 0.001f);
        float ez = Mathf.Max(b.extents.z, 0.001f);
        if (Mathf.Abs(dir.x) / ex >= Mathf.Abs(dir.z) / ez)
        {
            normal = Vector3.right * Mathf.Sign(dir.x);
            faceDistance = ex;
        }
        else
        {
            normal = Vector3.forward * Mathf.Sign(dir.z);
            faceDistance = ez;
        }
        return true;
    }

    /// Shifts the visible hand so that the palm sits on the object's face.
    /// Option A: locks the synthetic hand's wrist. Option B: moves the visible hand model's root transform.
    private void PinVisualHand(IHand hand, SyntheticHand synthetic, Transform visualRoot,
                               ref bool locked, ref VisualShift rootShift)
    {
        if (hand == null) return;
        if (synthetic == null && visualRoot == null) return;
        if (!hand.GetJointPose(HandJointId.HandPalm, out Pose palm)) return;
        if (!TryGetSideFace(palm.position, out Vector3 normal, out float faceDistance)) return;

        float current = Vector3.Dot(palm.position - ColliderCenter(), normal);
        float target = faceDistance + _handSurfaceGap;
        float delta = Mathf.Clamp(target - current, -_maxVisualShift, _maxVisualShift);
        Vector3 shift = normal * delta;

        if (synthetic != null && hand.GetRootPose(out Pose root))
        {
            Pose shifted = new Pose(root.position + shift, root.rotation);
            synthetic.LockWristPose(shifted, 1f, SyntheticHand.WristLockMode.Full, true, true);
            locked = true;
        }

        if (visualRoot != null)
        {
            ShiftVisualRoot(visualRoot, shift, ref rootShift);
        }
    }

    /// Applies a world-space shift to the visible hand's root without accumulating frame after frame.
    /// Works whether the hand tracking script rewrites the root's local position every frame or leaves it alone.
    private void ShiftVisualRoot(Transform root, Vector3 worldShift, ref VisualShift s)
    {
        Vector3 localShift = root.parent != null ? root.parent.InverseTransformVector(worldShift) : worldShift;

        Vector3 currentLocal = root.localPosition;
        bool untouchedSinceLastWrite = s.active && (currentLocal - s.lastSet).sqrMagnitude < 1e-10f;
        Vector3 baseLocal = untouchedSinceLastWrite ? currentLocal - s.applied : currentLocal;

        Vector3 targetLocal = baseLocal + localShift;
        root.localPosition = targetLocal;
        s.lastSet = targetLocal;
        s.applied = localShift;
        s.active = true;
    }

    private void RestoreVisualRoot(Transform root, ref VisualShift s)
    {
        if (!s.active) return;
        if (root != null && (root.localPosition - s.lastSet).sqrMagnitude < 1e-10f)
            root.localPosition -= s.applied;
        s.active = false;
        s.applied = Vector3.zero;
    }

    private void FreeVisualHands()
    {
        if (_leftVisualLocked)
        {
            if (_leftSyntheticHand != null) _leftSyntheticHand.FreeWrist();
            _leftVisualLocked = false;
        }
        if (_rightVisualLocked)
        {
            if (_rightSyntheticHand != null) _rightSyntheticHand.FreeWrist();
            _rightVisualLocked = false;
        }
        RestoreVisualRoot(_leftHandVisualRoot, ref _leftRootShift);
        RestoreVisualRoot(_rightHandVisualRoot, ref _rightRootShift);
    }

    // ---------- Support ----------

    /// Converts a world point computed at the object's current pose into the pose the object is about to move to.
    private Vector3 ToPose(Vector3 world, Vector3 pos, Quaternion rot)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return pos + rot * Vector3.Scale(local, transform.lossyScale);
    }

    /// How far the object must be lifted at the given pose so it does not sink into a support. 0 if nothing is underneath.
    /// Rays are cast down from five points of the top face (center and four corners), so an edge resting on a step counts too.
    /// The collider may live on a child object.
    private float SupportLift(Vector3 pos, Quaternion rot)
    {
        if (_cubeCollider == null) return 0f;

        const float inset = 0.01f;
        float height;

        if (_box != null)
        {
            Transform bt = _box.transform;
            Vector3 c = _box.center;
            Vector3 half = _box.size * 0.5f;
            float hx = Mathf.Max(half.x - inset, 0f);
            float hz = Mathf.Max(half.z - inset, 0f);

            Vector3 topCenter = bt.TransformPoint(c + new Vector3(0f, half.y, 0f));
            Vector3 bottomCenter = bt.TransformPoint(c - new Vector3(0f, half.y, 0f));
            height = Vector3.Distance(topCenter, bottomCenter);

            _topPoints[0] = topCenter;
            _topPoints[1] = bt.TransformPoint(c + new Vector3(hx, half.y, hz));
            _topPoints[2] = bt.TransformPoint(c + new Vector3(-hx, half.y, hz));
            _topPoints[3] = bt.TransformPoint(c + new Vector3(hx, half.y, -hz));
            _topPoints[4] = bt.TransformPoint(c + new Vector3(-hx, half.y, -hz));
        }
        else
        {
            Bounds b = _cubeCollider.bounds;
            height = b.size.y;
            float hx = Mathf.Max(b.extents.x - inset, 0f);
            float hz = Mathf.Max(b.extents.z - inset, 0f);
            Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);

            _topPoints[0] = top;
            _topPoints[1] = top + new Vector3(hx, 0f, hz);
            _topPoints[2] = top + new Vector3(-hx, 0f, hz);
            _topPoints[3] = top + new Vector3(hx, 0f, -hz);
            _topPoints[4] = top + new Vector3(-hx, 0f, -hz);
        }

        // The points were computed at the current pose; move them to the target pose
        for (int i = 0; i < 5; i++)
            _topPoints[i] = ToPose(_topPoints[i], pos, rot);

        const float start = 0.005f;
        float lift = 0f;

        for (int i = 0; i < 5; i++)
        {
            int n = Physics.RaycastNonAlloc(
                _topPoints[i] + Vector3.up * start, Vector3.down, _hits,
                height + start, _supportLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                Collider col = _hits[k].collider;
                if (col == null) continue;
                if (col.attachedRigidbody == _rb || col.transform.IsChildOf(transform)) continue;
                if (_hits[k].distance < nearest) nearest = _hits[k].distance;
            }
            if (nearest == float.MaxValue) continue;

            float fromTop = nearest - start;
            if (fromTop < height) lift = Mathf.Max(lift, height - fromTop);
        }

        return lift;
    }

    // ---------- Freeze after placement ----------

    private void UpdateSettle()
    {
        if (!_waitingToSettle || _rb.isKinematic) return;

        bool still = _rb.linearVelocity.magnitude < _settleSpeed
                     && _rb.angularVelocity.magnitude < _settleSpeed * 10f;

        _settleTimer = still ? _settleTimer + Time.deltaTime : 0f;
        if (_settleTimer < _settleTime) return;

        _waitingToSettle = false;
        _rb.isKinematic = true;

        float tilt = Vector3.Angle(transform.up, Vector3.up);
        bool slightlyTilted = tilt > 0.1f && tilt <= _uprightMaxAngle;
        bool lyingDown = tilt > _uprightMaxAngle;

        if (slightlyTilted || (lyingDown && _standUpWhenSettled))
            StandUpright();

        _settled = true;
        OnSettled?.Invoke();
    }

    /// Rotates the object upright around its collider center, keeping its yaw, then lifts it out of the support
    /// so it rests on its bottom face at the same spot.
    private void StandUpright()
    {
        Vector3 centerBefore = _cubeCollider != null ? ColliderCenter() : transform.position;

        // Keep the current heading: use whichever local axis is still horizontal
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        Quaternion upright = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        transform.rotation = upright;

        if (_cubeCollider != null)
        {
            // Rotation happened around the pivot; move the object so the collider center stays where it was
            Vector3 centerAfter = ColliderCenter();
            transform.position += centerBefore - centerAfter;

            // Now the upright shape may be sunk into the floor: lift it out
            float lift = SupportLift(transform.position, transform.rotation);
            if (lift > 0f) transform.position += Vector3.up * lift;
        }
    }
}
