using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using CognitiveVR.Data;

namespace CognitiveVR.Core
{
    /// <summary>
    /// Single-script replacement for FreezeDetector + per-object gaze reporters.
    ///
    /// Does two things from one head reference:
    ///  1. GAZE DWELL - raycasts forward from the center eye every frame and
    ///     accumulates how long the player looked at EVERY object in the scene.
    ///     Nothing needs to be attached to the objects; any collider counts.
    ///  2. FREEZE - watches head (and optionally hand) deltas and flags a
    ///     "freeze" when nothing moves for longer than FreezeThreshold, exactly
    ///     like the old FreezeDetector. Freezes now also record WHICH object was
    ///     under the gaze ray at the time.
    ///
    /// Purely a producer: writes to ExperimentDataManager through its existing
    /// public Log methods. No other script needs modifying.
    ///
    /// Note: on Quest 3/3S there is no eye tracker, so "center eye" is the head
    /// camera - this measures head gaze, the standard dwell proxy. If eye-gaze
    /// input actions are bound below, those are used for freeze detection instead.
    /// </summary>
    public class GazeObjectTracker : MonoBehaviour
    {
        /// <summary>Aggregated gaze data for a single object.</summary>
        [Serializable]
        public struct GazeObjectStats
        {
            [Tooltip("Object name (rigidbody root if present, else the collider's GameObject).")]
            public string ObjectName;
            [Tooltip("Total accumulated gaze time across all looks (seconds).")]
            public float TotalGazeTime;
            [Tooltip("How many separate times the player looked at this object.")]
            public int LookCount;
            [Tooltip("Longest single continuous look (seconds).")]
            public float LongestStare;
            [Tooltip("Head-to-object distance at the end of the most recent look (meters).")]
            public float LastDistance;
            [Tooltip("Time.time when the object was first looked at.")]
            public float FirstLookTime;
            [Tooltip("Time.time when the most recent look ended.")]
            public float LastLookTime;
            [Tooltip("How many freezes (no movement at all) happened while looking at this object.")]
            public int FreezeCount;
            [Tooltip("Total frozen time accumulated while looking at this object (seconds).")]
            public float FreezeSeconds;
        }

        [Header("Gaze Source")]
        [Tooltip("CenterEyeAnchor / head camera. Leave empty to auto-use Camera.main transform.")]
        [SerializeField] private Transform _centerEye;

        [Header("Raycast Settings")]
        [Tooltip("How far the gaze ray reaches (meters).")]
        [SerializeField] private float _maxGazeDistance = 15f;
        [Tooltip("Which layers count as gaze targets. Exclude the player/hands layer if they have colliders.")]
        [SerializeField] private LayerMask _gazeLayers = ~0;
        [Tooltip("Should trigger colliders (zones) count as gaze targets?")]
        [SerializeField] private bool _hitTriggers = false;

        [Header("Dwell Filtering")]
        [Tooltip("Looks shorter than this are discarded as flicks (seconds). 0 = register everything.")]
        [SerializeField] private float _minLookDuration = 0.2f;
        [Tooltip("If gaze slips into empty space and returns to the same object within this time, it still counts as one continuous look.")]
        [SerializeField] private float _lookAwayGrace = 0.1f;

        [Header("Freeze Detection")]
        [Tooltip("Turn off if you only want dwell times.")]
        [SerializeField] private bool _detectFreezes = true;
        [Tooltip("Seconds of holding still to count as frozen.")]
        public float FreezeThreshold = 3f;
        [Tooltip("How far the head may DRIFT from where it was before it counts as movement (meters). Measured against an anchor pose, not frame to frame, so tracking noise no longer breaks the freeze.")]
        public float MovementEpsilon = 0.02f;
        [Tooltip("How far the head may TURN from the anchor before it counts as movement (degrees).")]
        public float RotationEpsilon = 2.5f;
        [Tooltip("How far a hand may drift from the anchor before it counts as movement (meters).")]
        public float HandMovementEpsilon = 0.04f;

