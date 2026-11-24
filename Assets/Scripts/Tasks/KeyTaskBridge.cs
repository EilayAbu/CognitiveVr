using System;
using System.Collections;
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
    ///   (stool zone visits are NOT logged per visit - they are only counted:
    ///    stool_visits in key_task_solved, stoolZoneEnterCount /
    ///    totalStoolZoneSeconds / stoolStandableCount in the JSON. Visits
    ///    before the session starts are ignored.)
    ///   key_taken_by_hand     - the key was grabbed directly (stool route).
    ///   key_task_solved       - the task is done. value = t_logger_s. See
    ///                           RegisterSolved for the field list.
    ///   key_first_seen        - the player looked at the key for the first
    ///                           time (after Start). This starts the hint
    ///                           countdown unless a failed reach already did.
    ///                           value = logger time. details carry distance_m=
    ///                           and hint_delay_s= (seconds until the first hint).
    ///   key_hint_countdown_started - a failed reach started the hint countdown
    ///                           before any gaze did: every tracked hand left the
    ///                           KeyReachZone while the key was still on the shelf.
    ///                           Logged once at most; not logged when gaze started it.
    ///                           value = logger time. details: source=reach|
    ///                           seconds_inside=|hint_delay_s=.
    ///   key_hint_started      - the hint delay elapsed; chair (and umbrella)
    ///                           started glowing (first hint).
    ///                           details: hint=glow|since_first_seen_s=.
    ///   key_hint_chair_nudge  - the chair wiggled as a stronger hint.
    ///                           value = wiggle count. details: nudge=|since_hint_start_s=.
    ///   key_hint_chair_nudge_skipped - a scheduled wiggle did not happen
    ///                           (chair held / moving), so every hint attempt is logged.
    ///   key_hints_stopped     - hints ended. details carry reason=
    ///                           (grabbed:&lt;object&gt; or solved).
    ///   key_reach_attempt     - something (hand, umbrella, ...) entered the reach
    ///                           zone around the key. value = attempt count.
    ///                           details: obj=|attempt=|in_stool_zone=0|1.
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

        [Tooltip("After every hand leaves the reach zone, wait this long and check again that the key is still on the shelf before starting the hint countdown (the hand may be carrying the key out).")]
        [SerializeField] private float reachFailGraceSeconds = 0.5f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly KeyTaskSummary _summary = new KeyTaskSummary();

        private float _currentZoneEnterAt = -1f;
        private Coroutine _reachFailRoutine;

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
                hintRelay.ChairNudgeSkipped += HandleChairNudgeSkipped;

                // No hints during the instructions. No manager = no gate, so a
                // scene without one still gets its hints.
                hintRelay.CanStartCountdown = () => Manager == null || Manager.HasSessionStarted;
            }

            if (reachZone != null)
            {
                reachZone.ReachAttempt += HandleReachAttempt;
                reachZone.HandsLeftZone += HandleHandsLeftReachZone;
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
                hintRelay.ChairNudgeSkipped -= HandleChairNudgeSkipped;
                hintRelay.CanStartCountdown = null;
            }

            if (reachZone != null)
            {
                reachZone.ReachAttempt -= HandleReachAttempt;
                reachZone.HandsLeftZone -= HandleHandsLeftReachZone;
            }

            // Coroutines stop with the component; forget the handle too.
            _reachFailRoutine = null;
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

        // True once Start was pressed. The player can stand by the stool during the
        // instructions; those visits must not count as an attempt.
        private static bool SessionStarted => Manager != null && Manager.HasSessionStarted;

        private void HandleStoolZoneEnter()
        {
            if (!SessionStarted) return;

            _summary.triedStool = true;
            _summary.stoolZoneEnterCount++;
            _currentZoneEnterAt = LoggerNow();

            if (_summary.firstStoolZoneEnterAt < 0f)
                _summary.firstStoolZoneEnterAt = _currentZoneEnterAt;

            RegisterFirstAttempt("stool");
        }

        private void HandleStoolZoneExit()
        {
            if (_currentZoneEnterAt < 0f) return;

            float visitSeconds = LoggerNow() - _currentZoneEnterAt;
            if (visitSeconds >= 0f) _summary.totalStoolZoneSeconds += visitSeconds;
            _currentZoneEnterAt = -1f;
        }

        private void HandleStandableChanged(bool standable)
        {
            if (!standable || !SessionStarted) return;

            _summary.stoolStandableCount++;
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

            // The relay starts the countdown right after this event unless a
            // failed reach already did.
            if (hintRelay != null && !hintRelay.IsCountdownStarted)
            {
                _summary.hintCountdownStartedAt = _summary.firstKeySeenAt;
                _summary.hintCountdownSource = "gaze";
            }

            Manager?.Log("task", "key_first_seen", logName, _summary.firstKeySeenAt,
                $"distance_m={distance.ToString("F2", Inv)}" +
                (hintRelay != null ? $"|hint_delay_s={hintRelay.HintDelaySeconds.ToString("F1", Inv)}" : ""));
        }

        private void HandleHintGlowsEnabled()
        {
            _summary.hintGlowAt = LoggerNow();

            Manager?.Log("task", "key_hint_started", logName, _summary.hintGlowAt,
                "hint=glow" + SinceSuffix("since_first_seen_s", _summary.firstKeySeenAt));
        }

        private void HandleChairNudged(int count)
        {
            _summary.chairNudgeCount = count;

            Manager?.Log("task", "key_hint_chair_nudge", logName, count,
                $"nudge={count}" + SinceSuffix("since_hint_start_s", _summary.hintGlowAt));
        }

        private void HandleChairNudgeSkipped(int countSoFar)
        {
            _summary.chairNudgeSkippedCount++;

            Manager?.Log("task", "key_hint_chair_nudge_skipped", logName, countSoFar,
                $"nudges_so_far={countSoFar}|skipped={_summary.chairNudgeSkippedCount}" +
                SinceSuffix("since_hint_start_s", _summary.hintGlowAt));
        }

        /// <summary>"|name=seconds since t" on the logger clock, or "" if t was never set.</summary>
        private static string SinceSuffix(string name, float t)
        {
            if (t < 0f) return "";
            return $"|{name}={(LoggerNow() - t).ToString("F2", Inv)}";
        }

        private void HandleHintsStopped(string reason)
        {
            Manager?.Log("task", "key_hints_stopped", logName, null,
                $"reason={reason}" +
                $"|glows_shown={(_summary.hintGlowAt >= 0f ? 1 : 0)}" +
                $"|nudges={_summary.chairNudgeCount}");
        }

        // Every tracked hand left the reach zone. If the key is still on the
        // shelf, that was a failed reach and starts the hint countdown (no-op
        // if gaze already started it). Tools leaving the zone don't count.
        private void HandleHandsLeftReachZone(float secondsInside)
        {
            if (!CanTreatAsFailedReach()) return;
            if (_reachFailRoutine != null) return; // one check at a time

            _reachFailRoutine = StartCoroutine(ConfirmFailedReach(secondsInside));
        }

        private IEnumerator ConfirmFailedReach(float secondsInside)
        {
            // The hand may be carrying the key out: give the knock zone time
            // to report KeyTakenByHand before deciding.
            if (reachFailGraceSeconds > 0f)
                yield return new WaitForSeconds(reachFailGraceSeconds);

            _reachFailRoutine = null;

            if (!CanTreatAsFailedReach()) yield break;
            if (!hintRelay.StartCountdown("reach")) yield break;

            _summary.hintCountdownStartedAt = LoggerNow();
            _summary.hintCountdownSource = "reach";

            Manager?.Log("task", "key_hint_countdown_started", logName, _summary.hintCountdownStartedAt,
                $"source=reach" +
                $"|seconds_inside={secondsInside.ToString("F2", Inv)}" +
                $"|hint_delay_s={hintRelay.HintDelaySeconds.ToString("F1", Inv)}");
        }

        /// <summary>
        /// True while a failed reach may still start the hints: the session has
        /// started, the key is still on the shelf and the countdown hasn't begun.
        /// </summary>
        private bool CanTreatAsFailedReach()
        {
            if (hintRelay == null || hintRelay.IsCountdownStarted) return false;
            if (!SessionStarted || _summary.solved) return false;
            // The knock zone destroys itself once the key is knocked or taken.
            if (keyZone == null || keyZone.IsConsumed) return false;
            return true;
        }

        private void HandleReachAttempt(string objectName, int zoneCount)
        {
            // Like the stool route: reaching during the instructions is not an attempt.
            // Count here, not with the zone's counter, so attempt=1 is the first one after Start.
            if (!SessionStarted) return;

            int count = ++_summary.reachAttemptCount;
            if (_summary.firstReachAttemptAt < 0f)
                _summary.firstReachAttemptAt = LoggerNow();

            RegisterFirstAttempt("reach");

            Manager?.Log("task", "key_reach_attempt", logName, count,
                $"obj={objectName}" +
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

        private void RegisterSolved(string method)
        {
            if (_summary.solved) return;

            _summary.solved = true;
            _summary.solvedMethod = method;
            _summary.solvedAt = LoggerNow();

            // key_task_solved - one row, value = t_logger_s of the solve.
            //   method             how it was solved: hand (stool route) | umbrella | mop | ...
            //   solve_s            t_session_s of the solve (seconds since the session started)
            //   since_first_seen_s seconds from the first look at the key to the solve
            //   first_attempt      first thing tried: stool | reach | umbrella | mop | ...
            //   reach_attempts     times something entered the reach zone around the key
            //   stool_visits       times the player stepped into the stool stand zone
            //   slow_hits          tool hits too slow to knock the key down
            float sessionNow = Manager != null ? Manager.SessionElapsed : -1f;
            Manager?.Log("task", "key_task_solved", logName, _summary.solvedAt,
                $"method={method}" +
                (sessionNow >= 0f ? $"|solve_s={sessionNow.ToString("F2", Inv)}" : "") +
                SinceSuffix("since_first_seen_s", _summary.firstKeySeenAt) +
                $"|first_attempt={_summary.firstAttempt}" +
                $"|reach_attempts={_summary.reachAttemptCount}" +
                $"|stool_visits={_summary.stoolZoneEnterCount}" +
                $"|slow_hits={_summary.umbrellaSlowHitCount}");

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
            public float hintCountdownStartedAt = -1f; // hint countdown began
            public string hintCountdownSource = "";    // what started it: "gaze", "reach" or ""
            public float hintGlowAt = -1f;       // chair/umbrella glow turned on
            public int chairNudgeCount;          // times the chair wiggled
            public int chairNudgeSkippedCount;   // scheduled wiggles that did not happen

            // Hand reach attempts at the key.
            public int reachAttemptCount;
            public float firstReachAttemptAt = -1f;
        }
    }
}
