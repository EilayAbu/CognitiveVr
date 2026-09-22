using UnityEngine;

/// <summary>
/// Seated "wheelchair push" locomotion for users who can't use thumbsticks or legs.
/// Each hand works like a wheel rim. Pushing a hand forward while it is inside the
/// rim zone (low, beside the body) drives that side's wheel.
///   - Both hands forward        -> move forward
///   - One hand pushes more      -> turn toward the slower side
///   - Hand held still on rim    -> brakes that wheel (optional)
///   - Backward stroke           -> free "recovery" stroke (or reverse, if allowReverse)
/// No grip is needed. Works with hand tracking or controllers.
/// Put this on any GameObject and assign the rig, head and hand transforms.
/// </summary>
public class WheelchairLocomotion : MonoBehaviour
{
    public enum HandMode { BothHands, LeftOnly, RightOnly }
    public enum DirectionSource { Head, Rig }

    [Header("Rig references")]
    [Tooltip("Rig root that gets moved (XR Origin / OVRCameraRig). Hands and head must be children of it.")]
    public Transform rig;
    [Tooltip("Main camera / CenterEyeAnchor.")]
    public Transform head;
    [Tooltip("Left hand or controller anchor.")]
    public Transform leftHand;
    [Tooltip("Right hand or controller anchor.")]
    public Transform rightHand;
    [Tooltip("Optional. If assigned, movement goes through it so walls block the player.")]
    public CharacterController characterController;
    public bool applyGravity = true;

    [Header("Mode")]
    [Tooltip("Use a single-hand mode if the user only has one usable arm. " +
             "Then: push forward = move, sweep sideways = turn.")]
    public HandMode handMode = HandMode.BothHands;

    [Header("Direction")]
    [Tooltip("Head: 'forward' is where you are looking (hand strokes are also measured along the gaze). " +
             "Rig: 'forward' is the chair/rig forward.")]
    public DirectionSource directionSource = DirectionSource.Head;

    [Header("Rim zone (where the hands count as 'on the wheel')")]
    [Tooltip("OFF: hands count as pushing anywhere they are. ON: only inside the zone below.")]
    public bool useRimZone = false;
    [Tooltip("Hand must be at least this far below the head (m).")]
    public float rimMinBelowHead = 0.35f;
    [Tooltip("Hand must be at most this far below the head (m).")]
    public float rimMaxBelowHead = 1.1f;
    [Tooltip("Hand must be at least this far sideways from the head (m). 0 = no side requirement.")]
    public float rimMinSideOffset = 0f;
    [Tooltip("Used by CalibrateRimHeight(): zone = measured height +/- this value.")]
    public float rimTolerance = 0.2f;

    [Header("Feel")]
    [Tooltip("Hand speed -> wheel speed multiplier. Raise it for users with small or slow movements.")]
    public float pushGain = 1.5f;
    [Tooltip("How fast the wheel catches up to the hand (m/s^2).")]
    public float pushAcceleration = 8f;
    public float maxSpeed = 2f;
    [Tooltip("Deceleration while coasting (m/s^2).")]
    public float friction = 1.2f;
    [Tooltip("Distance between the virtual wheels (m). Smaller = sharper turns.")]
    public float wheelBase = 0.6f;
    public float maxTurnSpeed = 120f;
    [Tooltip("Hand speeds below this are ignored (filters tremor).")]
    public float handSpeedDeadzone = 0.08f;
    [Tooltip("0 = raw hand velocity, closer to 1 = smoother (also filters tremor).")]
    [Range(0f, 0.95f)] public float smoothing = 0.8f;

    [Header("Braking / reverse")]
    [Tooltip("Holding a hand still on the rim brakes that wheel.")]
    public bool holdToBrake = true;
    public float brakeHoldTime = 0.5f;
    public float brakeStrength = 4f;
    [Tooltip("OFF: backward strokes are free recovery strokes (easiest). " +
             "ON: backward strokes brake/reverse, so the user must lift the hand out of the zone to recover.")]
    public bool allowReverse = false;
    public float maxReverseSpeed = 0.8f;

