using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CognitiveVR.Core
{
    /// <summary>
    /// A single long-stare "freeze" event reported by a per-object gaze tracker.
    /// </summary>
    [Serializable]
    public struct GazeFreezeRecord
    {
        [Tooltip("Name of the object the player froze on.")]
        public string ObjectName;
        [Tooltip("Continuous gaze duration on the object (seconds).")]
        public float Duration;
        [Tooltip("Head-to-object distance at the moment gaze ended (meters).")]
        public float Distance;
        [Tooltip("Time.time when the freeze was reported.")]
        public float EndTime;
    }

    public class FreezeDetector : MonoBehaviour
    {
        [Header("Eye Gaze Input")]
        [Tooltip("Bind to <EyeGaze>/pose/position from XRI Default Input Actions")]
        [SerializeField] private InputActionProperty _gazePositionAction;
        [Tooltip("Bind to <EyeGaze>/pose/rotation from XRI Default Input Actions")]
        [SerializeField] private InputActionProperty _gazeRotationAction;

        [Header("Optional Hand Tracking")]
        [Tooltip("Left hand controller transform")]
        public Transform LeftHand;
        [Tooltip("Right hand controller transform")]
        public Transform RightHand;

        [Header("Head / Camera Reference")]
        [Tooltip("The object the camera comes out of (head/rig). Leave empty to auto-use Camera.main transform. Shared with per-object GazeFreezeReporter scripts.")]
        [SerializeField] private Transform _headObject;

        [Header("Gaze Freeze Registry")]
        [Tooltip("Every long-stare freeze reported by per-object GazeFreezeReporter scripts during the scene.")]
        [SerializeField] private List<GazeFreezeRecord> _gazeFreezes = new List<GazeFreezeRecord>();

        [Header("Detection Settings")]
        [Tooltip("Seconds without meaningful movement to count as frozen")]
        public float FreezeThreshold = 3f;
        [Tooltip("Minimum position delta per frame to count as movement")]
        public float MovementEpsilon = 0.005f;
        [Tooltip("Minimum rotation delta (degrees) per frame to count as movement")]
        public float RotationEpsilon = 0.5f;
        [Tooltip("Minimum hand position delta per frame to count as movement")]
        public float HandMovementEpsilon = 0.01f;

        [Header("Runtime State")]
        [SerializeField] private bool _isFrozen;
        [SerializeField] private float _currentIdleTime;
        [SerializeField] private float _totalFreezeTime;
        [SerializeField] private int _freezeCount;

        public bool IsFrozen => _isFrozen;
        public float CurrentIdleTime => _currentIdleTime;
        public float TotalFreezeTime => _totalFreezeTime;
        public int FreezeCount => _freezeCount;

        /// <summary>
        /// Convenience access for per-object gaze scripts (last one enabled wins).
        /// </summary>
        public static FreezeDetector Instance { get; private set; }

        /// <summary>
        /// The head/camera object shared with per-object gaze trackers.
        /// Falls back to Camera.main if none is assigned.
        /// </summary>
        public Transform HeadTransform
        {
            get
            {
                if (_headObject != null)
                    return _headObject;

                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                    _headObject = mainCamera.transform;

                return _headObject;
            }
        }

        /// <summary>
        /// All long-stare freeze events collected during the scene.
        /// </summary>
        public IReadOnlyList<GazeFreezeRecord> GazeFreezes => _gazeFreezes;

        /// <summary>
        /// Fired when a freeze is detected (idle time exceeded threshold).
        /// Parameters: freeze duration so far, gaze position at freeze start.
        /// </summary>
        public event Action<float, Vector3> OnFreezeStarted;

        /// <summary>
        /// Fired when the player resumes movement after a freeze.
        /// Parameters: total freeze duration, gaze position where freeze occurred.
        /// </summary>
        public event Action<float, Vector3> OnFreezeEnded;

        /// <summary>
        /// Fired when a per-object gaze tracker reports a long-stare freeze on look-away.
        /// </summary>
        public event Action<GazeFreezeRecord> OnGazeFreezeReported;

        private Vector3 _lastGazePos;
        private Quaternion _lastGazeRot;
        private Vector3 _lastLeftPos;
        private Vector3 _lastRightPos;
        private Vector3 _freezeStartPosition;
        private bool _wasTrackingLastFrame;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
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
        }

        private void LateUpdate()
        {
            if (_gazePositionAction.action == null || _gazeRotationAction.action == null)
                return;

            bool hasMoved = CheckForMovement();

            if (hasMoved)
            {
                if (_isFrozen)
                {
                    OnFreezeEnded?.Invoke(_currentIdleTime, _freezeStartPosition);
                }
                _currentIdleTime = 0f;
                _isFrozen = false;
            }
            else
            {
                _currentIdleTime += Time.deltaTime;

                if (!_isFrozen && _currentIdleTime >= FreezeThreshold)
                {
                    _isFrozen = true;
                    _freezeCount++;
                    _freezeStartPosition = _gazePositionAction.action.ReadValue<Vector3>();
                    OnFreezeStarted?.Invoke(_currentIdleTime, _freezeStartPosition);
                }

                if (_isFrozen)
                {
                    _totalFreezeTime += Time.deltaTime;
                }
            }

            StoreCurrentValues();
        }

        private bool CheckForMovement()
        {
            Vector3 gazePos = _gazePositionAction.action.ReadValue<Vector3>();
            Quaternion gazeRot = _gazeRotationAction.action.ReadValue<Quaternion>();

            if (!_wasTrackingLastFrame)
            {
                _wasTrackingLastFrame = true;
                _lastGazePos = gazePos;
                _lastGazeRot = gazeRot;
                StoreCurrentValues();
                return true;
            }

            float gazePosDelta = Vector3.Distance(gazePos, _lastGazePos);
            float gazeRotDelta = Quaternion.Angle(gazeRot, _lastGazeRot);

            if (gazePosDelta > MovementEpsilon || gazeRotDelta > RotationEpsilon)
                return true;

            if (LeftHand != null)
            {
                float leftDelta = Vector3.Distance(LeftHand.position, _lastLeftPos);
                if (leftDelta > HandMovementEpsilon) return true;
            }

            if (RightHand != null)
            {
                float rightDelta = Vector3.Distance(RightHand.position, _lastRightPos);
                if (rightDelta > HandMovementEpsilon) return true;
            }

            return false;
        }

        private void StoreCurrentValues()
        {
            if (_gazePositionAction.action != null)
            {
                _lastGazePos = _gazePositionAction.action.ReadValue<Vector3>();
                _lastGazeRot = _gazeRotationAction.action.ReadValue<Quaternion>();
            }
            if (LeftHand != null) _lastLeftPos = LeftHand.position;
            if (RightHand != null) _lastRightPos = RightHand.position;
        }

        /// <summary>
        /// Called by per-object gaze trackers when the player looks away after a
        /// stare that exceeded the allowed time. Records duration and end-distance.
        /// </summary>
        public void ReportGazeFreeze(string objectName, float duration, float distance)
        {
            var record = new GazeFreezeRecord
            {
                ObjectName = objectName,
                Duration = duration,
                Distance = distance,
                EndTime = Time.time
            };

            _gazeFreezes.Add(record);
            OnGazeFreezeReported?.Invoke(record);
        }

        public void ResetTracking()
        {
            _isFrozen = false;
            _currentIdleTime = 0f;
            _totalFreezeTime = 0f;
            _freezeCount = 0;
            _wasTrackingLastFrame = false;
            _gazeFreezes.Clear();
        }
    }
}
