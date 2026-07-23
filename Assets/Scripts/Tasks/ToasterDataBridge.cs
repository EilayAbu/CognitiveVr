using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using CognitiveVR.Data;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Pipes ToasterController activity into the experiment CSV. Purely a
    /// subscriber - it never drives the toaster, only reads it. Drop it on the
    /// same GameObject as the ToasterController; no Inspector wiring needed.
    ///
    /// Logged under category "task":
    ///   toaster_state  - every state transition, value = cooking elapsed.
    ///                    The Done row carries the final burn severity.
    ///   toaster_power  - power on/off
    ///   toaster_lid    - lid opened / closed
    ///   toast_inserted - toast placed inside the toaster
    ///   toast_removed  - toast taken out (temporary removal, not finalization)
    ///
    /// Lid and toast presence are polled rather than event-driven, because the
    /// controller exposes them as UnityEvents only. Both are simple bool reads.
    ///
    /// The same events are also accumulated into a <see cref="ToasterTaskSummary"/>
    /// which ExperimentDataManager embeds in the session summary JSON, next to
    /// the backpack report. Read it via <see cref="BuildSummary"/>.
    /// </summary>
    [RequireComponent(typeof(ToasterController))]
    public class ToasterDataBridge : MonoBehaviour
    {
        [Tooltip("Name used in the 'object' column. Keep it identical to the toaster's gaze name so the rows join in the summary.")]
        [SerializeField] private string logName = "Toaster";

        [Tooltip("Log lid open/close transitions.")]
        [SerializeField] private bool trackLid = true;

        [Tooltip("Log toast inserted/removed transitions.")]
        [SerializeField] private bool trackToastPresence = true;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private ToasterController _toaster;

        private bool _lidWasOpen;
        private bool _toastWasInside;
        private bool _initialized;

        private readonly ToasterTaskSummary _summary = new ToasterTaskSummary();

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            _toaster = GetComponent<ToasterController>();
        }

        private void OnEnable()
        {
            _toaster.OnStateChanged += HandleStateChanged;
            _toaster.OnPowerChanged += HandlePowerChanged;
        }

        private void OnDisable()
        {
            _toaster.OnStateChanged -= HandleStateChanged;
            _toaster.OnPowerChanged -= HandlePowerChanged;
        }

        private void Update()
        {
            bool lidOpen = _toaster.IsLidOpen();
            bool toastInside = _toaster.ToastInside;

            // First frame just captures the baseline - no phantom rows at startup.
            if (!_initialized)
            {
                _lidWasOpen = lidOpen;
                _toastWasInside = toastInside;
                _initialized = true;
                return;
            }

            if (trackLid && lidOpen != _lidWasOpen)
            {
                _lidWasOpen = lidOpen;
                Manager?.Log("task", "toaster_lid", logName, _toaster.CookingElapsed,
                    lidOpen ? "lid=open" : "lid=closed");

                if (lidOpen)
                {
                    _summary.lidOpenCount++;

                    // A "check" = opening the lid while something is actually cooking.
                    bool cooking = _toaster.State >= ToasterState.Cooking
                                   && _toaster.State <= ToasterState.Burnt;
                    if (cooking)
                    {
                        _summary.checkCount++;
                        if (_summary.firstCheckAt < 0f)
                            _summary.firstCheckAt = LoggerNow();
                    }

                    RecordEvent("lid_opened", $"state={_toaster.State}");
                }
                else
                {
                    _summary.lidCloseCount++;
                    RecordEvent("lid_closed", $"state={_toaster.State}");
                }
            }

            if (trackToastPresence && toastInside != _toastWasInside)
            {
                _toastWasInside = toastInside;
                Manager?.Log("task", toastInside ? "toast_inserted" : "toast_removed",
                    logName, _toaster.CookingElapsed,
                    $"state={_toaster.State}|lid={(lidOpen ? "open" : "closed")}");

                if (toastInside) _summary.toastInsertCount++;
                else _summary.toastRemoveCount++;

                RecordEvent(toastInside ? "toast_inserted" : "toast_removed",
                    $"state={_toaster.State}|lid={(lidOpen ? "open" : "closed")}");

                // Completion. The controller's Done state never fires from a
                // normal insert-cook-remove cycle (run 160758: a perfect cycle
                // still ended with doneAt=-1), so the natural end of the task is
                // defined here: taking the toast OUT after cooking has finished.
                // Outcome fields are snapshotted at this moment - the established
                // finalize pattern - so nothing the toaster does afterwards can
                // rewrite the result.
                if (!toastInside && _summary.doneAt < 0f &&
                    (_toaster.State == ToasterState.Ready || _toaster.State == ToasterState.Burnt))
                {
                    _summary.doneAt = LoggerNow();
                    _summary.completedBy = "toast_removed_" + _toaster.State.ToString().ToLowerInvariant();
                    _summary.burnSeverity = _toaster.CurrentBurnSeverity.ToString();
                    _summary.totalCookSeconds = _toaster.CookingElapsed;

                    Manager?.Log("task", "toaster_done", logName, _toaster.CookingElapsed,
                        $"severity={_toaster.CurrentBurnSeverity}|state={_toaster.State}" +
                        $"|cook_s={_toaster.CookingElapsed.ToString("F2", Inv)}");
                    RecordEvent("done",
                        $"severity={_toaster.CurrentBurnSeverity}|state={_toaster.State}");
                }
            }
        }

        private void HandleStateChanged(ToasterState state)
        {
            string details = $"state={state}|powered={(_toaster.IsPoweredOn ? 1 : 0)}";

            // The finalizing transition carries the outcome measure.
            if (state == ToasterState.Done)
            {
                details += $"|severity={_toaster.CurrentBurnSeverity}" +
                           $"|cook_s={_toaster.CookingElapsed.ToString("F2", Inv)}";
            }

            Manager?.Log("task", "toaster_state", logName, _toaster.CookingElapsed, details);

            float now = LoggerNow();
            switch (state)
            {
                case ToasterState.Cooking:
                    if (_summary.activatedAt < 0f) _summary.activatedAt = now;
                    break;
                case ToasterState.Ready:
                    if (_summary.toastReadyAt < 0f) _summary.toastReadyAt = now;
                    break;
                case ToasterState.Done:
                    if (_summary.doneAt < 0f)
                    {
                        _summary.doneAt = now;
                        _summary.completedBy = "controller_done";
                        _summary.burnSeverity = _toaster.CurrentBurnSeverity.ToString();
                        _summary.totalCookSeconds = _toaster.CookingElapsed;
                    }
                    break;
            }

            RecordEvent($"state_{state}", details);
        }

        private void HandlePowerChanged(bool on)
        {
            Manager?.Log("task", "toaster_power", logName, _toaster.CookingElapsed,
                on ? "power=on" : "power=off");

            _summary.powerToggleCount++;
            RecordEvent(on ? "power_on" : "power_off", $"state={_toaster.State}");
        }

        // ------------------------------------------------------------------ //
        // JSON summary
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Snapshot of the toaster task for the session summary JSON. Counters
        /// and the timeline are accumulated live; outcome fields are filled
        /// from the controller at call time.
        /// </summary>
        public ToasterTaskSummary BuildSummary()
        {
            _summary.finalState = _toaster.State.ToString();

            // Only fill outcome fields live if no completion snapshotted them.
            // Once done, the result at the moment of completion is the result.
            if (string.IsNullOrEmpty(_summary.completedBy))
            {
                _summary.burnSeverity = _toaster.CurrentBurnSeverity.ToString();
                _summary.totalCookSeconds = _toaster.CookingElapsed;
            }

            _summary.taskCompleted = _summary.doneAt >= 0f;
            return _summary;
        }

        private void RecordEvent(string eventName, string details)
        {
            _summary.events.Add(new ToasterEvent
            {
                eventName = eventName,
                tLoggerSeconds = LoggerNow(),
                cookingElapsedSeconds = _toaster.CookingElapsed,
                details = details ?? ""
            });
        }

        private static float LoggerNow()
        {
            return Manager != null ? Manager.LoggerElapsed : -1f;
        }

        [Serializable]
        public class ToasterEvent
        {
            public string eventName;
            public float tLoggerSeconds;
            public float cookingElapsedSeconds;
            public string details;
        }

        [Serializable]
        public class ToasterTaskSummary
        {
            // Counters.
            public int lidOpenCount;
            public int lidCloseCount;
            public int toastInsertCount;
            public int toastRemoveCount;
            public int checkCount;
            public int powerToggleCount;

            // Key timestamps (t_logger_s clock, same as the CSV). -1 = never.
            public float activatedAt = -1f;
            public float toastReadyAt = -1f;
            public float firstCheckAt = -1f;
            public float doneAt = -1f;

            // Outcome.
            public string finalState;
            public string burnSeverity;
            public float totalCookSeconds;
            public bool taskCompleted;
            /// <summary>"toast_removed_ready" / "toast_removed_burnt" / "controller_done". Empty = not completed.</summary>
            public string completedBy = "";

            // Full timeline of everything that happened at the toaster.
            public List<ToasterEvent> events = new List<ToasterEvent>();
        }
    }
}