    [Header("Single-hand turning")]
    [Tooltip("Degrees/second of turn per m/s of sideways hand speed.")]
    public float singleHandTurnGain = 120f;
    public float turnResponse = 360f;

    public float CurrentSpeed => (_wheelL + _wheelR) * 0.5f;
    public bool LeftOnRim { get; private set; }
    public bool RightOnRim { get; private set; }

    float _wheelL, _wheelR, _turn;
    float _stillL, _stillR;
    float _verticalVel;
    Vector3 _prevL, _prevR, _velL, _velR;
    Vector3 _fwdLocal = Vector3.forward; // movement forward, in rig space (flat)
    bool _initialized;

    void OnEnable()
    {
        _initialized = false;
        _wheelL = _wheelR = _turn = 0f;
    }

    void Update()
    {
        if (!rig || !head) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Everything is measured in rig space, so the chair's own movement
        // never feeds back into the hand velocity.
        Vector3 headLocal = rig.InverseTransformPoint(head.position);
        bool useL = leftHand && handMode != HandMode.RightOnly;
        bool useR = rightHand && handMode != HandMode.LeftOnly;
        Vector3 l = useL ? rig.InverseTransformPoint(leftHand.position) : Vector3.zero;
        Vector3 r = useR ? rig.InverseTransformPoint(rightHand.position) : Vector3.zero;

        if (!_initialized)
        {
            _prevL = l; _prevR = r;
            _velL = _velR = Vector3.zero;
            _initialized = true;
            return;
        }

        float t = 1f - Mathf.Pow(smoothing, dt * 60f); // frame-rate independent smoothing
        _velL = Vector3.Lerp(_velL, (l - _prevL) / dt, t);
        _velR = Vector3.Lerp(_velR, (r - _prevR) / dt, t);
        _prevL = l; _prevR = r;

        UpdateForwardAxis();
        Vector3 fwd = _fwdLocal;
        Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

        LeftOnRim = useL && IsOnRim(l, headLocal);
        RightOnRim = useR && IsOnRim(r, headLocal);

        if (handMode == HandMode.BothHands)
        {
            _wheelL = UpdateWheel(_wheelL, LeftOnRim, Vector3.Dot(_velL, fwd), ref _stillL, dt);
            _wheelR = UpdateWheel(_wheelR, RightOnRim, Vector3.Dot(_velR, fwd), ref _stillR, dt);
            _turn = 0f;
        }
        else
        {
            bool left = handMode == HandMode.LeftOnly;
            bool onRim = left ? LeftOnRim : RightOnRim;
            Vector3 v = left ? _velL : _velR;

            float w = UpdateWheel(_wheelL, onRim, Vector3.Dot(v, fwd), ref _stillL, dt);
            _wheelL = _wheelR = w;

            float side = Vector3.Dot(v, right);
            float targetTurn = (onRim && Mathf.Abs(side) > handSpeedDeadzone) ? side * singleHandTurnGain : 0f;
            _turn = Mathf.MoveTowards(_turn, targetTurn, turnResponse * dt);
        }

        ApplyMotion(dt);
    }

    /// <summary>Flat forward direction in rig space: gaze or rig forward.</summary>
    void UpdateForwardAxis()
    {
        if (directionSource == DirectionSource.Rig)
        {
            _fwdLocal = Vector3.forward;
            return;
        }

        Vector3 f = rig.InverseTransformDirection(head.forward);
        f.y = 0f;
        // Looking almost straight up/down gives no usable horizontal direction: keep the last one.
        if (f.sqrMagnitude > 0.04f)
            _fwdLocal = f.normalized;
    }

    bool IsOnRim(Vector3 hand, Vector3 headLocal)
    {
        if (!useRimZone) return true;

        float below = headLocal.y - hand.y;
        if (below < rimMinBelowHead || below > rimMaxBelowHead) return false;
        if (rimMinSideOffset > 0f && Mathf.Abs(hand.x - headLocal.x) < rimMinSideOffset) return false;
        return true;
    }

