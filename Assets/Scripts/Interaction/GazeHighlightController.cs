using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Performs one gaze raycast per frame and highlights a single matching target.
    /// </summary>
    [DisallowMultipleComponent]
    public class GazeHighlightController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private Transform _headTransform;

        [Header("Detection")]
        [SerializeField] private LayerMask _detectionMask = ~0;
        [SerializeField] private float _maxDistance = 4f;
        [SerializeField] private float _maxViewAngle = 20f;
        [SerializeField] private string _requiredTag = "GazeHighlight";

        [Header("Stability")]
        [SerializeField] private float _switchCooldownSeconds = 0.08f;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs;

        private GazeHighlightTarget _currentTarget;
        private GazeHighlightTarget _pendingTarget;
        private float _pendingSince = -1f;

        private void Awake()
        {
            ResolveHeadTransform();
        }

        private void LateUpdate()
        {
            ResolveHeadTransform();
            if (_headTransform == null)
                return;

            GazeHighlightTarget candidate = DetectCandidate();

            if (candidate == _currentTarget)
            {
                ResetPending();
                return;
            }

            if (_switchCooldownSeconds <= 0f)
            {
                SwitchTarget(candidate);
                return;
            }

            if (_pendingTarget != candidate)
            {
                _pendingTarget = candidate;
                _pendingSince = Time.time;
                return;
            }

            if (_pendingSince >= 0f && (Time.time - _pendingSince) >= _switchCooldownSeconds)
            {
                SwitchTarget(_pendingTarget);
            }
        }

        private void OnDisable()
        {
            ClearCurrentTarget();
            ResetPending();
        }

        private void ResolveHeadTransform()
        {
            if (_headTransform != null)
                return;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _headTransform = mainCamera.transform;
            }
        }

        private GazeHighlightTarget DetectCandidate()
        {
            Vector3 origin = _headTransform.position;
            Vector3 forward = _headTransform.forward;
            Ray ray = new Ray(origin, forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, _maxDistance, _detectionMask, QueryTriggerInteraction.Ignore))
                return null;

            Vector3 toHit = (hit.point - origin);
            if (toHit.sqrMagnitude < 0.0001f)
                return null;

            float angle = Vector3.Angle(forward, toHit.normalized);
            if (angle > _maxViewAngle)
                return null;

            GazeHighlightTarget target = hit.collider.GetComponentInParent<GazeHighlightTarget>();
            if (target == null)
                return null;

            if (!string.IsNullOrWhiteSpace(_requiredTag) && !target.CompareTag(_requiredTag))
                return null;

            return target;
        }

        private void SwitchTarget(GazeHighlightTarget nextTarget)
        {
            if (_currentTarget == nextTarget)
            {
                ResetPending();
                return;
            }

            if (_currentTarget != null)
                _currentTarget.SetHighlighted(false);

            _currentTarget = nextTarget;

            if (_currentTarget != null)
                _currentTarget.SetHighlighted(true);

            if (_verboseLogs)
            {
                string name = _currentTarget != null ? _currentTarget.name : "none";
                Debug.Log($"[{nameof(GazeHighlightController)}] Active highlight target: {name}", this);
            }

            ResetPending();
        }

        private void ClearCurrentTarget()
        {
            if (_currentTarget == null)
                return;

            _currentTarget.SetHighlighted(false);
            _currentTarget = null;
        }

        private void ResetPending()
        {
            _pendingTarget = null;
            _pendingSince = -1f;
        }
    }
}
