using System;
using System.Globalization;
using UnityEngine;
using CognitiveVR.Core;
using CognitiveVR.Data;
using CognitiveVR.Tasks.Window;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Treats window-closing and puddle-cleaning as ONE combined task and
    /// reports them as a single block. Purely a subscriber: it listens to the
    /// existing WindowTaskController events, the window's DoorStateEvents and
    /// the PuddleCleaner UnityEvents without modifying any of them, so
    /// PuddleDataBridge and any Inspector wiring keep working untouched.
    ///
    /// The task clock starts when the window is requested to open
    /// (RequestOpenWindow). From that anchor the bridge measures:
    ///   - how long until the player's FIRST action (window grab or first wipe)
    ///     and which system it was,
    ///   - whether each part was fully completed (window fully closed /
    ///     puddle fully cleaned) and when,
    ///   - which part was completed first.
    ///
    /// Window open/closed detection reads <see cref="DoorStateEvents"/> directly
    /// (angle-threshold based), so it works even if the WindowTaskController's own
    /// task-active chain is not fully wired. The controller events are kept as a
    /// backup source; guards make sure nothing is double-counted.
    ///
    /// All timestamps use <see cref="SessionTimer.ElapsedTime"/> (seconds since
    /// the session started), so they line up with the rest of the experiment.
    ///
    /// Logged under category "task":
    ///   rain_task_start          - RequestOpenWindow fired. Task clock zero.
    ///   rain_task_first_action   - first action. value = seconds since start,
    ///                              details carry first=window|puddle.
    ///   rain_task_window_closed  - window fully closed. value = seconds since start.
    ///   rain_task_puddle_cleaned - puddle fully cleaned. value = seconds since start.
    ///   rain_task_complete       - both parts done. value = seconds since start,
    ///                              details carry the completion order.
    ///
    /// The same data is accumulated into a <see cref="WindowPuddleTaskSummary"/>
    /// which ExperimentDataManager embeds in the session summary JSON. Read it
    /// via <see cref="BuildSummary"/>.
    /// </summary>
    public class WindowPuddleTaskBridge : MonoBehaviour
    {
        [Header("References (drag in Inspector)")]
        [Tooltip("The window task controller whose RequestOpen anchors the task clock.")]
        [SerializeField] private WindowTaskController windowController;
        [Tooltip("Reports the physical open/closed state of the window. Auto-found on the controller's GameObject if left empty.")]
        [SerializeField] private DoorStateEvents doorStateEvents;
        [Tooltip("The puddle that must be cleaned as the second half of the task.")]
        [SerializeField] private PuddleCleaner puddle;
        [Tooltip("Source of the session clock. Auto-found in the scene if left empty.")]
        [SerializeField] private SessionTimer sessionTimer;

        [Tooltip("Name used in the 'object' column of the CSV rows.")]
        [SerializeField] private string logName = "RainTask";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly WindowPuddleTaskSummary _summary = new WindowPuddleTaskSummary();

        private bool _completeLogged;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            if (doorStateEvents == null && windowController != null)
                doorStateEvents = windowController.GetComponent<DoorStateEvents>();

            if (sessionTimer == null)
                sessionTimer = FindFirstObjectByType<SessionTimer>();
        }

        private void OnEnable()
        {
            if (windowController != null)
            {
                windowController.RequestOpen += HandleTaskStart;
                windowController.CloseAttempt += HandleWindowAttempt;
                windowController.WindowClosed += HandleWindowClosed;
            }
            else
            {
                Debug.LogWarning($"[{nameof(WindowPuddleTaskBridge)}] No WindowTaskController assigned.", this);
            }

            if (doorStateEvents != null)
            {
                doorStateEvents.DoorOpened += HandleWindowOpened;
                doorStateEvents.DoorClosed += HandleWindowClosed;
            }
            else
            {
                Debug.LogWarning($"[{nameof(WindowPuddleTaskBridge)}] No DoorStateEvents assigned; window open/closed will rely on the controller only.", this);
            }

            if (puddle != null)
            {
                puddle.onWipe.AddListener(HandleWipe);
                puddle.onCleaned.AddListener(HandlePuddleCleaned);
            }
            else
            {
                Debug.LogWarning($"[{nameof(WindowPuddleTaskBridge)}] No PuddleCleaner assigned.", this);
            }
        }

        private void OnDisable()
        {
            if (windowController != null)
            {
                windowController.RequestOpen -= HandleTaskStart;
                windowController.CloseAttempt -= HandleWindowAttempt;
                windowController.WindowClosed -= HandleWindowClosed;
            }

            if (doorStateEvents != null)
            {
                doorStateEvents.DoorOpened -= HandleWindowOpened;
                doorStateEvents.DoorClosed -= HandleWindowClosed;
            }

            if (puddle != null)
            {
                puddle.onWipe.RemoveListener(HandleWipe);
                puddle.onCleaned.RemoveListener(HandlePuddleCleaned);
            }
        }

        // ------------------------------------------------------------------ //
        // Event handlers
        // ------------------------------------------------------------------ //

        private void HandleTaskStart()
        {
            if (_summary.taskStartedAt >= 0f) return; // only the first open anchors the clock

            _summary.taskStartedAt = Now();

            Manager?.Log("task", "rain_task_start", logName, null, "trigger=request_open_window");
        }

        private void HandleWindowOpened()
        {
            if (_summary.windowOpenedAt >= 0f) return;

            _summary.windowOpenedAt = Now();

            Manager?.Log("task", "rain_task_window_opened", logName, SinceStart(_summary.windowOpenedAt),
                $"since_task_start_s={SinceStart(_summary.windowOpenedAt).ToString("F2", Inv)}");
        }

        private void HandleWindowAttempt()
        {
            if (_summary.windowStartedAt >= 0f) return;

            _summary.windowStartedAt = Now();
            RegisterFirstAction("window");
        }

        private void HandleWipe()
        {
            _summary.wipeCount++;

            if (_summary.puddleStartedAt >= 0f) return;

            _summary.puddleStartedAt = Now();
            RegisterFirstAction("puddle");
        }

        private void HandleWindowClosed()
        {
            if (_summary.windowClosed) return;

            _summary.windowClosed = true;
            _summary.windowClosedAt = Now();
            _summary.timeToCloseWindow = SinceStart(_summary.windowClosedAt);

            // A full close is also proof the player acted on the window, in case the
            // grab-based CloseAttempt was never wired.
            if (_summary.windowStartedAt < 0f)
                _summary.windowStartedAt = _summary.windowClosedAt;
            RegisterFirstAction("window");

            RegisterCompletion("window");

            Manager?.Log("task", "rain_task_window_closed", logName, _summary.timeToCloseWindow,
                $"since_task_start_s={_summary.timeToCloseWindow.ToString("F2", Inv)}");

            CheckBothComplete();
        }

        private void HandlePuddleCleaned()
        {
            if (_summary.puddleCleaned) return;

            _summary.puddleCleaned = true;
            _summary.puddleCleanedAt = Now();
            _summary.timeToCleanPuddle = SinceStart(_summary.puddleCleanedAt);

            RegisterCompletion("puddle");

            Manager?.Log("task", "rain_task_puddle_cleaned", logName, _summary.timeToCleanPuddle,
                $"since_task_start_s={_summary.timeToCleanPuddle.ToString("F2", Inv)}" +
                $"|wipes={_summary.wipeCount}");

            CheckBothComplete();
        }

        // ------------------------------------------------------------------ //
        // Internals
        // ------------------------------------------------------------------ //

        private void RegisterFirstAction(string action)
        {
            if (!string.IsNullOrEmpty(_summary.firstAction)) return;

            _summary.firstAction = action;
            float now = Now();
            _summary.timeToFirstAction = SinceStart(now);

            Manager?.Log("task", "rain_task_first_action", logName, _summary.timeToFirstAction,
                $"first={action}" +
                $"|since_task_start_s={_summary.timeToFirstAction.ToString("F2", Inv)}");
        }

        private void RegisterCompletion(string part)
        {
            if (_summary.firstCompleted == "none")
                _summary.firstCompleted = part;
        }

        private void CheckBothComplete()
        {
            if (_completeLogged || !_summary.windowClosed || !_summary.puddleCleaned) return;

            _completeLogged = true;
            _summary.bothCompleted = true;

            float completedAt = Mathf.Max(_summary.windowClosedAt, _summary.puddleCleanedAt);
            float sinceStart = SinceStart(completedAt);

            string order = _summary.firstCompleted == "window"
                ? "window_then_puddle"
                : "puddle_then_window";

            Manager?.Log("task", "rain_task_complete", logName, sinceStart,
                $"order={order}" +
                $"|first_action={_summary.firstAction}" +
                $"|window_s={_summary.timeToCloseWindow.ToString("F2", Inv)}" +
                $"|puddle_s={_summary.timeToCleanPuddle.ToString("F2", Inv)}");
        }

        /// <summary>Seconds since the task started, or -1 when the task never started.</summary>
        private float SinceStart(float at)
        {
            if (_summary.taskStartedAt < 0f || at < 0f) return -1f;
            return at - _summary.taskStartedAt;
        }

        /// <summary>
        /// Current session time in seconds. Uses <see cref="SessionTimer.ElapsedTime"/>
        /// when available so the numbers match the rest of the experiment, falling
        /// back to the logger clock only if no timer is present.
        /// </summary>
        private float Now()
        {
            if (sessionTimer != null) return sessionTimer.ElapsedTime;
            return Manager != null ? Manager.LoggerElapsed : -1f;
        }

        // ------------------------------------------------------------------ //
        // JSON summary
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Snapshot of the combined task for the session summary JSON.
        /// All timestamps use the session clock (SessionTimer.ElapsedTime); -1 = never.
        /// </summary>
        public WindowPuddleTaskSummary BuildSummary()
        {
            return _summary;
        }

        [Serializable]
        public class WindowPuddleTaskSummary
        {
            // Anchor: RequestOpenWindow.
            public float taskStartedAt = -1f;

            // First action after the window opened: "window", "puddle" or "".
            public string firstAction = "";
            public float timeToFirstAction = -1f;

            // Window half.
            public float windowOpenedAt = -1f;    // physical open detected (usually the auto-open)
            public float windowStartedAt = -1f;   // first close attempt (grab), or full-close fallback
            public bool windowClosed;
            public float windowClosedAt = -1f;
            public float timeToCloseWindow = -1f;

            // Puddle half.
            public float puddleStartedAt = -1f;   // first wipe
            public bool puddleCleaned;
            public float puddleCleanedAt = -1f;
            public float timeToCleanPuddle = -1f;
            public int wipeCount;

            // Order and overall outcome.
            public string firstCompleted = "none"; // "window", "puddle" or "none"
            public bool bothCompleted;
        }
    }
}
