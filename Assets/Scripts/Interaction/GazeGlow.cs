using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Self-contained gaze glow:
    /// each instance computes the angle between the main camera forward and
    /// the direction toward this object, and toggles a QuickOutline Outline
    /// component when the player is looking at it.
    /// </summary>
    [DisallowMultipleComponent]
    public class GazeGlow : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Optional override. Leave empty to auto-use Camera.main transform.")]
        [SerializeField] private Transform _headTransformOverride;

        [Tooltip("Looking-at angle to turn the highlight ON (degrees from camera forward).")]
        [Range(0f, 90f)]
        [SerializeField] private float _enterAngleDegrees = 12f;

        [Tooltip("Looking-away angle to turn the highlight OFF (must be >= enter to avoid flicker).")]
        [Range(0f, 90f)]
        [SerializeField] private float _exitAngleDegrees = 18f;

        [SerializeField] private bool _useDistanceLimit = true;
        [Tooltip("Maximum distance from camera at which the highlight can engage.")]
        [SerializeField] private float _maxDistance = 5f;

        [Header("Highlight")]
        [Tooltip("QuickOutline component to toggle. Drag the Outline you added to this object here. If empty, GetComponent<Outline>() is used.")]
        [SerializeField] private Outline _outline;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs;

        private Transform _cachedHead;
        private bool _isHighlighted;

        public bool IsHighlighted => _isHighlighted;

        private void Awake()
        {
            if (_outline == null)
                _outline = GetComponent<Outline>();

            if (_outline != null)
            {
                _outline.enabled = false;
            }
            else if (_verboseLogs)
            {
                Debug.LogWarning(
                    $"[{nameof(GazeGlow)}] No Outline assigned on {name}. Highlight will be inert until one is provided.",
                    this);
            }

            ResolveHead();
        }

        private void OnDisable()
        {
            if (_isHighlighted)
            {
                _isHighlighted = false;
                ApplyHighlightState();
            }
        }

        private void OnValidate()
        {
            if (_exitAngleDegrees < _enterAngleDegrees)
                _exitAngleDegrees = _enterAngleDegrees;

            if (_maxDistance < 0.05f)
                _maxDistance = 0.05f;
        }

        private void LateUpdate()
        {
            Transform head = ResolveHead();
            if (head == null)
                return;

            Vector3 camPos = head.position;
            Vector3 camFwd = head.forward;
            Vector3 toObject = transform.position - camPos;

            float sqr = toObject.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                SetState(false);
                return;
            }

            if (_useDistanceLimit && sqr > _maxDistance * _maxDistance)
            {
                SetState(false);
                return;
            }

            float angle = Vector3.Angle(camFwd, toObject);

            if (!_isHighlighted)
            {
                if (angle <= _enterAngleDegrees)
                    SetState(true);
            }
            else
            {
                if (angle > _exitAngleDegrees)
                    SetState(false);
            }
        }

        private void SetState(bool highlighted)
        {
            if (_isHighlighted == highlighted)
                return;

            _isHighlighted = highlighted;
            ApplyHighlightState();

            if (_verboseLogs)
            {
                Debug.Log($"[{nameof(GazeGlow)}] {name} -> {(highlighted ? "ON" : "OFF")}", this);
            }
        }

        private void ApplyHighlightState()
        {
            if (_outline != null)
                _outline.enabled = _isHighlighted;
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