    float UpdateWheel(float wheel, bool onRim, float handForwardSpeed, ref float stillTimer, float dt)
    {
        if (!onRim)
        {
            stillTimer = 0f;
            return Coast(wheel, dt);
        }

        // Hand resting on the rim
        if (Mathf.Abs(handForwardSpeed) < handSpeedDeadzone)
        {
            stillTimer += dt;
            if (holdToBrake && stillTimer >= brakeHoldTime)
                return Mathf.MoveTowards(wheel, 0f, brakeStrength * dt);
            return Coast(wheel, dt);
        }
        stillTimer = 0f;

        float push = handForwardSpeed * pushGain;

        if (push > 0f)
        {
            // Forward stroke: like a real rim without grip, it can only add speed.
            if (push > wheel)
                wheel = Mathf.MoveTowards(wheel, push, pushAcceleration * dt);
            else
                wheel = Coast(wheel, dt);
        }
        else
        {
            if (allowReverse)
            {
                if (push < wheel)
                    wheel = Mathf.MoveTowards(wheel, push, brakeStrength * dt);
                else
                    wheel = Coast(wheel, dt);
            }
            else
            {
                // Recovery stroke: hand slides back freely.
                wheel = Coast(wheel, dt);
            }
        }

        return Mathf.Clamp(wheel, allowReverse ? -maxReverseSpeed : 0f, maxSpeed);
    }

    float Coast(float wheel, float dt) => Mathf.MoveTowards(wheel, 0f, friction * dt);

    void ApplyMotion(float dt)
    {
        float linear = (_wheelL + _wheelR) * 0.5f;
        // Left wheel faster than right -> turn right (positive yaw).
        float yaw = (_wheelL - _wheelR) / Mathf.Max(0.1f, wheelBase) * Mathf.Rad2Deg + _turn;
        yaw = Mathf.Clamp(yaw, -maxTurnSpeed, maxTurnSpeed);

        bool rotated = false;
        if (Mathf.Abs(yaw) > 0.01f)
        {
            // Rotate around the head so turning feels natural and doesn't swing the view.
            rig.RotateAround(head.position, Vector3.up, yaw * dt);
            rotated = true;
        }

        // _fwdLocal is in rig space, so it stays correct after the rotation above.
        Vector3 fwd = rig.TransformDirection(_fwdLocal);
        fwd.y = 0f;
        fwd.Normalize();
        Vector3 delta = fwd * (linear * dt);

        if (characterController && characterController.enabled)
        {
            if (rotated) Physics.SyncTransforms();

            if (applyGravity)
            {
                if (characterController.isGrounded) _verticalVel = -1f;
                else _verticalVel += Physics.gravity.y * dt;
                delta.y = _verticalVel * dt;
            }
            characterController.Move(delta);
        }
        else
        {
            rig.position += delta;
        }
    }

    /// <summary>Stops all movement immediately (e.g. when opening a menu).</summary>
    public void StopImmediately()
    {
        _wheelL = _wheelR = _turn = 0f;
    }

    /// <summary>
    /// Ask the user to rest their hands where they comfortably reach "the wheels",
    /// then call this (from a button, a voice command, or the Inspector context menu).
    /// </summary>
    [ContextMenu("Calibrate Rim Height (hands at rim)")]
    public void CalibrateRimHeight()
    {
        if (!rig || !head) return;
        Vector3 headLocal = rig.InverseTransformPoint(head.position);
        float sum = 0f;
        int count = 0;

        if (leftHand && handMode != HandMode.RightOnly)
        {
            sum += headLocal.y - rig.InverseTransformPoint(leftHand.position).y;
            count++;
        }
        if (rightHand && handMode != HandMode.LeftOnly)
        {
            sum += headLocal.y - rig.InverseTransformPoint(rightHand.position).y;
            count++;
        }
        if (count == 0) return;

        float avg = sum / count;
        rimMinBelowHead = Mathf.Max(0.05f, avg - rimTolerance);
        rimMaxBelowHead = avg + rimTolerance;
        Debug.Log($"[WheelchairLocomotion] Rim zone calibrated: {rimMinBelowHead:F2}m - {rimMaxBelowHead:F2}m below head");
    }
}