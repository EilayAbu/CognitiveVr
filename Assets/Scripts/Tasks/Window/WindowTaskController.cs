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
    /// Detection of the physical open/closed state comes from <see cref="DoorStateEvents"/>.
    /// </summary>
    public class WindowTaskController : MonoBehaviour
    {
        [Header("Model")]
        [SerializeField] private WindowTaskModel model = new WindowTaskModel();

        [Header("References")]
        [Tooltip("Reports the physical open/closed state of the window.")]
        [SerializeField] private DoorStateEvents doorStateEvents;

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

        private bool _isTaskActive;
        private bool _closed;
        private bool _attempted;
        private float _openTime;
        private float _firstAttemptTime;

        private void Start()
        {
            if (model.AutoOpenOnStart)
                Invoke(nameof(RequestOpenWindow), model.OpenDelay);
        }

        private void OnEnable()
        {
            if (doorStateEvents != null)
            {
                doorStateEvents.DoorOpened += HandleWindowOpened;
                doorStateEvents.DoorClosed += HandleWindowClosed;
            }
        }

        private void OnDisable()
        {
            if (doorStateEvents != null)
            {
                doorStateEvents.DoorOpened -= HandleWindowOpened;
                doorStateEvents.DoorClosed -= HandleWindowClosed;
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

            OnWindowOpened?.Invoke();
            WindowOpened?.Invoke();
        }

        private void HandleWindowClosed()
        {
            if (!_isTaskActive || _closed)
                return;

            _closed = true;

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
