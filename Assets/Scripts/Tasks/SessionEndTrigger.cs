using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using CognitiveVR.Core;
using CognitiveVR.Data;
using CognitiveVR.Interaction;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// End-of-session trigger at the house door. The participant can leave at
    /// ANY time, with or without the backpack - the bag status is only logged.
    ///
    /// Geometry comes from the BoxCollider on this object (the doorway box).
    /// Its thin horizontal axis is the door normal; "outward" points away from
    /// where the player stands when the session starts (inside the house), or
    /// is forced with Outward Direction. Height is ignored, so the box can sit
    /// on the floor. Two regions along the normal, both limited to the door
    /// width (+ Lateral Margin):
    ///
    ///   inside house | PRE-EXIT ZONE (doorway .. Real Exit Distance) | REAL EXIT
    ///
    ///  - Pre-exit zone: the participant stepped into / slightly out of the door
    ///    but can still turn back ("remembered something"). Logged only:
    ///      exit_pre_zone_enter   value = visit number
    ///                            details: visit|items|backpack_taken|backpack_dist_m
    ///      exit_pre_zone_return  walked back INTO the house. value = seconds spent in the zone
    ///                            details: visit|time_in_zone_s
    ///  - Real exit: head gets Real Exit Distance past the door plane:
    ///      exit_complete         value = items in the backpack
    ///                            details: backpack_taken=0|1 (bag within Backpack Carry Radius
    ///                            of the head) | backpack_dist_m | items | item_list (a;b;c) |
    ///                            pre_zone_visits | pre_zone_returns | time_in_pre_zone_s
    ///    then FinalizeSession("exit_player") and onSessionComplete (end audio +
    ///    "well done" screen), always - whatever is in the bag.
    ///  - Time limit (SimulationAutoEnd -> CompleteByTimeout):
    ///      exit_timeout          same details as exit_complete + in_pre_zone=0|1
    ///    then FinalizeSession("simulation_timeout") and the same ending.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SessionEndTrigger : MonoBehaviour
    {
        public enum OutwardDirection { Auto, AlongPositiveAxis, AlongNegativeAxis }

        [Header("Backpack (logged only - never blocks the exit)")]
        [Tooltip("The backpack. Used to decide backpack_taken (distance to the head).")]
        [SerializeField] private Transform backpackObject;
        [Tooltip("The inventory zone whose contents are counted. Auto-found if left empty.")]
        [SerializeField] private BackpackInventoryZone backpack;
        [Tooltip("The bag counts as taken if it is within this many meters of the head when the player leaves.")]
        [SerializeField] private float backpackCarryRadius = 1.5f;

        [Header("Player")]
        [Tooltip("Head / center eye. Auto-found from GazeObjectTracker if left empty.")]
        [SerializeField] private Transform playerHead;

        [Header("Door geometry")]
        [Tooltip("Which way is OUT along the door normal. Auto = away from the player's position when the session starts.")]
        [SerializeField] private OutwardDirection outwardDirection = OutwardDirection.Auto;
        [Tooltip("How far INTO the house (from the door plane) the pre-exit zone starts (m).")]
        [SerializeField] private float preZoneInsideDepth = 0.3f;
        [Tooltip("How far OUT past the door plane the head must get for the real exit (m). Everything between is the pre-exit zone.")]
        [SerializeField] private float realExitDistance = 1.0f;
        [Tooltip("Extra width on each side of the door counted as 'in the doorway' (m).")]
        [SerializeField] private float lateralMargin = 0.5f;

        [Header("Behaviour")]
        [Tooltip("Fire only once per session. Turn off if you reset and re-run in the same scene.")]
        [SerializeField] private bool oneShot = true;

        [Header("Events")]
        [Tooltip("Fired when the exit succeeds and logging is finalized. Hook your popup message here.")]
        public UnityEvent onSessionComplete;

        [Header("Runtime (Read Only)")]
        [SerializeField] private bool _hasFired;
        [SerializeField] private bool _inPreZone;
        [SerializeField] private int _preZoneVisits;
        [SerializeField] private int _preZoneReturns;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private BoxCollider _box;
        private SessionTimer _sessionTimer;
        private float _outSign;          // +1 / -1 along _normal; 0 = not resolved yet
        private float _preZoneEnteredAt = -1f;
        private float _lastPreZoneSeconds = -1f;

        /// <summary>How many items are currently in the backpack.</summary>
        public int CurrentItemCount => backpack != null ? backpack.StoredItemNames.Count : 0;

        public int PreZoneVisits => _preZoneVisits;
        public int PreZoneReturns => _preZoneReturns;

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();

            if (backpack == null)
                backpack = FindFirstObjectByType<BackpackInventoryZone>();

            if (playerHead == null && GazeObjectTracker.Instance != null)
                playerHead = GazeObjectTracker.Instance.HeadTransform;

            if (playerHead == null && Camera.main != null)
                playerHead = Camera.main.transform;

            _sessionTimer = FindFirstObjectByType<SessionTimer>();

            if (outwardDirection == OutwardDirection.AlongPositiveAxis) _outSign = 1f;
            else if (outwardDirection == OutwardDirection.AlongNegativeAxis) _outSign = -1f;
        }

        private void Update()
        {
            if (_hasFired && oneShot) return;
            if (playerHead == null || _box == null) return;

            GetDoorFrame(out Vector3 center, out Vector3 normal, out Vector3 lateral, out float halfWidth);

            Vector3 d = playerHead.position - center;
            d.y = 0f;
            float along = Vector3.Dot(d, normal);
            float side = Mathf.Abs(Vector3.Dot(d, lateral));

            // Auto direction: the player is inside the house when the session
            // starts, so "out" is the other side.
            if (_outSign == 0f)
            {
                bool started = _sessionTimer == null || _sessionTimer.IsRunning;
                if (!started || Mathf.Abs(along) < 0.05f) return;
                _outSign = along < 0f ? 1f : -1f;
                Debug.Log($"[{nameof(SessionEndTrigger)}] Outward direction resolved: {(_outSign > 0 ? "+" : "-")} door normal.", this);
            }

            along *= _outSign; // now: < 0 inside the house, > 0 outside
            bool inDoorWidth = side <= halfWidth + lateralMargin;

            if (inDoorWidth && along >= realExitDistance)
            {
                Complete();
                return;
            }

            bool inPre = inDoorWidth && along >= -preZoneInsideDepth;

            if (inPre && !_inPreZone) EnterPreZone();
            else if (!inPre && _inPreZone) LeavePreZone(backIntoHouse: along < 0f);
        }

        // ------------------------------------------------------------------ //
        // Pre-exit zone
        // ------------------------------------------------------------------ //

        private void EnterPreZone()
        {
            _inPreZone = true;
            _preZoneVisits++;
            _preZoneEnteredAt = Now();

            Manager?.Log("task", "exit_pre_zone_enter", "ExitZone", _preZoneVisits,
                $"visit={_preZoneVisits}|{BackpackDetails(includeList: false)}");
        }

        private void LeavePreZone(bool backIntoHouse)
        {
            _inPreZone = false;
            float spent = _preZoneEnteredAt >= 0f ? Mathf.Max(0f, Now() - _preZoneEnteredAt) : -1f;
            _lastPreZoneSeconds = spent;
            _preZoneEnteredAt = -1f;

            // Leaving sideways / outward without reaching the real exit is not a
            // "came back"; only stepping back into the house is.
            if (!backIntoHouse) return;

            _preZoneReturns++;
            Manager?.Log("task", "exit_pre_zone_return", "ExitZone", spent,
                $"visit={_preZoneVisits}|time_in_zone_s={spent.ToString("F2", Inv)}|returns={_preZoneReturns}");
        }

        // ------------------------------------------------------------------ //
        // Completion
        // ------------------------------------------------------------------ //

        private void Complete()
        {
            _hasFired = true;

            float inZone = _preZoneEnteredAt >= 0f ? Mathf.Max(0f, Now() - _preZoneEnteredAt) : _lastPreZoneSeconds;

            Manager?.Log("task", "exit_complete", "ExitZone", CurrentItemCount,
                $"{BackpackDetails(includeList: true)}" +
                $"|pre_zone_visits={_preZoneVisits}|pre_zone_returns={_preZoneReturns}" +
                (inZone >= 0f ? $"|time_in_pre_zone_s={inZone.ToString("F2", Inv)}" : ""));

            RegisterExitSummary("exit_player");
            Manager?.FinalizeSession("exit_player");

            onSessionComplete?.Invoke();
        }

        /// <summary>Manual finalize, e.g. from an experimenter button.</summary>
        public void ForceComplete()
        {
            if (_hasFired && oneShot) return;

            _hasFired = true;
            Manager?.Log("task", "exit_complete", "ExitZone", CurrentItemCount,
                $"reason=manual|{BackpackDetails(includeList: true)}");
            RegisterExitSummary("manual");
            Manager?.FinalizeSession("manual");
            onSessionComplete?.Invoke();
        }

        /// <summary>
        /// Automatic end when the simulation time limit is reached (called by
        /// SimulationAutoEnd). Writes exit_timeout with the bag status, then plays
        /// the same ending as a normal exit.
        /// </summary>
        public void CompleteByTimeout()
        {
            if (_hasFired && oneShot) return;

            _hasFired = true;
            Manager?.Log("task", "exit_timeout", "ExitZone", CurrentItemCount,
                $"{BackpackDetails(includeList: true)}" +
                $"|in_pre_zone={(_inPreZone ? 1 : 0)}" +
                $"|pre_zone_visits={_preZoneVisits}|pre_zone_returns={_preZoneReturns}");
            RegisterExitSummary("simulation_timeout");
            Manager?.FinalizeSession("simulation_timeout"); // no-op if already finalized
            onSessionComplete?.Invoke();
        }

        /// <summary>Allows the trigger to fire again after a ResetPuddle-style restart.</summary>
        public void ResetTrigger()
        {
            _hasFired = false;
            _inPreZone = false;
            _preZoneVisits = 0;
            _preZoneReturns = 0;
            _preZoneEnteredAt = -1f;
            _lastPreZoneSeconds = -1f;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private bool BackpackTaken(out float distance)
        {
            distance = -1f;
            if (backpackObject == null || playerHead == null) return false;
            distance = Vector3.Distance(backpackObject.position, playerHead.position);
            return distance <= backpackCarryRadius;
        }

        private string BackpackDetails(bool includeList)
        {
            bool taken = BackpackTaken(out float dist);
            string s = $"backpack_taken={(taken ? 1 : 0)}" +
                       (dist >= 0f ? $"|backpack_dist_m={dist.ToString("F2", Inv)}" : "") +
                       $"|items={CurrentItemCount}";
            if (includeList && backpack != null)
                s += $"|item_list={string.Join(";", backpack.StoredItemNames)}";
            return s;
        }

        private void RegisterExitSummary(string how)
        {
            bool taken = BackpackTaken(out float dist);
            Manager?.RegisterExit(how, taken, dist, CurrentItemCount, _preZoneVisits, _preZoneReturns);
        }

        /// <summary>
        /// Door frame from the BoxCollider: center (world), horizontal normal
        /// (the box's thinner horizontal axis), lateral axis and half door width.
        /// </summary>
        private void GetDoorFrame(out Vector3 center, out Vector3 normal, out Vector3 lateral, out float halfWidth)
        {
            Transform t = _box.transform;
            Vector3 scale = t.lossyScale;
            center = t.TransformPoint(_box.center);

            float halfX = Mathf.Abs(_box.size.x * scale.x) * 0.5f;
            float halfZ = Mathf.Abs(_box.size.z * scale.z) * 0.5f;

            Vector3 right = t.right; right.y = 0f; right.Normalize();
            Vector3 fwd = t.forward; fwd.y = 0f; fwd.Normalize();

            if (halfX <= halfZ) { normal = right; lateral = fwd; halfWidth = halfZ; }
            else { normal = fwd; lateral = right; halfWidth = halfX; }
        }

        private float Now()
        {
            return Manager != null ? Manager.LoggerElapsed : Time.time;
        }

        private void OnDrawGizmosSelected()
        {
            if (_box == null) _box = GetComponent<BoxCollider>();
            if (_box == null) return;

            GetDoorFrame(out Vector3 center, out Vector3 normal, out Vector3 lateral, out float halfWidth);
            float sign = _outSign != 0f ? _outSign : (outwardDirection == OutwardDirection.AlongNegativeAxis ? -1f : 1f);
            Vector3 n = normal * sign;
            float w = halfWidth + lateralMargin;
            Vector3 up = Vector3.up * 2f;

            // Pre-exit zone (yellow) and real exit line (red).
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.6f);
            Vector3 preCenter = center + n * ((realExitDistance - preZoneInsideDepth) * 0.5f) + Vector3.up;
            Gizmos.matrix = Matrix4x4.TRS(preCenter, Quaternion.LookRotation(n, Vector3.up), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(w * 2f, 2f, realExitDistance + preZoneInsideDepth));
            Gizmos.matrix = Matrix4x4.identity;

            Gizmos.color = Color.red;
            Vector3 exitMid = center + n * realExitDistance;
            Gizmos.DrawLine(exitMid - lateral * w, exitMid + lateral * w);
            Gizmos.DrawLine(exitMid - lateral * w + up, exitMid + lateral * w + up);
        }
    }
}
