using CognitiveVR.Core;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Detects "staring at task note without movement" and reports it as INIT_1 stuck.
    /// According to the specification, prolonged no-action after scene start maps to initiation weakness.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class TaskNoteGazeTask : CognitiveTask
    {
        [Header("Gaze Detection")]
        [SerializeField] private Transform _headTransform;
        [SerializeField] private float _maxGazeDistance = 3f;
        [SerializeField] private float _maxViewAngle = 15f;

        [Header("Freeze Validation")]
        [SerializeField] private FreezeDetector _gazeFreezeDetector;
        [SerializeField] private float _minGazeAndFreezeSeconds = 3f;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = true;

        private bool _isGazing;
        private float _gazeStartAt = -1f;
        private bool _reportedCurrentGaze;
        private Collider _noteCollider;

        private void Awake()
        {
            _noteCollider = GetComponent<Collider>();
            ResolveReferences();
        }

        private void LateUpdate()
        {
            if (_noteCollider == null) return;
            if (_headTransform == null) return;

            bool lookingAtNote = IsLookingAtNote();

            if (lookingAtNote && !_isGazing)
            {
                _isGazing = true;
                _gazeStartAt = Time.time;
                _reportedCurrentGaze = false;
                StartTask("Started gazing at task note");

                if (_verboseLogs)
                    Debug.Log("[TaskNoteGazeTask] Started gaze at task note.");
            }
            else if (!lookingAtNote && _isGazing)
            {
                _isGazing = false;
                _gazeStartAt = -1f;
                _reportedCurrentGaze = false;
                ReportProgress("Stopped gazing at task note");

                if (_verboseLogs)
                    Debug.Log("[TaskNoteGazeTask] Ended gaze at task note.");
            }

            if (!_isGazing || _reportedCurrentGaze) return;
            if (_gazeStartAt < 0f) return;

            float gazeDuration = Time.time - _gazeStartAt;
            bool isFrozen = _gazeFreezeDetector != null && _gazeFreezeDetector.IsFrozen;

            if (gazeDuration >= _minGazeAndFreezeSeconds && isFrozen)
            {
                // Spec mapping: prolonged no-action while focused at start => INIT_1 (Initiation weakness).
                string reason = "INIT_1 initiation failure: prolonged stare at task note without movement";
                ForceStuck(reason);
                _reportedCurrentGaze = true;

                if (_verboseLogs)
                    Debug.Log($"[TaskNoteGazeTask] Reported stuck ({reason}), duration={gazeDuration:F1}s");
            }
        }

        private bool IsLookingAtNote()
        {
            Vector3 toNote = _noteCollider.bounds.center - _headTransform.position;
            float distance = toNote.magnitude;
            if (distance > _maxGazeDistance) return false;

            float angle = Vector3.Angle(_headTransform.forward, toNote.normalized);
            if (angle > _maxViewAngle) return false;

            Ray ray = new Ray(_headTransform.position, _headTransform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, _maxGazeDistance))
                return hit.collider == _noteCollider;

            return false;
        }

        private void ResolveReferences()
        {
            if (_headTransform == null && Camera.main != null)
                _headTransform = Camera.main.transform;

            if (_gazeFreezeDetector == null)
                _gazeFreezeDetector = FindObjectOfType<FreezeDetector>();
        }
    }
}