        [Header("Optional Hand Tracking")]
        [Tooltip("Left hand controller transform. Optional - leave empty to judge freezes on head movement alone.")]
        public Transform LeftHand;
        [Tooltip("Right hand controller transform.")]
        public Transform RightHand;

        [Header("Optional Eye Gaze Input (leave empty on Quest 3)")]
        [Tooltip("Bind to <EyeGaze>/pose/position. If bound, freeze detection uses this instead of the head transform.")]
        [SerializeField] private InputActionProperty _gazePositionAction;
        [Tooltip("Bind to <EyeGaze>/pose/rotation.")]
        [SerializeField] private InputActionProperty _gazeRotationAction;

        [Header("Experiment Logging")]
        [Tooltip("Write gaze and freeze rows to ExperimentDataManager (if one exists).")]
        [SerializeField] private bool _logToDataManager = true;
        [Tooltip("Automatically dump per-object dwell_total rows on disable / app pause.")]
        [SerializeField] private bool _autoDumpTotals = true;

        [Header("Live Registry (runtime, read-only)")]
        [SerializeField] private string _currentlyLookingAt = "";
        [SerializeField] private bool _isFrozen;
        [SerializeField] private float _currentIdleTime;
        [SerializeField] private float _totalFreezeTime;
        [SerializeField] private int _freezeCount;
        [Tooltip("Per-object totals collected so far this scene.")]
        [SerializeField] private List<GazeObjectStats> _registry = new List<GazeObjectStats>();

        [Header("Debug")]
        [Tooltip("Draw the gaze ray in the Scene view (green = hitting something).")]
        [SerializeField] private bool _drawGazeRay = false;

        // ------------------------------------------------------------------ //

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Convenience access (last one enabled wins).</summary>
        public static GazeObjectTracker Instance { get; private set; }

        // --- Dwell events ---

        /// <summary>Fired when a look survives the minimum duration. Parameter: object name.</summary>
        public event Action<string> OnLookStarted;

        /// <summary>Fired when a registered look ends. Parameters: updated stats, duration of that look.</summary>
        public event Action<GazeObjectStats, float> OnLookEnded;

        // --- Freeze events (same signatures as the old FreezeDetector) ---

        /// <summary>Fired when idle time exceeds the threshold. Parameters: idle time, gaze position.</summary>
        public event Action<float, Vector3> OnFreezeStarted;

        /// <summary>Fired when movement resumes. Parameters: total freeze duration, gaze position.</summary>
        public event Action<float, Vector3> OnFreezeEnded;

        // --- Public state ---

        /// <summary>Name of the object currently under the gaze ray ("" if none).</summary>
        public string CurrentlyLookingAt => _currentlyLookingAt;

        /// <summary>Seconds spent on the current object so far.</summary>
        public float CurrentLookTime => _currentLookTime;

        /// <summary>True while the player is holding completely still.</summary>
        public bool IsFrozen => _isFrozen;

        /// <summary>Seconds since the last detected movement.</summary>
        public float CurrentIdleTime => _currentIdleTime;

        /// <summary>Total frozen seconds this scene.</summary>
        public float TotalFreezeTime => _totalFreezeTime;

        /// <summary>Number of freezes this scene.</summary>
        public int FreezeCount => _freezeCount;

        /// <summary>Per-object totals collected so far (inspector mirror).</summary>
        public IReadOnlyList<GazeObjectStats> Registry => _registry;

        /// <summary>
        /// True once <see cref="StopTracking"/> has run. Gaze is frozen at its
        /// session-end values and no further rows will be written.
        /// </summary>
        public bool IsTrackingStopped => _trackingStopped;

        /// <summary>
        /// The transform the gaze ray comes out of. Explicit assignment wins,
        /// otherwise Camera.main.
        /// </summary>
        public Transform CenterEye
        {
            get
            {
                if (_centerEye == null && Camera.main != null)
                    _centerEye = Camera.main.transform;

                return _centerEye;
            }
        }

        /// <summary>Alias so anything expecting the old FreezeDetector head reference still works.</summary>
        public Transform HeadTransform => CenterEye;

        // ------------------------------------------------------------------ //

