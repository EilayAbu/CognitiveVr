using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using CognitiveVR.Data;

/// <summary>
/// Pipes GuideActionController (neighbor task) activity into the experiment
/// CSV. Purely a subscriber - it never drives the scenario, only reads it.
/// Drop it on the same GameObject as the GuideActionController; no Inspector
/// wiring needed.
///
/// Logged under category "task":
///   guide_mission_start - scheduled event fired, ticking started
///   guide_tick          - each tick/knock reminder. value = tick number
///   guide_door_opened   - door opened. value = seconds since mission start
///   guide_ask_start     - the neighbor started talking (scenario started)
///   guide_choice_shown  - the choice canvas was displayed
///   guide_choice        - the decision. details = choice=agree|refuse,
///                         value = seconds from choice shown to click
///   guide_scenario_end  - scenario finished (guide leaves)
///
/// The same events accumulate into a <see cref="GuideTaskSummary"/> which
/// ExperimentDataManager embeds in the session summary JSON. Read it via
/// <see cref="BuildSummary"/>.
///
/// All timestamps are t_logger_s - the CSV clock shared by every task bridge.
/// </summary>
[RequireComponent(typeof(GuideActionController))]
public class GuideDataBridge : MonoBehaviour
{
    [Tooltip("Name used in the 'object' column. Keep it identical to the guide's gaze name so the rows join in the summary.")]
    [SerializeField] private string logName = "Guide";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private GuideActionController _guide;

    private readonly GuideTaskSummary _summary = new GuideTaskSummary();

    private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

    private void Awake()
    {
        _guide = GetComponent<GuideActionController>();
    }

    private void OnEnable()
    {
        // Push our summary into the manager singleton. By the time this object
        // becomes active the manager already exists, so no lookup is needed and
        // the manager never has to find this (initially inactive) component.
        Manager?.RegisterGuideSummary(_summary);

        _guide.OnMissionStarted += HandleMissionStarted;
        _guide.OnTickPlayed += HandleTickPlayed;
        _guide.OnMissionDoorOpenedEvent += HandleMissionDoorOpened;
        _guide.OnScenarioStarted += HandleScenarioStarted;
        _guide.OnChoiceShown += HandleChoiceShown;
        _guide.OnChoiceMade += HandleChoiceMade;
        _guide.OnScenarioEnded += HandleScenarioEnded;
    }

    private void OnDisable()
    {
        _guide.OnMissionStarted -= HandleMissionStarted;
        _guide.OnTickPlayed -= HandleTickPlayed;
        _guide.OnMissionDoorOpenedEvent -= HandleMissionDoorOpened;
        _guide.OnScenarioStarted -= HandleScenarioStarted;
        _guide.OnChoiceShown -= HandleChoiceShown;
        _guide.OnChoiceMade -= HandleChoiceMade;
        _guide.OnScenarioEnded -= HandleScenarioEnded;
    }

    // ------------------------------------------------------------------ //
    // Handlers
    // ------------------------------------------------------------------ //

    private void HandleMissionStarted()
    {
        float now = Now();
        if (_summary.missionStartedAt < 0f)
            _summary.missionStartedAt = now;

        Manager?.Log("task", "guide_mission_start", logName, null, null);
        RecordEvent("mission_start", null);
    }

    private void HandleTickPlayed(int tickNumber)
    {
        _summary.tickCount = Mathf.Max(_summary.tickCount, tickNumber);

        Manager?.Log("task", "guide_tick", logName, tickNumber, $"tick={tickNumber}");
        RecordEvent("tick", $"tick={tickNumber}");
    }

    private void HandleMissionDoorOpened()
    {
        float now = Now();
        float? sinceMissionStart = null;

        if (_summary.doorOpenedAt < 0f)
        {
            _summary.doorOpenedAt = now;
            if (_summary.missionStartedAt >= 0f && now >= 0f)
                _summary.timeToOpenDoorSeconds = now - _summary.missionStartedAt;
        }

        if (_summary.missionStartedAt >= 0f && now >= 0f)
            sinceMissionStart = now - _summary.missionStartedAt;

        Manager?.Log("task", "guide_door_opened", logName, sinceMissionStart,
            sinceMissionStart.HasValue
                ? $"since_mission_start_s={sinceMissionStart.Value.ToString("F2", Inv)}"
                : null);
        RecordEvent("door_opened",
            sinceMissionStart.HasValue
                ? $"since_mission_start_s={sinceMissionStart.Value.ToString("F2", Inv)}"
                : null);
    }

