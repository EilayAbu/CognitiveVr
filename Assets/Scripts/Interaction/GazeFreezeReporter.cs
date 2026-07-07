using CognitiveVR.Core;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Per-object gaze freeze tracker.
    ///
    /// Measures how long the player continuously looks at THIS object using the
    /// same angle + distance method as <see cref="DelayedGazeGlow"/>
    /// (direction from head to object, distance limit, and enter/exit angle
    /// hysteresis). When the player looks away, if the continuous gaze exceeded
    /// <see cref="_allowedGazeSeconds"/> it reports the stare duration and the
    /// head-to-object distance at that moment to the central
    /// <see cref="FreezeDetector"/> registry.
    /// </summary>
    [DisallowMultipleComponent]
    public class GazeFreezeReporter : MonoBehaviour
    {
        [Header("Head Reference")]
        [Tooltip("Optional override. Leave empty to use FreezeDetector's head, then Camera.main.")]
        [SerializeField] private Transform _headTransformOverride;

        [Header("Detection")]
        [Tooltip("Looking-at angle (degrees from head forward) to start counting gaze on this object.")]
        [Range(0f, 90f)]
        [SerializeField] private float _enterAngleDegrees = 12f;

        [Tooltip("Looking-away angle (must be >= enter angle) before gaze on this object is dropped.")]
        [Range(0f, 90f)]
        [SerializeField] private float _exitAngleDegrees = 18f;

        [SerializeField] private bool _useDistanceLimit = true;

        [Tooltip("Maximum distance from the head at which gaze on this object counts.")]
        [SerializeField] private float _maxDistance = 5f;

        [Header("Freeze Threshold")]
        [Tooltip("Continuous gaze (seconds) above which a look-away is reported as a freeze.")]
        [SerializeField] private float _allowedGazeSeconds = 3f;

        [Header("Registry")]
        [Tooltip("Central registry. Leave empty to auto-resolve via FreezeDetector.Instance / FindObjectOfType.")]
        [SerializeField] private FreezeDetector _freezeDetector;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs;

        private Transform _cachedHead;
        private float _continuousGazeTime;
        private float _lastDistance;

        public float ContinuousGazeTime => _continuousGazeTime;

        private void OnValidate()
        {
            if (_exitAngleDegrees < _enterAngleDegrees)
                _exitAngleDegrees = _enterAngleDegrees;

            if (_maxDistance < 0.05f)
                _maxDistance = 0.05f;

            if (_allowedGazeSeconds < 0f)
                _allowedGazeSeconds = 0f;
        }

        private void OnDisable()
        {
            // Flush an in-progress long stare so it isn't lost.
            EndGaze();
        }

        private void Update()
        {
            Transform head = ResolveHead();
            if (head == null)
            {
                EndGaze();
                return;
            }

            Vector3 toObject = transform.position - head.position;
            float sqr = toObject.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                EndGaze();
                return;
            }

            if (_useDistanceLimit && sqr > _maxDistance * _maxDistance)
            {
                EndGaze();
                return;
            }

            float angle = Vector3.Angle(head.forward, toObject);

            // Hysteresis identical to DelayedGazeGlow: once we've started
            // accumulating gaze, allow a wider exit cone so micro-jitter
            // doesn't reset us.
            float threshold = _continuousGazeTime > 0f ? _exitAngleDegrees : _enterAngleDegrees;
            bool looking = angle <= threshold;

            if (looking)
            {
                _continuousGazeTime += Time.deltaTime;
                _lastDistance = toObject.magnitude;
            }
            else
            {
                EndGaze();
            }
        }

        /// <summary>
        /// Ends the current gaze. If it exceeded the allowed time, reports the
        /// freeze (duration + end-distance) to the FreezeDetector registry.
        /// </summary>
        private void EndGaze()
        {
            if (_continuousGazeTime <= 0f)
                return;

            if (_continuousGazeTime >= _allowedGazeSeconds)
            {
                FreezeDetector detector = ResolveDetector();
                if (detector != null)
                {
                    detector.ReportGazeFreeze(gameObject.name, _continuousGazeTime, _lastDistance);

                    if (_verboseLogs)
                    {
                        Debug.Log(
                            $"[{nameof(GazeFreezeReporter)}] Freeze on '{name}': " +
                            $"{_continuousGazeTime:0.##}s at {_lastDistance:0.##}m.",
                            this);
                    }
                }
                else if (_verboseLogs)
                {
                    Debug.LogWarning(
                        $"[{nameof(GazeFreezeReporter)}] No FreezeDetector found; freeze on '{name}' not recorded.",
                        this);
                }
            }

            _continuousGazeTime = 0f;
        }

        private FreezeDetector ResolveDetector()
        {
            if (_freezeDetector != null)
                return _freezeDetector;

            _freezeDetector = FreezeDetector.Instance;
            if (_freezeDetector == null)
                _freezeDetector = FindObjectOfType<FreezeDetector>();

            return _freezeDetector;
        }

        private Transform ResolveHead()
        {
            if (_headTransformOverride != null)
            {
                _cachedHead = _headTransformOverride;
                return _cachedHead;
            }

            FreezeDetector detector = ResolveDetector();
            if (detector != null && detector.HeadTransform != null)
            {
                _cachedHead = detector.HeadTransform;
                return _cachedHead;
            }

            if (_cachedHead != null)
                return _cachedHead;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                _cachedHead = mainCamera.transform;

            return _cachedHead;
        }
    }
}