        private readonly Dictionary<string, GazeObjectStats> _stats = new Dictionary<string, GazeObjectStats>();
        private readonly Dictionary<GameObject, string> _nameCache = new Dictionary<GameObject, string>();

        // Dwell state.
        private GameObject _currentTarget;
        private string _currentName;
        private float _currentLookTime;
        private float _currentLookStartedAt;
        private float _lastHitDistance;
        private float _graceTimer;
        private bool _currentLookRegistered;
        private bool _totalsDirty;
        private int _dumpPass;

        // Latched by StopTracking() at session end. Once set, LateUpdate stops
        // raycasting and the OnDisable / OnApplicationPause auto-dumps are
        // suppressed, so exactly one dwell_total pass is ever written.
        private bool _trackingStopped;

        // Freeze state. The anchor is the pose the player has been holding;
        // movement is drift away from it, not per-frame delta.
        private Vector3 _anchorPos;
        private Quaternion _anchorRot;
        private Vector3 _anchorLeftPos;
        private Vector3 _anchorRightPos;
        private Vector3 _freezeStartPosition;
        private string _freezeStartObject = "";
        private bool _wasTrackingLastFrame;

        // ------------------------------------------------------------------ //
        // Lifecycle
        // ------------------------------------------------------------------ //

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            Instance = this;
            _gazePositionAction.action?.Enable();
            _gazeRotationAction.action?.Enable();
        }

        private void OnDisable()
        {
            _gazePositionAction.action?.Disable();
            _gazeRotationAction.action?.Disable();

            // If the session already closed itself, everything has been flushed
            // and the log is shut. Dumping again here is what produced the
            // spurious "pass=2" totals that disagreed with the JSON summary.
            if (_trackingStopped)
                return;

            EndCurrentLook();

            if (_autoDumpTotals && _totalsDirty)
                DumpTotalsToLog();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            // Quest apps can be killed while backgrounded - get the totals out.
            if (paused && !_trackingStopped && _autoDumpTotals && _totalsDirty)
                DumpTotalsToLog();
        }

        private void LateUpdate()
        {
            // Session is over: no more raycasts, no more dwell accumulation.
            if (_trackingStopped)
                return;

            UpdateGaze();

            if (_detectFreezes)
                UpdateFreeze();
        }

        // ------------------------------------------------------------------ //
        // Gaze dwell
        // ------------------------------------------------------------------ //

        private void UpdateGaze()
        {
            Transform eye = CenterEye;
            if (eye == null)
                return;

            GameObject hitObject = null;
            float hitDistance = 0f;

            if (Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, _maxGazeDistance, _gazeLayers,
                    _hitTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore))
            {
                Rigidbody rb = hit.collider.attachedRigidbody;
                hitObject = rb != null ? rb.gameObject : hit.collider.gameObject;
                hitDistance = hit.distance;
            }

            if (_drawGazeRay)
                Debug.DrawRay(eye.position, eye.forward * _maxGazeDistance,
                    hitObject != null ? Color.green : Color.red);

            // Still on the same object: accumulate.
            if (hitObject != null && hitObject == _currentTarget)
            {
                _currentLookTime += Time.deltaTime;
                _lastHitDistance = hitDistance;
                _graceTimer = 0f;

                if (!_currentLookRegistered && _currentLookTime >= _minLookDuration)
                    RegisterLookStart();

                return;
            }

            // Slipped into empty space: allow a short grace before ending the look.
            if (hitObject == null && _currentTarget != null)
            {
                _graceTimer += Time.deltaTime;
                if (_graceTimer < _lookAwayGrace)
                    return;

                EndCurrentLook();
                return;
            }

