using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Delayed gaze relay.
    ///
    /// Watches the player's gaze on a "trigger" object (A). Once the player has
    /// continuously looked at A for <see cref="_requiredGazeSeconds"/>, schedules
    /// a one-shot timer that, after <see cref="_delaySeconds"/> seconds, enables
    /// the <see cref="GazeGlow"/> on a "target" object (B).
    ///
    /// When B is grabbed (Oculus.Interaction <see cref="Grabbable"/> raises a
    /// <see cref="PointerEventType.Select"/>), B's <see cref="GazeGlow"/> is
    /// disabled and this relay turns itself off.
    /// </summary>
    [DisallowMultipleComponent]
    public class DelayedGazeGlow : MonoBehaviour
    {
        [Header("Detection (Trigger - object A)")]
        [Tooltip("Optional override. Leave empty to auto-use Camera.main transform.")]
        [SerializeField] private Transform _headTransformOverride;

        [Tooltip("Object A: the player has to look at this object to start the relay.")]
        [SerializeField] private Transform _triggerObject;

        [Tooltip("Looking-at angle (degrees from camera forward) to count gaze on A.")]
        [Range(0f, 90f)]
        [SerializeField] private float _enterAngleDegrees = 12f;

        [Tooltip("Looking-away angle (must be >= enter angle) before gaze on A is dropped.")]
        [Range(0f, 90f)]
        [SerializeField] private float _exitAngleDegrees = 18f;

        [SerializeField] private bool _useTriggerDistanceLimit = true;

        [Tooltip("Maximum distance from the camera at which gaze on A counts.")]
        [SerializeField] private float _triggerMaxDistance = 5f;

        [Tooltip("Minimum continuous gaze (in seconds) on A before the delay timer starts.")]
        [SerializeField] private float _requiredGazeSeconds = 1f;

        [Header("Delay")]
        [Tooltip("Seconds to wait after A is registered before enabling B's GazeGlow.")]
        [SerializeField] private float _delaySeconds = 120f;

        [Header("Target (object B)")]
        [Tooltip("GazeGlow on object B. It will be disabled on Awake and enabled after the delay elapses.")]
        [SerializeField] private GazeGlow _targetGazeGlow;

        [Tooltip("Optional: Grabbable on B. If empty, auto-discovered via GetComponentInChildren on the target GazeGlow's GameObject.")]
        [SerializeField] private Grabbable _targetGrabbable;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs;

        private Transform _cachedHead;
        private float _continuousGazeTime;
        private bool _gazeRegistered;
        private bool _delayScheduled;
        private bool _grabbed;

        public bool IsGazeRegistered => _gazeRegistered;
        public bool IsTargetEnabled => _targetGazeGlow != null && _targetGazeGlow.enabled;

        private void Awake()
        {
            if (_targetGazeGlow != null)
            {
                _targetGazeGlow.enabled = false;
            }

            if (_targetGrabbable == null && _targetGazeGlow != null)
            {
                _targetGrabbable = _targetGazeGlow.GetComponentInChildren<Grabbable>();
            }

            if (_targetGazeGlow == null && _verboseLogs)
            {
                Debug.LogWarning(
                    $"[{nameof(DelayedGazeGlow)}] No target GazeGlow assigned on {name}. Relay will be inert.",
                    this);
            }

            ResolveHead();
        }

        private void OnEnable()
        {
            if (_targetGrabbable != null)
            {
                _targetGrabbable.WhenPointerEventRaised += HandleTargetPointerEvent;
            }
        }

        private void OnDisable()
        {
            if (_targetGrabbable != null)
            {
                _targetGrabbable.WhenPointerEventRaised -= HandleTargetPointerEvent;
            }
        }

        private void OnValidate()
        {
            if (_exitAngleDegrees < _enterAngleDegrees)
                _exitAngleDegrees = _enterAngleDegrees;

            if (_requiredGazeSeconds < 0f)
                _requiredGazeSeconds = 0f;

            if (_delaySeconds < 0f)
                _delaySeconds = 0f;

            if (_triggerMaxDistance < 0.05f)
                _triggerMaxDistance = 0.05f;
        }

        private void Update()
        {
            if (_grabbed || _gazeRegistered)
                return;

            UpdateTriggerGaze();
        }

        private void UpdateTriggerGaze()
        {
            Transform head = ResolveHead();
            if (head == null || _triggerObject == null)
            {
                _continuousGazeTime = 0f;
                return;
            }

            Vector3 toObject = _triggerObject.position - head.position;
            float sqr = toObject.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                _continuousGazeTime = 0f;
                return;
            }

            if (_useTriggerDistanceLimit && sqr > _triggerMaxDistance * _triggerMaxDistance)
            {
                _continuousGazeTime = 0f;
                return;
            }

            float angle = Vector3.Angle(head.forward, toObject);

            // Hysteresis identical to GazeGlow: once we've started accumulating
            // gaze, allow a wider exit cone so micro-jitter doesn't reset us.
            float threshold = _continuousGazeTime > 0f ? _exitAngleDegrees : _enterAngleDegrees;
            bool looking = angle <= threshold;

            if (looking)
            {
                _continuousGazeTime += Time.deltaTime;
                if (_continuousGazeTime >= _requiredGazeSeconds)
                {
                    RegisterTriggerGaze();
                }
            }
            else
            {
                _continuousGazeTime = 0f;
            }
        }

        private void RegisterTriggerGaze()
        {
            if (_gazeRegistered)
                return;

            _gazeRegistered = true;

            if (_verboseLogs)
            {
                Debug.Log(
                    $"[{nameof(DelayedGazeGlow)}] Trigger gaze registered on '{(_triggerObject != null ? _triggerObject.name : "<null>")}'. " +
                    $"Waiting {_delaySeconds:0.##}s before enabling target GazeGlow.",
                    this);
            }

            if (!_delayScheduled)
            {
                _delayScheduled = true;
                StartCoroutine(WaitAndEnableTarget());
            }
        }

        private IEnumerator WaitAndEnableTarget()
        {
            if (_delaySeconds > 0f)
            {
                yield return new WaitForSeconds(_delaySeconds);
            }

            if (_grabbed)
                yield break;

            if (_targetGazeGlow != null)
            {
                _targetGazeGlow.enabled = true;

                if (_verboseLogs)
                {
                    Debug.Log(
                        $"[{nameof(DelayedGazeGlow)}] Delay elapsed -> enabled GazeGlow on '{_targetGazeGlow.name}'.",
                        _targetGazeGlow);
                }
            }
        }

        private void HandleTargetPointerEvent(PointerEvent pointerEvent)
        {
            if (pointerEvent.Type != PointerEventType.Select)
                return;

            _grabbed = true;

            if (_targetGazeGlow != null)
            {
                _targetGazeGlow.enabled = false;

                if (_verboseLogs)
                {
                    Debug.Log(
                        $"[{nameof(DelayedGazeGlow)}] Target grabbed -> disabled GazeGlow on '{_targetGazeGlow.name}'.",
                        _targetGazeGlow);
                }
            }

            enabled = false;
        }

        private Transform ResolveHead()
        {
            if (_headTransformOverride != null)
            {
                _cachedHead = _headTransformOverride;
                return _cachedHead;
            }

            if (_cachedHead != null)
                return _cachedHead;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _cachedHead = mainCamera.transform;
            }

            return _cachedHead;
        }
    }
}
