using UnityEngine;
using UnityEngine.Events;
using CognitiveVR.Tasks;

namespace CognitiveVR.Core
{
    /// <summary>
    /// Ends the simulation automatically a fixed time after it started.
    ///
    /// "Started" means SessionTimer.StartSession() (the participant pressing
    /// Start), not scene load - the instructions phase is not counted.
    ///
    /// Uses its own clock on purpose: SessionTimer stops counting when its
    /// SessionDuration runs out. In MainScene SessionDuration is 545 s - a
    /// backstop just after this component's 540 s - so data is collected all
    /// the way to the ending, and the exit_timeout row (with the backpack
    /// status) is written by SessionEndTrigger.CompleteByTimeout before the log
    /// closes. Then the regular ending plays (end audio + black screen + message).
    /// If the participant already finished at the exit, the trigger is
    /// one-shot and the timeout does nothing.
    /// </summary>
    public class SimulationAutoEnd : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds after SessionTimer.StartSession() at which the ending plays (9 minutes = 540s).")]
        [SerializeField] private float autoEndAfterSeconds = 540f;
        [Tooltip("Planned end of the task (7 minutes = 420s). Only a marker row is logged here; the session keeps " +
                 "running and logging until Auto End After Seconds. 0 = no marker.")]
        [SerializeField, Min(0f)] private float plannedEndSeconds = 420f;

        [Header("References (auto-found if empty)")]
        [SerializeField] private SessionTimer sessionTimer;
        [SerializeField] private SessionEndTrigger endTrigger;

        [Header("Events")]
        [Tooltip("Fired when the time limit is reached, after the ending is triggered.")]
        public UnityEvent onAutoEnd;

        [Header("Runtime (Read Only)")]
        [SerializeField] private bool _armed;
        [SerializeField] private bool _fired;
        [SerializeField] private bool _plannedEndLogged;
        [SerializeField] private float _elapsed;

        private float _startTime;

        private void Awake()
        {
            if (sessionTimer == null) sessionTimer = FindFirstObjectByType<SessionTimer>();
            if (endTrigger == null) endTrigger = FindFirstObjectByType<SessionEndTrigger>();

            if (sessionTimer == null)
                Debug.LogWarning($"[{nameof(SimulationAutoEnd)}] No SessionTimer in scene - auto end will never arm.", this);
            if (endTrigger == null)
                Debug.LogWarning($"[{nameof(SimulationAutoEnd)}] No SessionEndTrigger in scene - nothing to play at the time limit.", this);
        }

        private void OnEnable()
        {
            if (sessionTimer != null)
            {
                sessionTimer.OnSessionStarted += HandleSessionStarted;

                // Covers enabling this component after the session already started.
                if (sessionTimer.IsRunning && !_armed && !_fired)
                    Arm(Time.time - sessionTimer.ElapsedTime);
            }
        }

        private void OnDisable()
        {
            if (sessionTimer != null)
                sessionTimer.OnSessionStarted -= HandleSessionStarted;
        }

        private void HandleSessionStarted()
        {
            if (_fired) return;
            Arm(Time.time);
        }

        private void Arm(float startTime)
        {
            _startTime = startTime;
            _elapsed = Time.time - startTime;
            _armed = true;
        }

        private void Update()
        {
            if (!_armed) return;

            _elapsed = Time.time - _startTime;

            if (!_plannedEndLogged && plannedEndSeconds > 0f && _elapsed >= plannedEndSeconds)
            {
                _plannedEndLogged = true;
                var manager = CognitiveVR.Data.ExperimentDataManager.Instance;
                // Skipped if the participant already left: the log is closed by then.
                if (manager != null && !manager.IsLoggingClosed)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    manager.Log("session", "planned_end_reached", "", _elapsed,
                        $"planned_end_s={plannedEndSeconds.ToString("F0", inv)}" +
                        $"|auto_end_at_s={autoEndAfterSeconds.ToString("F0", inv)}");
                }
            }

            if (_elapsed < autoEndAfterSeconds) return;

            _armed = false;
            _fired = true;

            Debug.Log($"[{nameof(SimulationAutoEnd)}] Time limit reached ({autoEndAfterSeconds:F0}s since start) - playing end sequence.", this);

            if (endTrigger != null)
                endTrigger.CompleteByTimeout();

            onAutoEnd?.Invoke();
        }
    }
}
