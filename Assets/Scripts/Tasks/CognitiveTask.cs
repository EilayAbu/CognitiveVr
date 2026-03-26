using CognitiveVR.Core;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place this MonoBehaviour on any scene object that represents a task.
    /// Select the TaskType from the Inspector dropdown.
    /// All lifecycle reporting flows through TaskApi.
    /// Direct TaskController access is limited to registration and time resolution.
    /// </summary>
    public class CognitiveTask : MonoBehaviour
    {
        [Header("Task Identity")]
        [SerializeField] private TaskType _taskType;

        [Header("Auto Start")]
        [SerializeField] private bool _autoStartOnEnable;

        [Header("Stuck Detection -- Progress Based")]
        [Tooltip("Detect stuck when no ReportProgress/CompleteStep calls within timeout")]
        [SerializeField] private bool _useProgressBasedStuck = true;
        [Tooltip("Override the default timeout from TaskRegistry. 0 = use registry default.")]
        [SerializeField] private float _stuckTimeoutOverride;

        [Header("Stuck Detection -- Freeze Detector")]
        [Tooltip("Also report stuck when the FreezeDetector fires (player not moving)")]
        [SerializeField] private bool _useFreezeDetectorForStuck;
        [SerializeField] private FreezeDetector _freezeDetector;

        [Header("Runtime (Read Only)")]
        [SerializeField] private TaskStatus _status = TaskStatus.NotStarted;

        private float _lastProgressTime = -1f;
        private bool _stuckReported;
        private bool _stuckByFreeze;
        private float _resolvedTimeout;

        public TaskType Type => _taskType;
        public TaskStatus Status => _status;
        public bool IsRunning => _status == TaskStatus.InProgress || _status == TaskStatus.Stuck;
        public bool IsCompleted => _status == TaskStatus.Completed;

        #region Unity Lifecycle

        private void OnEnable()
        {
            TaskController.Instance.RegisterTask(_taskType);
            ResolveTimeout();

            if (_useFreezeDetectorForStuck)
                BindFreezeDetector();

            if (_autoStartOnEnable && _status == TaskStatus.NotStarted)
                StartTask();
        }

        private void OnDisable()
        {
            UnbindFreezeDetector();
        }

        private void Update()
        {
            if (!_useProgressBasedStuck) return;
            if (!IsRunning) return;
            if (_lastProgressTime < 0f) return;
            if (_stuckReported) return;

            float idle = Now - _lastProgressTime;
            if (idle >= _resolvedTimeout)
            {
                _stuckByFreeze = false;
                SetStuck("No progress timeout");
            }
        }

        #endregion

        #region Public API (called by game logic)

        public void StartTask(string message = "")
        {
            if (_status != TaskStatus.NotStarted) return;

            _status = TaskStatus.InProgress;
            _lastProgressTime = Now;
            _stuckReported = false;
            _stuckByFreeze = false;

            TaskApi.ReportStarted(_taskType, message);
        }

        public void ReportProgress(string message = "")
        {
            EnsureStarted();
            _lastProgressTime = Now;

            if (_stuckReported)
            {
                _status = TaskStatus.InProgress;
                _stuckReported = false;
                _stuckByFreeze = false;
                TaskApi.ReportResumed(_taskType, message);
            }

            TaskApi.ReportProgress(_taskType, message);
        }

        public void CompleteStep(string stepId, string message = "")
        {
            EnsureStarted();
            _lastProgressTime = Now;

            if (_stuckReported)
            {
                _status = TaskStatus.InProgress;
                _stuckReported = false;
                _stuckByFreeze = false;
                TaskApi.ReportResumed(_taskType, "Resumed via step completion");
            }

            TaskApi.ReportStepCompleted(_taskType, stepId, message);
        }

        public void CompleteTask(string message = "")
        {
            EnsureStarted();
            _status = TaskStatus.Completed;
            _stuckReported = false;
            _stuckByFreeze = false;

            TaskApi.ReportCompleted(_taskType, message);
        }

        public void FailTask(string reason = "")
        {
            EnsureStarted();
            _status = TaskStatus.Failed;
            _stuckReported = false;
            _stuckByFreeze = false;

            TaskApi.ReportFailed(_taskType, reason);
        }

        public void ForceStuck(string reason = "")
        {
            EnsureStarted();
            _stuckByFreeze = false;
            SetStuck(reason);
        }

        public void ResetTask()
        {
            _status = TaskStatus.NotStarted;
            _lastProgressTime = -1f;
            _stuckReported = false;
            _stuckByFreeze = false;
            TaskController.Instance.ResetTask(_taskType);
        }

        #endregion

        #region Freeze Detector Integration

        private void BindFreezeDetector()
        {
            if (_freezeDetector == null)
                _freezeDetector = FindObjectOfType<FreezeDetector>();

            if (_freezeDetector == null) return;

            _freezeDetector.OnFreezeStarted += HandleFreezeStarted;
            _freezeDetector.OnFreezeEnded += HandleFreezeEnded;
        }

        private void UnbindFreezeDetector()
        {
            if (_freezeDetector == null) return;

            _freezeDetector.OnFreezeStarted -= HandleFreezeStarted;
            _freezeDetector.OnFreezeEnded -= HandleFreezeEnded;
        }

        private void HandleFreezeStarted(float duration, Vector3 position)
        {
            if (!IsRunning || _stuckReported) return;
            _stuckByFreeze = true;
            SetStuck($"Player freeze at {position}");
        }

        private void HandleFreezeEnded(float duration, Vector3 position)
        {
            if (_status != TaskStatus.Stuck || !_stuckByFreeze) return;

            _status = TaskStatus.InProgress;
            _stuckReported = false;
            _stuckByFreeze = false;
            _lastProgressTime = Now;
            TaskApi.ReportResumed(_taskType, $"Player resumed movement after {duration:F1}s freeze");
        }

        #endregion

        #region Internal

        private float Now => TaskController.Instance.GetSessionTime();

        private void EnsureStarted()
        {
            if (_status == TaskStatus.NotStarted)
                StartTask();
        }

        private void SetStuck(string reason)
        {
            _status = TaskStatus.Stuck;
            _stuckReported = true;
            TaskApi.ReportStuck(_taskType, reason);
        }

        private void ResolveTimeout()
        {
            if (_stuckTimeoutOverride > 0f)
            {
                _resolvedTimeout = _stuckTimeoutOverride;
                return;
            }

            TaskDefinition def = TaskRegistry.Get(_taskType);
            _resolvedTimeout = def != null ? def.DefaultStuckTimeout : 15f;
        }

        #endregion
    }
}
