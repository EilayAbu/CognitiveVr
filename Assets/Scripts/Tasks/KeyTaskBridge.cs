using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using CognitiveVR.Data;
using CognitiveVR.Interaction;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Reports HOW the player solved the key-on-the-shelf task: knocking the key
    /// down with a tool (umbrella, mop, ...), or climbing the stool and grabbing
    /// it by hand.
    /// Purely a subscriber: it listens to the existing KeyKnockZone and
    /// StoolStandZone events without modifying their behaviour.
    ///
    /// Drop it on any persistent GameObject (e.g. next to ExperimentDataManager)
    /// and drag the zone references in the Inspector.
    ///
    /// Logged under category "task":
    ///   key_umbrella_knock    - tool hit fast enough, key knocked down.
    ///                           value = hit speed (m/s). details carry tool=
    ///                           (umbrella, mop, ...).
    ///   key_umbrella_too_slow - tool touched the zone but too slowly to
    ///                           count. value = hit speed (m/s). details carry
    ///                           tool=.
    ///   key_stool_zone_enter  - player stepped into the stool stand zone
    ///                           (positioned to climb). value = visit number.
    ///   key_stool_zone_exit   - player left the stand zone. value = seconds
    ///                           spent inside on this visit.
    ///   key_stool_standable   - the stool actually became a climbable surface
    ///                           (player in zone, stool not held).
    ///   key_taken_by_hand     - the key was grabbed directly (stool route).
    ///   key_task_solved       - the task is done. details carry method=
    ///                           umbrella|hand, the tool= used on the umbrella
    ///                           route, plus attempt counters.
    ///   key_first_seen        - the player looked at the key for the first
    ///                           time. value = logger time. details carry
    ///                           distance_m=.
    ///   key_hint_glow_on      - the hint delay elapsed; chair (and umbrella)
    ///                           started glowing.
    ///   key_hint_chair_nudge  - the chair wiggled as a stronger hint.
    ///                           value = wiggle count.
    ///   key_hints_stopped     - hints ended. details carry reason=
    ///                           (grabbed:&lt;object&gt; or solved).
    ///   key_reach_attempt     - a hand entered the reach zone around the key.
    ///                           value = attempt count. details carry hand=
    ///                           and in_stool_zone=0|1.
    ///
    /// The same data is accumulated into a <see cref="KeyTaskSummary"/> which
    /// ExperimentDataManager embeds in the session summary JSON. Read it via
    /// <see cref="BuildSummary"/>.
    /// </summary>
    public class KeyTaskBridge : MonoBehaviour
    {
        [Header("References (drag in Inspector)")]
        [Tooltip("The trigger zone around the key on the shelf.")]
        [SerializeField] private KeyKnockZone keyZone;
        [Tooltip("The stand zone that makes the stool climbable next to the shelf.")]
        [SerializeField] private StoolStandZone stoolZone;
        [Tooltip("Gaze relay that glows the chair/umbrella and wiggles the chair. Optional.")]
        [SerializeField] private DelayedGazeGlow hintRelay;
        [Tooltip("Trigger around the key that counts hand reach attempts. Optional.")]
        [SerializeField] private KeyReachZone reachZone;

        [Tooltip("Name used in the 'object' column of the CSV rows.")]
        [SerializeField] private string logName = "KeyTask";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly KeyTaskSummary _summary = new KeyTaskSummary();

        private float _currentZoneEnterAt = -1f;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void OnEnable()
        {
            if (keyZone != null)
            {
                keyZone.KeyKnockedByUmbrella += HandleUmbrellaKnock;
                keyZone.UmbrellaHitTooSlow += HandleUmbrellaTooSlow;
                keyZone.KeyTakenByHand += HandleTakenByHand;
            }
            else
            {
                Debug.LogWarning($"[{nameof(KeyTaskBridge)}] No KeyKnockZone assigned.", this);
            }

            if (stoolZone != null)
            {
                stoolZone.PlayerEnteredZone += HandleStoolZoneEnter;
                stoolZone.PlayerExitedZone += HandleStoolZoneExit;
                stoolZone.StandableChanged += HandleStandableChanged;
            }
            else
            {
                Debug.LogWarning($"[{nameof(KeyTaskBridge)}] No StoolStandZone assigned.", this);
            }

            if (hintRelay != null)
            {
                hintRelay.TriggerGazeRegistered += HandleKeyFirstSeen;
                hintRelay.HintGlowsEnabled += HandleHintGlowsEnabled;
                hintRelay.ChairNudged += HandleChairNudged;
                hintRelay.HintsStopped += HandleHintsStopped;
            }

            if (reachZone != null)
            {
                reachZone.ReachAttempt += HandleReachAttempt;
            }
        }

        private void OnDisable()
        {
            // The key zone destroys itself once solved, so null-check again.
            if (keyZone != null)
            {
                keyZone.KeyKnockedByUmbrella -= HandleUmbrellaKnock;
                keyZone.UmbrellaHitTooSlow -= HandleUmbrellaTooSlow;
                keyZone.KeyTakenByHand -= HandleTakenByHand;
            }

            if (stoolZone != null)
            {
                stoolZone.PlayerEnteredZone -= HandleStoolZoneEnter;
                stoolZone.PlayerExitedZone -= HandleStoolZoneExit;
                stoolZone.StandableChanged -= HandleStandableChanged;
            }

            if (hintRelay != null)
            {
                hintRelay.TriggerGazeRegistered -= HandleKeyFirstSeen;
                hintRelay.HintGlowsEnabled -= HandleHintGlowsEnabled;
                hintRelay.ChairNudged -= HandleChairNudged;
                hintRelay.HintsStopped -= HandleHintsStopped;
            }

            if (reachZone != null)
            {
                reachZone.ReachAttempt -= HandleReachAttempt;
            }
        }

        // ------------------------------------------------------------------ //
        // Umbrella route (any striking tool: umbrella, mop, ...)
        // ------------------------------------------------------------------ //

        private void HandleUmbrellaKnock(float speed, string tool)
        {
            _summary.usedUmbrella = true;
            _summary.triedUmbrella = true;
            _summary.umbrellaKnockSpeed = speed;
            _summary.knockTool = tool;
            RegisterTool(tool);

            RegisterFirstAttempt(tool); // Logs first=mop instead of first=umbrella|tool=mop

            Manager?.Log("task", $"key_{tool}_knock", logName, speed,
                $"hit_speed_mps={speed.ToString("F2", Inv)}");

            RegisterSolved(tool); // Logs method=mop instead of method=umbrella|tool=mop
        }

        private void HandleUmbrellaTooSlow(float speed, string tool)
        {
            _summary.umbrellaSlowHitCount++;
            _summary.triedUmbrella = true;
            RegisterTool(tool);

            RegisterFirstAttempt(tool);

            Manager?.Log("task", $"key_{tool}_too_slow", logName, speed,
                $"hit_speed_mps={speed.ToString("F2", Inv)}" +
                $"|slow_hits={_summary.umbrellaSlowHitCount}");
        }

        // ------------------------------------------------------------------ //
        // Stool route
        // ------------------------------------------------------------------ //

        private void HandleStoolZoneEnter()
        {
            _summary.triedStool = true;
            _summary.stoolZoneEnterCount++;
            _currentZoneEnterAt = LoggerNow();

            if (_summary.firstStoolZoneEnterAt < 0f)
                _summary.firstStoolZoneEnterAt = _currentZoneEnterAt;

            RegisterFirstAttempt("stool");

            Manager?.Log("task", "key_stool_zone_enter", logName, _summary.stoolZoneEnterCount,
                $"visit={_summary.stoolZoneEnterCount}");
        }

        private void HandleStoolZoneExit()
        {
            float visitSeconds = -1f;
            if (_currentZoneEnterAt >= 0f)
            {
                visitSeconds = LoggerNow() - _currentZoneEnterAt;
                if (visitSeconds >= 0f) _summary.totalStoolZoneSeconds += visitSeconds;
                _currentZoneEnterAt = -1f;
            }

            Manager?.Log("task", "key_stool_zone_exit", logName, visitSeconds,
                visitSeconds >= 0f ? $"visit_s={visitSeconds.ToString("F2", Inv)}" : null);
        }

        private void HandleStandableChanged(bool standable)
        {
            if (!standable) return;

            _summary.stoolStandableCount++;

            Manager?.Log("task", "key_stool_standable", logName, _summary.stoolStandableCount,
                "stool_is_climbable=1");
        }

        private void HandleTakenByHand()
        {
            Manager?.Log("task", "key_taken_by_hand", logName, null,
                _summary.triedStool ? "after_stool_zone=1" : "after_stool_zone=0");

            RegisterSolved("hand");
        }

        // ------------------------------------------------------------------ //
        // Hints and reach attempts
        // ------------------------------------------------------------------ //

        private void HandleKeyFirstSeen(float distance)
        {
            if (_summary.firstKeySeenAt >= 0f) return;

            _summary.firstKeySeenAt = LoggerNow();

            Manager?.Log("task", "key_first_seen", logName, _summary.firstKeySeenAt,
                $"distance_m={distance.ToString("F2", Inv)}");
        }

        private void HandleHintGlowsEnabled()
        {
            _summary.hintGlowAt = LoggerNow();

            Manager?.Log("task", "key_hint_glow_on", logName, _summary.hintGlowAt, null);
        }

        private void HandleChairNudged(int count)
        {
            _summary.chairNudgeCount = count;

            Manager?.Log("task", "key_hint_chair_nudge", logName, count, $"nudge={count}");
        }

        private void HandleHintsStopped(string reason)
        {
            Manager?.Log("task", "key_hints_stopped", logName, null,
                $"reason={reason}" +
                $"|glows_shown={(_summary.hintGlowAt >= 0f ? 1 : 0)}" +
                $"|nudges={_summary.chairNudgeCount}");
        }

        private void HandleReachAttempt(string hand, int count)
        {
            _summary.reachAttemptCount = count;
            if (_summary.firstReachAttemptAt < 0f)
                _summary.firstReachAttemptAt = LoggerNow();

            RegisterFirstAttempt("reach");

            Manager?.Log("task", "key_reach_attempt", logName, count,
                $"hand={hand}" +
                $"|attempt={count}" +
                $"|in_stool_zone={(_currentZoneEnterAt >= 0f ? 1 : 0)}");
        }

        // ------------------------------------------------------------------ //
        // Internals
        // ------------------------------------------------------------------ //

        /// <summary>Remembers every distinct tool that touched the zone, in order.</summary>
        private void RegisterTool(string tool)
        {
            if (string.IsNullOrEmpty(tool)) return;

            if (string.IsNullOrEmpty(_summary.firstTool))
                _summary.firstTool = tool;

            if (!_summary.toolsTried.Contains(tool))
                _summary.toolsTried.Add(tool);
        }

        private void RegisterFirstAttempt(string attempt, string tool = null)
        {
            if (!string.IsNullOrEmpty(_summary.firstAttempt)) return;

            _summary.firstAttempt = attempt;
            _summary.firstAttemptAt = LoggerNow();

            Manager?.Log("task", "key_task_first_attempt", logName, _summary.firstAttemptAt,
                $"first={attempt}" +
                (string.IsNullOrEmpty(tool) ? "" : $"|tool={tool}"));
        }

        private void RegisterSolved(string method, string tool = null)
        {
            if (_summary.solved) return;

            _summary.solved = true;
            _summary.solvedMethod = method;
            _summary.solvedAt = LoggerNow();

            Manager?.Log("task", "key_task_solved", logName, _summary.solvedAt,
                $"method={method}" +
                (string.IsNullOrEmpty(tool) ? "" : $"|tool={tool}")
                +
                $"|first_attempt={_summary.firstAttempt}" +
                $"|umbrella_slow_hits={_summary.umbrellaSlowHitCount}" +
                $"|stool_zone_visits={_summary.stoolZoneEnterCount}" +
                $"|reach_attempts={_summary.reachAttemptCount}");

            if (hintRelay != null) hintRelay.StopHints("solved");
            if (reachZone != null) reachZone.enabled = false;
        }

        private static float LoggerNow()
        {
            return Manager != null ? Manager.LoggerElapsed : -1f;
        }

        // ------------------------------------------------------------------ //
        // JSON summary
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Snapshot of the key task for the session summary JSON.
        /// All timestamps use the t_logger_s clock (same as the CSV); -1 = never.
        /// </summary>
        public KeyTaskSummary BuildSummary()
        {
            // If the session ends while the player is still standing in the
            // stool zone, close the open visit so the total is honest.
            if (_currentZoneEnterAt >= 0f)
            {
                float open = LoggerNow() - _currentZoneEnterAt;
                if (open > 0f) _summary.totalStoolZoneSeconds += open;
                _currentZoneEnterAt = -1f;
            }

            return _summary;
        }

        [Serializable]
        public class KeyTaskSummary
        {
            // Outcome.
            public bool solved;
            public string solvedMethod = "none"; // "umbrella", "hand" or "none"
            public float solvedAt = -1f;

            // Which route was tried first: tool name, "stool", "reach" or "".
            public string firstAttempt = "";
            public float firstAttemptAt = -1f;

            // Umbrella route (any striking tool: umbrella, mop, ...).
            public bool usedUmbrella;            // knocked the key down with it
            public bool triedUmbrella;           // at least touched the zone with it
            public float umbrellaKnockSpeed = -1f;
            public int umbrellaSlowHitCount;     // hits too slow to count
            public string knockTool = "";        // tool that knocked the key down
            public string firstTool = "";        // first tool that touched the zone
            public List<string> toolsTried = new List<string>(); // distinct tools, in order

            // Stool route.
            public bool triedStool;              // entered the stand zone at least once
            public int stoolZoneEnterCount;
            public float firstStoolZoneEnterAt = -1f;
            public float totalStoolZoneSeconds;
            public int stoolStandableCount;      // times the stool became climbable

            // Hints.
            public float firstKeySeenAt = -1f;   // first registered gaze on the key
            public float hintGlowAt = -1f;       // chair/umbrella glow turned on
            public int chairNudgeCount;          // times the chair wiggled

            // Hand reach attempts at the key.
            public int reachAttemptCount;
            public float firstReachAttemptAt = -1f;
        }
    }
}
