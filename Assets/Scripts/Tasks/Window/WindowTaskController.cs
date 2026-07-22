using System;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Tasks.Window
{
    /// <summary>
    /// MVC Controller for the window task. Reads the <see cref="WindowTaskModel"/>,
    /// manages the task state, and broadcasts events (both UnityEvents for Inspector
    /// wiring and C# events for code) that View components react to.
    ///
    /// Open/closed detection is built in: every frame the controller measures the
    /// absolute angular difference between the pivot's current rotation and the
    /// rotation it had at startup (assumed closed) using Quaternion.Angle. This is
    /// axis- and direction-agnostic, so it works regardless of the pivot's initial
    /// orientation (e.g. a 180-degree X flip) or which way the window swings.
    /// </summary>
    public class WindowTaskController : MonoBehaviour
    {
        [Header("Model")]
        [SerializeField] private WindowTaskModel model = new WindowTaskModel();

        [Header("Detection")]
        [Tooltip("The transform that physically rotates when the window opens. Falls back to this transform.")]
        [SerializeField] private Transform windowPivot;

        [Tooltip("Degrees away from the closed rotation to count as OPEN.")]
        [SerializeField] private float openAngleThreshold = 45f;

        [Tooltip("Degrees away from the closed rotation to count as CLOSED again (must be below the open threshold).")]
        [SerializeField] private float closedAngleThreshold = 8f;

        [Header("UnityEvents (wire in Inspector)")]
        public UnityEvent OnRequestOpen;
        public UnityEvent OnWindowOpened;
        public UnityEvent OnWindowClosed;
        public UnityEvent OnCloseAttempt;
        public UnityEvent<WindowTaskResult> OnTaskReported;

        // C# events (subscribe in code)
        public event Action RequestOpen;
        public event Action WindowOpened;
        public event Action WindowClosed;
        public event Action CloseAttempt;
        public event Action<WindowTaskResult> TaskReported;

        public WindowTaskModel Model => model;

        /// <summary>Current angular offset from the closed rotation, in degrees.</summary>
        public float CurrentAngle { get; private set; }

        /// <summary>True while the window is physically detected as open.</summary>
        public bool IsOpen => _isOpen;

        private Quaternion _closedRotation;
        private bool _isOpen;
        private bool _isTaskActive;
        private bool _closed;
        private bool _attempted;
        private float _openTime;
        private float _firstAttemptTime;

        private void Awake()
        {
            if (windowPivot == null)
                windowPivot = transform;

            // The scene starts with the window shut, so whatever rotation the pivot
            // has right now is the "closed" reference.
            _closedRotation = windowPivot.localRotation;
        }

        private void Start()
        {
            if (model.AutoOpenOnStart)
                Invoke(nameof(RequestOpenWindow), model.OpenDelay);
        }

        private void Update()
        {
            CurrentAngle = Quaternion.Angle(_closedRotation, windowPivot.localRotation);

            if (!_isOpen && CurrentAngle > openAngleThreshold)
            {
                _isOpen = true;
                HandleWindowOpened();
            }
            else if (_isOpen && CurrentAngle < closedAngleThreshold)
            {
                _isOpen = false;
                HandleWindowClosed();
            }
        }

        /// <summary>
        /// Requests the window to open. The actual motion is performed by a View
        /// component (e.g. <see cref="WindowMover"/>) listening to this event.
        /// </summary>
        public void RequestOpenWindow()
        {
            OnRequestOpen?.Invoke();
            RequestOpen?.Invoke();
        }

        private void HandleWindowOpened()
        {
            if (_closed)
                return;

            _isTaskActive = true;
            _openTime = Time.time;

            Debug.Log($"[WindowTask] Window opened (angle {CurrentAngle:F0}).", this);

            OnWindowOpened?.Invoke();
            WindowOpened?.Invoke();
        }

        private void HandleWindowClosed()
        {
            if (!_isTaskActive || _closed)
                return;

            _closed = true;

            Debug.Log($"[WindowTask] Window closed (angle {CurrentAngle:F0}).", this);

            OnWindowClosed?.Invoke();
            WindowClosed?.Invoke();

            ReportResult();
        }

        /// <summary>
        /// Wire this to the window handle's InteractableUnityEventWrapper "When Select"
        /// event. Marks that the player attempted to close the window.
        /// </summary>
        public void NotifyCloseAttempt()
        {
            if (!_isTaskActive || _closed)
                return;

            if (!_attempted)
            {
                _attempted = true;
                _firstAttemptTime = Time.time;
            }

            OnCloseAttempt?.Invoke();
            CloseAttempt?.Invoke();
        }

        /// <summary>
        /// Builds the structured result and broadcasts it. Called automatically on close,
        /// and can be called externally (e.g. at session end) to report a window that was
        /// never closed.
        /// </summary>
        public void ReportResult()
        {
            var result = BuildResult();
            OnTaskReported?.Invoke(result);
            TaskReported?.Invoke(result);
        }

        private WindowTaskResult BuildResult()
        {
            var result = new WindowTaskResult();

            if (_closed)
            {
                result.Outcome = WindowTaskOutcome.Closed;
                result.TimeToClose = Time.time - _openTime;
                if (_attempted)
                    result.TimeToFirstAttempt = _firstAttemptTime - _openTime;
            }
            else if (_attempted)
            {
                result.Outcome = WindowTaskOutcome.AttemptedNotClosed;
                result.TimeToFirstAttempt = _firstAttemptTime - _openTime;
            }
            else
            {
                result.Outcome = WindowTaskOutcome.NeverAttempted;
            }

            return result;
        }
    }
}