            // Landed on a (different) object.
            if (hitObject != null)
            {
                EndCurrentLook();
                BeginLook(hitObject, hitDistance);
            }
        }

        private void BeginLook(GameObject target, float distance)
        {
            _currentTarget = target;
            _currentName = ResolveName(target);
            _currentLookTime = 0f;
            _currentLookStartedAt = Time.time;
            _lastHitDistance = distance;
            _graceTimer = 0f;
            _currentLookRegistered = false;
            _currentlyLookingAt = _currentName;

            // With a zero threshold, register immediately.
            if (_currentLookTime >= _minLookDuration)
                RegisterLookStart();
        }

        private void RegisterLookStart()
        {
            _currentLookRegistered = true;
            OnLookStarted?.Invoke(_currentName);

            if (_logToDataManager)
                ExperimentDataManager.Instance?.LogGazeEnter(_currentName);
        }

        private void EndCurrentLook()
        {
            if (_currentName == null)
            {
                _currentTarget = null;
                return;
            }

            if (_currentLookRegistered)
            {
                GazeObjectStats s = GetStats(_currentName, _currentLookStartedAt);

                s.TotalGazeTime += _currentLookTime;
                s.LookCount++;
                if (_currentLookTime > s.LongestStare)
                    s.LongestStare = _currentLookTime;
                s.LastDistance = _lastHitDistance;
                s.LastLookTime = Time.time;

                _stats[_currentName] = s;
                _totalsDirty = true;
                UpdateRegistryEntry(s);

                OnLookEnded?.Invoke(s, _currentLookTime);

                if (_logToDataManager)
                    ExperimentDataManager.Instance?.LogGazeExit(_currentName, _currentLookTime);
            }

            _currentTarget = null;
            _currentName = null;
            _currentLookTime = 0f;
            _graceTimer = 0f;
            _currentLookRegistered = false;
            _currentlyLookingAt = "";
        }

        // ------------------------------------------------------------------ //
        // Freeze detection
        // ------------------------------------------------------------------ //

        private void UpdateFreeze()
        {
            if (!TryReadPose(out Vector3 pos, out Quaternion rot))
                return;

            bool hasMoved = CheckForMovement(pos, rot);

            if (hasMoved)
            {
                if (_isFrozen)
                {
                    OnFreezeEnded?.Invoke(_currentIdleTime, _freezeStartPosition);

                    if (_logToDataManager)
                    {
                        ExperimentDataManager.Instance?.Log("freeze", "freeze_end", _freezeStartObject,
                            _currentIdleTime, $"gaze_pos={V(_freezeStartPosition)}");
                    }
                }

                _currentIdleTime = 0f;
                _isFrozen = false;

                // Only now does the anchor move. While holding still it stays
                // put, so slow creeping drift still accumulates against it.
                SetAnchor(pos, rot);
            }
            else
            {
                _currentIdleTime += Time.deltaTime;

                if (!_isFrozen && _currentIdleTime >= FreezeThreshold)
                {
                    _isFrozen = true;
                    _freezeCount++;
                    _freezeStartPosition = pos;
                    _freezeStartObject = _currentlyLookingAt;

                    // Credit the freeze to whatever is under the gaze ray.
                    if (!string.IsNullOrEmpty(_freezeStartObject))
                    {
                        GazeObjectStats s = GetStats(_freezeStartObject, Time.time);
                        s.FreezeCount++;
                        _stats[_freezeStartObject] = s;
                        UpdateRegistryEntry(s);
                        _totalsDirty = true;
                    }

                    OnFreezeStarted?.Invoke(_currentIdleTime, _freezeStartPosition);

                    if (_logToDataManager)
                    {
                        ExperimentDataManager.Instance?.Log("freeze", "freeze_start", _freezeStartObject,
                            _currentIdleTime, $"gaze_pos={V(_freezeStartPosition)}");
                    }
                }

                if (_isFrozen)
                {
                    _totalFreezeTime += Time.deltaTime;

                    if (!string.IsNullOrEmpty(_freezeStartObject))
                    {
                        GazeObjectStats s = GetStats(_freezeStartObject, Time.time);
                        s.FreezeSeconds += Time.deltaTime;
                        _stats[_freezeStartObject] = s;
                        UpdateRegistryEntry(s);
                    }
                }
            }
        }

        /// <summary>
        /// Reads the pose used for movement comparison: bound eye-gaze actions if
        /// present, otherwise the head transform.
        /// </summary>
        private bool TryReadPose(out Vector3 pos, out Quaternion rot)
        {
            InputAction posAction = _gazePositionAction.action;
            InputAction rotAction = _gazeRotationAction.action;

            if (posAction != null && rotAction != null)
            {
                pos = posAction.ReadValue<Vector3>();
                rot = rotAction.ReadValue<Quaternion>();
                return true;
            }

            Transform eye = CenterEye;
            if (eye == null)
            {
                pos = Vector3.zero;
                rot = Quaternion.identity;
                return false;
            }

            pos = eye.position;
            rot = eye.rotation;
            return true;
        }

        /// <summary>
        /// Windowed stillness test. Compares the current pose against the ANCHOR
        /// pose - the one held since the player last moved - rather than against
        /// last frame. Per-frame deltas never work on a real headset: tracking
        /// noise alone clears any threshold small enough to be meaningful, so
        /// the idle timer resets constantly and a freeze is never detected.
        /// Drift from an anchor tolerates that noise while still catching a
        /// genuine turn of the head.
        /// </summary>
        private bool CheckForMovement(Vector3 pos, Quaternion rot)
        {
            if (!_wasTrackingLastFrame)
            {
                _wasTrackingLastFrame = true;
                SetAnchor(pos, rot);
                return true;
            }

            if (Vector3.Distance(pos, _anchorPos) > MovementEpsilon)
                return true;

            if (Quaternion.Angle(rot, _anchorRot) > RotationEpsilon)
                return true;

            if (LeftHand != null && Vector3.Distance(LeftHand.position, _anchorLeftPos) > HandMovementEpsilon)
                return true;

            if (RightHand != null && Vector3.Distance(RightHand.position, _anchorRightPos) > HandMovementEpsilon)
                return true;

            return false;
        }

        /// <summary>Re-anchors to the current pose. Called only when movement is detected.</summary>
        private void SetAnchor(Vector3 pos, Quaternion rot)
        {
            _anchorPos = pos;
            _anchorRot = rot;
            if (LeftHand != null) _anchorLeftPos = LeftHand.position;
            if (RightHand != null) _anchorRightPos = RightHand.position;
        }

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Per-object totals sorted by TotalGazeTime (descending). If a look is
        /// in progress it is merged in non-destructively, so calling this
        /// mid-stare still counts everything.
        /// </summary>
        public List<GazeObjectStats> GetResults(bool includeCurrentLook = true)
        {
            var merged = new Dictionary<string, GazeObjectStats>(_stats);

            if (includeCurrentLook && _currentLookRegistered && _currentName != null)
            {
                if (!merged.TryGetValue(_currentName, out GazeObjectStats s))
                {
                    s = new GazeObjectStats
                    {
                        ObjectName = _currentName,
                        FirstLookTime = _currentLookStartedAt
                    };
                }

                s.TotalGazeTime += _currentLookTime;
                s.LookCount++;
                if (_currentLookTime > s.LongestStare)
                    s.LongestStare = _currentLookTime;
                s.LastDistance = _lastHitDistance;
                s.LastLookTime = Time.time;
                merged[_currentName] = s;
            }

            var results = new List<GazeObjectStats>(merged.Values);
            results.Sort((a, b) => b.TotalGazeTime.CompareTo(a.TotalGazeTime));
            return results;
        }

        /// <summary>
        /// Closes the session cleanly: ends the look that is in progress (so its
        /// gaze_exit row and its seconds are not lost), closes an open freeze,
        /// writes the single dwell_total pass, and then latches tracking off so
        /// LateUpdate stops raycasting.
        ///
        /// Called by ExperimentDataManager.FinalizeSession. After this returns,
        /// GetResults() is final and will match the dwell_total rows exactly -
        /// which is what makes the CSV and the JSON summary agree.
        /// </summary>
        public void StopTracking()
        {
            if (_trackingStopped)
                return;

            // Fold the in-progress look into the totals first. This must happen
            // BEFORE the latch, because EndCurrentLook writes a gaze_exit row.
            EndCurrentLook();

            // Same for a freeze that is still open, so freeze_summary is final.
            if (_isFrozen)
            {
                _isFrozen = false;
                OnFreezeEnded?.Invoke(_currentIdleTime, _freezeStartPosition);

                if (_logToDataManager)
                {
                    ExperimentDataManager.Instance?.Log("freeze", "freeze_end", _freezeStartObject,
                        _currentIdleTime, $"gaze_pos={V(_freezeStartPosition)}|closed_by=session_end");
                }
            }

            _currentIdleTime = 0f;

            DumpTotalsToLog();

            _trackingStopped = true;
            _currentlyLookingAt = "";
        }

        /// <summary>
        /// Writes one "gaze / dwell_total" row per object plus a
        /// "freeze / freeze_summary" row to the ExperimentDataManager. Safe to
        /// call any time; also wired to OnDisable and OnApplicationPause when
        /// Auto Dump Totals is on.
        /// </summary>
        public void DumpTotalsToLog()
        {
            ExperimentDataManager mgr = ExperimentDataManager.Instance;
            if (mgr == null)
                return;

            _dumpPass++;

            foreach (GazeObjectStats s in GetResults())
            {
                mgr.Log("gaze", "dwell_total", s.ObjectName, s.TotalGazeTime,
                    $"pass={_dumpPass}|" +
                    $"looks={s.LookCount}" +
                    $"|longest_s={s.LongestStare.ToString("F3", Inv)}" +
                    $"|first_at_s={s.FirstLookTime.ToString("F1", Inv)}" +
                    $"|last_dist_m={s.LastDistance.ToString("F2", Inv)}" +
                    $"|freezes={s.FreezeCount}" +
                    $"|freeze_s={s.FreezeSeconds.ToString("F3", Inv)}");
            }

            if (_detectFreezes)
            {
                mgr.Log("freeze", "freeze_summary", "", _totalFreezeTime,
                    $"pass={_dumpPass}|freeze_count={_freezeCount}");
            }

            _totalsDirty = false;
        }

        /// <summary>Clears everything, e.g. between session phases.</summary>
        public void ResetTracking()
        {
            _stats.Clear();
            _registry.Clear();
            _nameCache.Clear();

            _currentTarget = null;
            _currentName = null;
            _currentLookTime = 0f;
            _graceTimer = 0f;
            _currentLookRegistered = false;
            _currentlyLookingAt = "";

            _isFrozen = false;
            _currentIdleTime = 0f;
            _totalFreezeTime = 0f;
            _freezeCount = 0;
            _freezeStartObject = "";
            _wasTrackingLastFrame = false;

            _totalsDirty = false;
            _dumpPass = 0;
            _trackingStopped = false;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Logging key for a gaze target. If the object (or a parent) carries an
        /// ItemUsageTracker, its resolved ItemName wins, so gaze rows join up
        /// with interaction and backpack rows even when the item is renamed,
        /// localized or spawned as a "(Clone)". Cached per GameObject, so the
        /// GetComponentInParent cost is paid once per object per session.
        /// </summary>
        private string ResolveName(GameObject target)
        {
            if (_nameCache.TryGetValue(target, out string cached))
                return cached;

            string resolved = target.name;

            ItemUsageTracker usage = target.GetComponentInParent<ItemUsageTracker>(true);
            if (usage != null && !string.IsNullOrWhiteSpace(usage.ItemName))
                resolved = usage.ItemName;

            _nameCache[target] = resolved;
            return resolved;
        }

        private GazeObjectStats GetStats(string objectName, float firstLookTime)
        {
            if (!_stats.TryGetValue(objectName, out GazeObjectStats s))
            {
                s = new GazeObjectStats
                {
                    ObjectName = objectName,
                    FirstLookTime = firstLookTime
                };
            }
            return s;
        }

        private void UpdateRegistryEntry(GazeObjectStats s)
        {
            for (int i = 0; i < _registry.Count; i++)
            {
                if (_registry[i].ObjectName == s.ObjectName)
                {
                    _registry[i] = s;
                    return;
                }
            }
            _registry.Add(s);
        }

        /// <summary>Vector formatted with semicolons so it never fights CSV commas.</summary>
        private static string V(Vector3 v)
        {
            return $"({v.x.ToString("F2", Inv)};{v.y.ToString("F2", Inv)};{v.z.ToString("F2", Inv)})";
        }
    }
}