    private void HandleScenarioStarted()
    {
        float now = Now();
        if (_summary.scenarioStartedAt < 0f)
            _summary.scenarioStartedAt = now;

        Manager?.Log("task", "guide_ask_start", logName, null, null);
        RecordEvent("ask_start", null);
    }

    private void HandleChoiceShown()
    {
        float now = Now();
        if (_summary.choiceShownAt < 0f)
            _summary.choiceShownAt = now;

        Manager?.Log("task", "guide_choice_shown", logName, null, null);
        RecordEvent("choice_shown", null);
    }

    private void HandleChoiceMade(bool agreed)
    {
        float now = Now();
        float? decisionSeconds = null;

        if (_summary.choiceShownAt >= 0f && now >= 0f)
            decisionSeconds = now - _summary.choiceShownAt;

        if (_summary.choiceMadeAt < 0f)
        {
            _summary.choiceMadeAt = now;
            if (decisionSeconds.HasValue)
                _summary.decisionSeconds = decisionSeconds.Value;
        }

        _summary.agreedToHelp = agreed;
        _summary.choice = agreed ? "agree" : "refuse";

        string details = $"choice={_summary.choice}";
        if (decisionSeconds.HasValue)
            details += $"|decision_s={decisionSeconds.Value.ToString("F2", Inv)}";

        Manager?.Log("task", "guide_choice", logName, decisionSeconds, details);
        RecordEvent(agreed ? "choice_agree" : "choice_refuse", details);
    }

    private void HandleScenarioEnded(bool agreed)
    {
        float now = Now();
        if (_summary.scenarioEndedAt < 0f)
            _summary.scenarioEndedAt = now;

        _summary.scenarioCompleted = true;

        Manager?.Log("task", "guide_scenario_end", logName, null,
            $"choice={_summary.choice}");
        RecordEvent("scenario_end", $"choice={_summary.choice}");
    }

    // ------------------------------------------------------------------ //
    // JSON summary
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Snapshot of the neighbor task for the session summary JSON. Everything
    /// is accumulated live, so this just returns the current state.
    /// </summary>
    public GuideTaskSummary BuildSummary()
    {
        return _summary;
    }

    private void RecordEvent(string eventName, string details)
    {
        // Safety: if the manager was not ready when we first enabled, make sure it
        // is holding our summary before the first event lands in it.
        Manager?.RegisterGuideSummary(_summary);

        _summary.events.Add(new GuideEvent
        {
            eventName = eventName,
            tLoggerSeconds = Now(),
            details = details ?? ""
        });
    }

    /// <summary>
    /// Logger seconds (t_logger_s) - the SAME clock as every other task bridge
    /// and the CSV's second column. This bridge previously used SessionElapsed,
    /// which made guideTask the only summary block on the session clock: its
    /// timestamps read 17.4s earlier than the toaster/key blocks for the same
    /// wall moment. Convert to session time in analysis via
    /// summary.sessionStartLoggerSeconds if needed.
    /// </summary>
    private static float Now()
    {
        return Manager != null ? Manager.LoggerElapsed : -1f;
    }

    [Serializable]
    public class GuideEvent
    {
        public string eventName;
        public float tLoggerSeconds;
        public string details;
    }

    [Serializable]
    public class GuideTaskSummary
    {
        // Counters.
        public int tickCount;

        // Key timestamps (t_logger_s clock, same as the CSV and every other
        // task block). -1 = never. Note: missionStartedAt = -1 is EXPECTED in
        // short test runs - the neighbor_knock scheduled event fires at 180s,
        // so opening the door before then legitimately leaves it unset.
        public float missionStartedAt = -1f;
        public float doorOpenedAt = -1f;
        public float scenarioStartedAt = -1f;
        public float choiceShownAt = -1f;
        public float choiceMadeAt = -1f;
        public float scenarioEndedAt = -1f;

        // Derived metrics. -1 = not measured.
        public float timeToOpenDoorSeconds = -1f;
        public float decisionSeconds = -1f;

        // Outcome.
        public bool agreedToHelp;
        public string choice = "none";
        public bool scenarioCompleted;

        // Full timeline of everything that happened with the neighbor.
        public List<GuideEvent> events = new List<GuideEvent>();
    }
}