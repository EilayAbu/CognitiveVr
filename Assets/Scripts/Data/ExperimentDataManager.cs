using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using CognitiveVR.Core;
using CognitiveVR.Interaction;

namespace CognitiveVR.Data
{
    /// <summary>
    /// Central experiment logger. Writes timestamped CSV(s) of every event
    /// plus a JSON summary at session end. Purely a subscriber: it hooks into
    /// the existing SessionTimer, GazeObjectTracker and BackpackInventoryZone
    /// events without modifying any of them.
    ///
    /// Item interactions arrive either from <see cref="ItemUsageTracker"/>
    /// components (recommended, zero wiring) or from Inspector-wired
    /// UnityEvents calling the public Log* methods (e.g. an
    /// InteractableUnityEventWrapper "When Select" list).
    ///
    /// Gaze dwell and freeze data come from <see cref="GazeObjectTracker"/>,
    /// which writes its rows through this logger and is read here at session
    /// end for the summary totals.
    ///
    /// Every row carries four clocks:
    ///  - real_time    : local system clock (HH:mm:ss.fff)
    ///  - t_logger_s   : seconds since this logger started (scene load)
    ///  - t_session_s  : seconds since SessionTimer.StartSession() (empty before)
    ///  - wall_clock   : the in-scene 08:52-based clock from SessionTimer
    ///
    /// The JSON summary uses ONE clock everywhere: t_logger_s. The
    /// sessionStartLoggerSeconds field is the bridge to t_session_s.
    /// </summary>
    public class ExperimentDataManager : MonoBehaviour
    {
        public static ExperimentDataManager Instance { get; private set; }

        [Header("Participant / Output")]
        [Tooltip("Prefix used in the output file names.")]
        [SerializeField] private string participantId = "P00";
        [Tooltip("Subfolder created under Application.persistentDataPath.")]
        [SerializeField] private string outputSubfolder = "ExperimentLogs";
        [Tooltip("Mirror every event (except pose samples) to the Console.")]
        [SerializeField] private bool logToConsole = true;

        [Header("File Splitting")]
        [Tooltip("ON  = two CSVs: *_events.csv (touching / session) and *_gaze.csv (looking / freezes).\n" +
                 "OFF = everything in one *_events.csv, separated by the 'category' column.")]
        [SerializeField] private bool separateGazeFile = true;

        [Header("Scene References (auto-found if left empty)")]
        [SerializeField] private SessionTimer sessionTimer;
        [SerializeField] private GazeObjectTracker gazeTracker;
        [SerializeField] private BackpackInventoryZone backpack;
        [SerializeField] private CognitiveVR.Tasks.ToasterDataBridge toasterBridge;
        [SerializeField] private CognitiveVR.Tasks.WindowPuddleTaskBridge windowPuddleBridge;
        [SerializeField] private CognitiveVR.Tasks.KeyTaskBridge keyTaskBridge;

        [Header("Continuous Tracking")]
        [Tooltip("Seconds between head pose samples written to the CSV. 0 = disabled.")]
        [SerializeField] private float poseSampleInterval = 1f;
        [Tooltip("headPathMeters accumulates every frame, but only once the head has drifted this far (m) from its last counted position. Filters tracking jitter without losing slow drift. The old behavior - summing the 1 Hz pose samples - cut every corner and underestimated locomotion.")]
        [SerializeField] private float headPathMinStep = 0.01f;

        // ------------------------------------------------------------------ //

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private const string CsvHeader =
            "real_time,t_logger_s,t_session_s,wall_clock,category,event,object,value,details";

        private StreamWriter _writer;
        private StreamWriter _gazeWriter;
        private string _csvPath;
        private string _gazeCsvPath;
        private string _summaryPath;

        private float _loggerStartRealtime;
        private DateTime _startedAt;
        private int _eventCount;
        private bool _sessionStarted;
        private bool _finalized;
        // Latched at the very end of FinalizeSession. Once set, Log() drops
        // rows instead of writing them, so nothing that happens after the
        // participant leaves the exit zone can reach the CSV or the summary.
        private bool _loggingClosed;
        private string _endReason = "";
        private List<string> _finalContents = new List<string>();
        private bool _hasFinalContents;
        private float _finalSessionElapsed;
        private CognitiveVR.Tasks.ToasterDataBridge.ToasterTaskSummary _finalToasterSummary;

        // The guide (neighbor) task uses a push model: GuideDataBridge registers
        // its summary object here while it is active, so the manager never has to
        // find the bridge (which sits on an object that is inactive at scene load).
        private GuideDataBridge.GuideTaskSummary _guideSummary;
        private CognitiveVR.Tasks.WindowPuddleTaskBridge.WindowPuddleTaskSummary _finalWindowPuddleSummary;
        private CognitiveVR.Tasks.KeyTaskBridge.KeyTaskSummary _finalKeyTaskSummary;

        // Interaction bookkeeping (keyed by item name).
        private readonly Dictionary<string, float> _selectStartTimes = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _lastHoverEnter = new Dictionary<string, float>();
        private readonly Dictionary<string, ItemSummary> _itemStats = new Dictionary<string, ItemSummary>();
        private readonly List<string> _packingOrder = new List<string>();

        // Pose sampling / head path.
        private float _nextPoseSampleAt;
        private Vector3 _headPathAnchor;
        private bool _hasHeadPathAnchor;
        private float _headPathMeters;

        // Logger time at which SessionTimer.StartSession() fired. -1 = never.
        // This is the bridge between the two CSV clocks:
        //   t_session_s = t_logger_s - sessionStartLoggerSeconds
        private float _sessionStartLoggerSeconds = -1f;

        /// <summary>Seconds since the logger started (unscaled, pause-proof).</summary>
        public float LoggerElapsed => Time.realtimeSinceStartup - _loggerStartRealtime;

        /// <summary>
        /// Seconds since SessionTimer.StartSession(). -1 when no timer is present.
        /// Uses the wired SessionTimer reference, so callers never need their own.
        /// </summary>
        public float SessionElapsed => sessionTimer != null ? sessionTimer.ElapsedTime : -1f;

        /// <summary>
        /// Wall-clock seconds since the logger started. Unlike LoggerElapsed this
        /// stays correct during editor play-mode teardown, where
        /// Time.realtimeSinceStartup jumps backwards.
        /// </summary>
        public float LoggerDurationSeconds => (float)(DateTime.Now - _startedAt).TotalSeconds;

        // ------------------------------------------------------------------ //
        // Lifecycle
        // ------------------------------------------------------------------ //

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[{nameof(ExperimentDataManager)}] Duplicate instance on '{name}' destroyed.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (sessionTimer == null) sessionTimer = FindFirstObjectByType<SessionTimer>();
            if (gazeTracker == null) gazeTracker = FindFirstObjectByType<GazeObjectTracker>();
            if (backpack == null) backpack = FindFirstObjectByType<BackpackInventoryZone>();
            if (toasterBridge == null) toasterBridge = FindFirstObjectByType<CognitiveVR.Tasks.ToasterDataBridge>();
            // guideBridge is intentionally NOT searched here: it lives on an object
            // that is inactive at scene load. It registers itself via
            // RegisterGuideSummary() once it becomes active (push model).
            if (windowPuddleBridge == null) windowPuddleBridge = FindFirstObjectByType<CognitiveVR.Tasks.WindowPuddleTaskBridge>();
            if (keyTaskBridge == null) keyTaskBridge = FindFirstObjectByType<CognitiveVR.Tasks.KeyTaskBridge>();

            OpenLogFile();
        }

        private void OnEnable()
        {
            if (sessionTimer != null)
            {
                sessionTimer.OnSessionStarted += HandleSessionStarted;
                sessionTimer.OnSessionEnded += HandleSessionEnded;
                sessionTimer.OnScheduledEventTriggered += HandleScheduledEvent;
                sessionTimer.OnTimeWarning += HandleTimeWarning;
            }

            if (backpack != null)
            {
                backpack.WhenItemEntered += HandleBackpackItemEntered;
                backpack.WhenItemExited += HandleBackpackItemExited;
            }

            // Note: gaze and freeze rows are written by GazeObjectTracker itself
            // through the public Log methods below, so there is nothing to
            // subscribe to here. Its totals are read in WriteSummary().
        }

        private void OnDisable()
        {
            if (sessionTimer != null)
            {
                sessionTimer.OnSessionStarted -= HandleSessionStarted;
                sessionTimer.OnSessionEnded -= HandleSessionEnded;
                sessionTimer.OnScheduledEventTriggered -= HandleScheduledEvent;
                sessionTimer.OnTimeWarning -= HandleTimeWarning;
            }

            if (backpack != null)
            {
                backpack.WhenItemEntered -= HandleBackpackItemEntered;
                backpack.WhenItemExited -= HandleBackpackItemExited;
            }
        }

        private void Update()
        {
            if (_loggingClosed)
                return;

            // Every frame, not once per pose sample: summing the 1 Hz samples
            // systematically undercounted (straight-line chords through every
            // turn). Verified against run 160758: the 1 Hz sum reproduced the
            // reported 21.13 m exactly, i.e. all sub-second movement was lost.
            AccumulateHeadPath();

            if (poseSampleInterval > 0f && Time.realtimeSinceStartup >= _nextPoseSampleAt)
            {
                _nextPoseSampleAt = Time.realtimeSinceStartup + poseSampleInterval;
                SamplePose();
            }
        }

        private void AccumulateHeadPath()
        {
            Transform head = ResolveHead();
            if (head == null) return;

            Vector3 pos = head.position;

            if (!_hasHeadPathAnchor)
            {
                _headPathAnchor = pos;
                _hasHeadPathAnchor = true;
                return;
            }

            // Anchor-gated (same pattern as the freeze detector): per-frame
            // deltas on a real headset are dominated by tracking noise, which
            // would inflate the path by meters per minute. Distance only counts
            // once the head is genuinely elsewhere; slow drift still accumulates
            // because the anchor stays put until the threshold is crossed.
            float d = Vector3.Distance(pos, _headPathAnchor);
            if (d >= headPathMinStep)
            {
                _headPathMeters += d;
                _headPathAnchor = pos;
            }
        }

        private Transform ResolveHead()
        {
            if (gazeTracker != null)
                return gazeTracker.HeadTransform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        private void OnApplicationPause(bool paused)
        {
            // Quest apps can be killed while backgrounded — make sure data survives.
            if (paused)
            {
                _writer?.Flush();
                _gazeWriter?.Flush();

                // After finalize the summary is already final; rewriting it here
                // could only ever make it disagree with the CSV.
                if (!_finalized)
                    WriteSummary();
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            // Fallback only. A normal run has already finalized itself at the
            // exit zone; this catches aborted runs (headset removed, editor stop,
            // app quit) so they still produce a complete file. The endReason
            // field in the JSON is what tells the two apart at analysis time.
            if (!_finalized)
                FinalizeSession("quit");

            if (_writer != null)
            {
                // WriteRow, not Log: this is bookkeeping and must survive the gate.
                WriteRow("session", "logger_stop", "", LoggerDurationSeconds, $"end_reason={_endReason}");
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }

            if (_gazeWriter != null)
            {
                _gazeWriter.Flush();
                _gazeWriter.Dispose();
                _gazeWriter = null;
            }

            Instance = null;
        }

        /// <summary>
        /// Flushes per-object gaze totals, writes a session_end row and dumps the
        /// JSON summary. Safe to call more than once - only the first call writes
        /// the session_end row. Call this from an end-of-session trigger, a UI
        /// button, or leave it to OnDestroy.
        /// </summary>
        public void FinalizeSession(string reason)
        {
            // Idempotent. A second caller (OnDestroy, a re-entered exit zone, the
            // SessionTimer expiring after a manual finalize) must NOT re-dump the
            // gaze totals or rewrite the summary - that is what previously
            // produced a second dwell_total pass disagreeing with the JSON.
            if (_finalized)
                return;

            _finalized = true;
            _endReason = reason;

            // 1. Cut off producers that could still push rows at us. Note this
            //    only stops them being LOGGED - a SessionTimer scheduled event
            //    will still fire its own UnityEvents in the scene.
            if (sessionTimer != null)
                sessionTimer.OnScheduledEventTriggered -= HandleScheduledEvent;

            // 2. Close anything still held. Writes real unselect rows through the
            //    normal path, which is still open at this point - deliberately so.
            CloseOpenHolds();

            // 3. Close the in-progress look, emit the one and only dwell_total
            //    pass, then stop the tracker's LateUpdate. After this call
            //    GetResults() is frozen, so the summary below and the CSV rows
            //    are guaranteed to be the same numbers.
            if (gazeTracker != null)
                gazeTracker.StopTracking();

            // 4. Snapshot the backpack NOW. By the time OnDestroy runs, the zone
            //    may already have been torn down and would report empty.
            _finalContents = backpack != null
                ? new List<string>(backpack.StoredItemNames)
                : new List<string>();
            _hasFinalContents = true;

            // Same reason: the SessionTimer may be stopped or reset by the
            // time OnDestroy rewrites the summary, reporting 0 elapsed.
            _finalSessionElapsed = sessionTimer != null ? sessionTimer.ElapsedTime : 0f;

            // Snapshot the toaster report too - the bridge may be destroyed
            // before OnDestroy rewrites the summary.
            if (toasterBridge != null)
                _finalToasterSummary = toasterBridge.BuildSummary();

            // _guideSummary is kept live by the bridge via RegisterGuideSummary;
            // nothing to snapshot here.

            if (windowPuddleBridge != null)
                _finalWindowPuddleSummary = windowPuddleBridge.BuildSummary();

            if (keyTaskBridge != null)
                _finalKeyTaskSummary = keyTaskBridge.BuildSummary();

            // 5. The closing row, then the JSON.
            Log("session", "session_end", "", sessionTimer != null ? sessionTimer.ElapsedTime : (float?)null,
                $"reason={reason}");

            WriteSummary();

            _writer?.Flush();
            _gazeWriter?.Flush();

            // 6. Latch the log shut. Everything above has already been written.
            _loggingClosed = true;

            Debug.Log($"[{nameof(ExperimentDataManager)}] Session finalized ({reason}). " +
                      $"Logging closed at t={LoggerElapsed.ToString("F2", Inv)}s.");
        }

        /// <summary>
        /// Writes a closing unselect row for every item still held when the
        /// session ended, so no hold stays open and no duration is computed
        /// against a stale start stamp.
        /// </summary>
        private void CloseOpenHolds()
        {
            if (_selectStartTimes.Count == 0)
                return;

            float now = LoggerElapsed;
            var stillHeld = new List<string>(_selectStartTimes.Keys);

            foreach (string itemName in stillHeld)
            {
                float startedAt = _selectStartTimes[itemName];
                float held = Mathf.Max(0f, now - startedAt);

                GetStats(itemName).totalHeldSeconds += held;

                Log("interaction", "unselect", itemName, held,
                    $"held_s={held.ToString("F3", Inv)}|closed_by=session_end");
            }

            _selectStartTimes.Clear();
        }

        // ------------------------------------------------------------------ //
        // Public logging API (also targets for Inspector-wired UnityEvents)
        // ------------------------------------------------------------------ //

        /// <summary>An item was grabbed / a button press began.</summary>
        public void LogSelect(string itemName)
        {
            float now = LoggerElapsed;
            _selectStartTimes[itemName] = now;

            ItemSummary stats = GetStats(itemName);
            stats.selectCount++;
            if (stats.firstInteractionAt < 0f) stats.firstInteractionAt = now;

            string details = null;
            if (_lastHoverEnter.TryGetValue(itemName, out float hoverAt))
            {
                details = $"hover_to_select_s={(now - hoverAt).ToString("F3", Inv)}";
            }

            Log("interaction", "select", itemName, null, details);
        }

        /// <summary>The item was released. Hold duration is computed automatically.</summary>
        public void LogUnselect(string itemName)
        {
            float now = LoggerElapsed;
            float? held = null;

            if (_selectStartTimes.TryGetValue(itemName, out float startedAt))
            {
                // Clamped: a negative duration means the start was stamped on a
                // different clock, and a negative must never reach the CSV.
                held = Mathf.Max(0f, now - startedAt);
                _selectStartTimes.Remove(itemName);
                GetStats(itemName).totalHeldSeconds += held.Value;
            }

            Log("interaction", "unselect", itemName, held,
                held.HasValue ? $"held_s={held.Value.ToString("F3", Inv)}" : null);
        }

        /// <summary>A hand started hovering the item.</summary>
        public void LogHoverEnter(string itemName)
        {
            _lastHoverEnter[itemName] = LoggerElapsed;
            Log("interaction", "hover_enter", itemName, null, null);
        }

        /// <summary>The hand stopped hovering the item.</summary>
        public void LogHoverExit(string itemName)
        {
            Log("interaction", "hover_exit", itemName, null, null);
        }

        /// <summary>
        /// An item hit the floor. Counted per item in the JSON summary so you can
        /// see who fumbled what, and how often.
        /// </summary>
        public void LogItemDropped(string itemName, float impactSpeed, string details)
        {
            ItemSummary stats = GetStats(itemName);
            stats.dropCount++;

            Log("interaction", "item_dropped", itemName, impactSpeed, details);
        }

        /// <summary>
        /// Flags an item as important (or not) in the JSON summary. Called once by
        /// ItemUsageTracker.OnEnable with its Inspector-set value; does not write a
        /// CSV row on its own - it just tags whatever ItemSummary the item ends up
        /// with. Safe to call before any interaction is logged for that item.
        /// </summary>
        public void SetItemImportant(string itemName, bool important)
        {
            GetStats(itemName).isImportant = important;
        }

        /// <summary>Convenience for simple UI / poke buttons wired in the Inspector.</summary>
        public void LogButtonPress(string buttonName)
        {
            ItemSummary stats = GetStats(buttonName);
            stats.selectCount++;
            if (stats.firstInteractionAt < 0f) stats.firstInteractionAt = LoggerElapsed;

            Log("interaction", "button_press", buttonName, null, null);
        }

        /// <summary>Called by GazeObjectTracker when a look begins.</summary>
        public void LogGazeEnter(string objectName)
        {
            Log("gaze", "gaze_enter", objectName, null, null);
        }

        /// <summary>Companion to <see cref="LogGazeEnter"/>. Duration in seconds.</summary>
        public void LogGazeExit(string objectName, float gazeDuration)
        {
            Log("gaze", "gaze_exit", objectName, gazeDuration, null);
        }

        /// <summary>Free-form marker row (Inspector-friendly).</summary>
        public void LogCustom(string message)
        {
            Log("custom", "note", "", null, message);
        }

        /// <summary>
        /// Called by <see cref="GuideDataBridge"/> when it becomes active. The
        /// manager holds the reference to the same summary object the bridge keeps
        /// updating, so the neighbor-task section of the JSON is always current at
        /// session end - no need to find the (initially inactive) bridge.
        /// </summary>
        public void RegisterGuideSummary(GuideDataBridge.GuideTaskSummary summary)
        {
            if (summary != null)
                _guideSummary = summary;
        }

        // ------------------------------------------------------------------ //
        // Subscribed handlers
        // ------------------------------------------------------------------ //

        private void HandleSessionStarted()
        {
            _sessionStarted = true;
            if (_sessionStartLoggerSeconds < 0f)
                _sessionStartLoggerSeconds = LoggerElapsed;

            Log("session", "session_start", "", null,
                sessionTimer != null ? $"wall_clock_start={sessionTimer.WallClockFormatted}" : null);
        }

        private void HandleSessionEnded()
        {
            FinalizeSession("session_timer");
        }

        private void HandleScheduledEvent(SessionTimer.ScheduledEvent evt)
        {
            Log("session", "scheduled_event", evt.Id, evt.TriggerTime, $"display={evt.DisplayName}");
        }

        private void HandleTimeWarning(float elapsed)
        {
            Log("session", "time_warning", "", elapsed, null);
        }

        private void HandleBackpackItemEntered(string itemName, BackpackSlot slot)
        {
            ItemSummary stats = GetStats(itemName);
            stats.backpackInCount++;

            if (!_packingOrder.Contains(itemName))
            {
                _packingOrder.Add(itemName);
            }

            Log("backpack", "item_in", itemName, null, slot != null ? $"slot={slot.name}" : null);
        }

        private void HandleBackpackItemExited(string itemName, BackpackSlot slot)
        {
            GetStats(itemName).backpackOutCount++;
            Log("backpack", "item_out", itemName, null, slot != null ? $"slot={slot.name}" : null);
        }

        // ------------------------------------------------------------------ //
        // Core writing
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Writes one CSV row. All public Log* methods and handlers funnel here.
        /// Gaze and freeze categories go to the gaze file when splitting is on.
        /// </summary>
        public void Log(string category, string eventName, string objectName, float? value, string details)
        {
            if (_loggingClosed)
            {
                // Deliberately noisy: if something is still firing after the exit
                // zone you want to see it in the Console, not silently lose it.
                Debug.LogWarning($"[{nameof(ExperimentDataManager)}] Dropped post-session row: " +
                                 $"{category}/{eventName} {objectName} {details}");
                return;
            }

            WriteRow(category, eventName, objectName, value, details);
        }

        /// <summary>
        /// Unconditional row write. Bypasses the post-session gate, so only
        /// logger bookkeeping (logger_stop) should use it.
        /// </summary>
        private void WriteRow(string category, string eventName, string objectName, float? value, string details)
        {
            StreamWriter target = SelectWriter(category);
            if (target == null) return;

            _eventCount++;

            string realTime = DateTime.Now.ToString("HH:mm:ss.fff", Inv);
            // During editor play-mode teardown Time.realtimeSinceStartup jumps
            // backwards, so fall back to the wall clock for those last rows.
            float tLoggerValue = LoggerElapsed;
            if (tLoggerValue < 0f) tLoggerValue = LoggerDurationSeconds;
            string tLogger = tLoggerValue.ToString("F3", Inv);
            string tSession = (_sessionStarted && sessionTimer != null)
                ? sessionTimer.ElapsedTime.ToString("F3", Inv)
                : "";
            string wallClock = sessionTimer != null ? sessionTimer.WallClockFormatted : "";
            string valueStr = value.HasValue ? value.Value.ToString("F3", Inv) : "";

            target.WriteLine(string.Join(",",
                Esc(realTime), Esc(tLogger), Esc(tSession), Esc(wallClock),
                Esc(category), Esc(eventName), Esc(objectName), Esc(valueStr), Esc(details ?? "")));

            if (logToConsole && category != "tracking" && category != "gaze")
            {
                Debug.Log($"[Data {tLogger}s] {category}/{eventName} {objectName} {details}");
            }
        }

        /// <summary>Routes a category to the right file.</summary>
        private StreamWriter SelectWriter(string category)
        {
            if (separateGazeFile && _gazeWriter != null &&
                (category == "gaze" || category == "freeze"))
            {
                return _gazeWriter;
            }
            return _writer;
        }

        private void OpenLogFile()
        {
            string dir = Path.Combine(Application.persistentDataPath, outputSubfolder);
            Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
            _csvPath = Path.Combine(dir, $"{participantId}_{stamp}_events.csv");
            _gazeCsvPath = Path.Combine(dir, $"{participantId}_{stamp}_gaze.csv");
            _summaryPath = Path.Combine(dir, $"{participantId}_{stamp}_summary.json");

            // UTF-8 with BOM so Hebrew display names open correctly in Excel.
            _writer = new StreamWriter(_csvPath, false, new UTF8Encoding(true)) { AutoFlush = true };
            _writer.WriteLine(CsvHeader);

            if (separateGazeFile)
            {
                _gazeWriter = new StreamWriter(_gazeCsvPath, false, new UTF8Encoding(true)) { AutoFlush = true };
                _gazeWriter.WriteLine(CsvHeader);
            }

            _loggerStartRealtime = Time.realtimeSinceStartup;
            _startedAt = DateTime.Now;
            _nextPoseSampleAt = Time.realtimeSinceStartup + Mathf.Max(poseSampleInterval, 0.01f);

            Log("session", "logger_start", "", null,
                $"participant={participantId}|date={_startedAt.ToString("yyyy-MM-dd", Inv)}");

            Debug.Log($"[{nameof(ExperimentDataManager)}] Logging to: {_csvPath}");
            if (separateGazeFile)
                Debug.Log($"[{nameof(ExperimentDataManager)}] Gaze log: {_gazeCsvPath}");
        }

        private void SamplePose()
        {
            Transform head = ResolveHead();
            if (head == null) return;

            var sb = new StringBuilder(64);
            sb.Append("pos=").Append(V(head.position)).Append("|rot=").Append(V(head.eulerAngles));

            if (gazeTracker != null)
            {
                // RawGazeTarget, not CurrentlyLookingAt: the tracker now ignores
                // floor/wall/ceiling for dwell stats, but the continuous stream
                // should keep showing what the head is physically pointed at.
                sb.Append("|looking_at=").Append(gazeTracker.RawGazeTarget);
                if (gazeTracker.IsFrozen) sb.Append("|frozen=1");
            }

            Log("tracking", "pose", "head", null, sb.ToString());
        }

        private void WriteSummary()
        {
            if (string.IsNullOrEmpty(_summaryPath)) return;

            MergeGazeStats();

            var summary = new SessionSummary
            {
                participantId = participantId,
                startedAtIso = _startedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                writtenAtIso = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                endReason = string.IsNullOrEmpty(_endReason) ? "in_progress" : _endReason,
                loggerDurationSeconds = LoggerDurationSeconds,
                sessionStartLoggerSeconds = _sessionStartLoggerSeconds,
                sessionElapsedSeconds = _hasFinalContents
                    ? _finalSessionElapsed
                    : (sessionTimer != null ? sessionTimer.ElapsedTime : 0f),
                totalEvents = _eventCount,
                headPathMeters = _headPathMeters,
                freezeCount = gazeTracker != null ? gazeTracker.FreezeCount : 0,
                totalFreezeSeconds = gazeTracker != null ? gazeTracker.TotalFreezeTime : 0f,
                objectsLookedAt = 0,
                packingOrder = new List<string>(_packingOrder),
                finalBackpackContents = _hasFinalContents
                    ? new List<string>(_finalContents)
                    : (backpack != null ? new List<string>(backpack.StoredItemNames) : new List<string>()),
                toasterTask = _finalToasterSummary
                    ?? (toasterBridge != null ? toasterBridge.BuildSummary() : new CognitiveVR.Tasks.ToasterDataBridge.ToasterTaskSummary()),
                guideTask = _guideSummary ?? new GuideDataBridge.GuideTaskSummary(),
                windowPuddleTask = _finalWindowPuddleSummary
                    ?? (windowPuddleBridge != null ? windowPuddleBridge.BuildSummary() : new CognitiveVR.Tasks.WindowPuddleTaskBridge.WindowPuddleTaskSummary()),
                keyTask = _finalKeyTaskSummary
                    ?? (keyTaskBridge != null ? keyTaskBridge.BuildSummary() : new CognitiveVR.Tasks.KeyTaskBridge.KeyTaskSummary())
            };

            foreach (KeyValuePair<string, ItemSummary> pair in _itemStats)
            {
                pair.Value.inBackpackAtEnd = summary.finalBackpackContents.Contains(pair.Key);
                if (pair.Value.lookCount > 0) summary.objectsLookedAt++;
                summary.items.Add(pair.Value);
            }

            // Most-looked-at first: usually the interesting ordering.
            summary.items.Sort((a, b) => b.totalGazeSeconds.CompareTo(a.totalGazeSeconds));

            try
            {
                File.WriteAllText(_summaryPath, JsonUtility.ToJson(summary, true), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(ExperimentDataManager)}] Failed to write summary: {e.Message}", this);
            }
        }

        /// <summary>
        /// Folds the tracker's per-object dwell totals into the item table so
        /// objects that were only looked at (never grabbed) still appear.
        /// </summary>
        private void MergeGazeStats()
        {
            if (gazeTracker == null) return;

            foreach (GazeObjectTracker.GazeObjectStats g in gazeTracker.GetResults())
            {
                ItemSummary stats = GetStats(g.ObjectName);
                stats.totalGazeSeconds = g.TotalGazeTime;
                stats.lookCount = g.LookCount;
                stats.longestStareSeconds = g.LongestStare;
                stats.firstLookAt = g.FirstLookTime;
                stats.glanceCount = g.GlanceCount;
                stats.glanceSeconds = g.GlanceSeconds;
                stats.gazeFreezeCount = g.FreezeCount;
                stats.gazeFreezeTotalSeconds = g.FreezeSeconds;
            }
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private ItemSummary GetStats(string itemName)
        {
            if (!_itemStats.TryGetValue(itemName, out ItemSummary stats))
            {
                stats = new ItemSummary { name = itemName };
                _itemStats[itemName] = stats;
            }
            return stats;
        }

        /// <summary>Vector formatted with semicolons so it never fights CSV commas.</summary>
        private static string V(Vector3 v)
        {
            return $"({v.x.ToString("F2", Inv)};{v.y.ToString("F2", Inv)};{v.z.ToString("F2", Inv)})";
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        // ------------------------------------------------------------------ //
        // Summary data shapes (JsonUtility-friendly)
        // ------------------------------------------------------------------ //

        [Serializable]
        public class ItemSummary
        {
            public string name;

            // Touching.
            public int selectCount;
            public float totalHeldSeconds;
            public float firstInteractionAt = -1f;
            public int backpackInCount;
            public int backpackOutCount;
            public bool inBackpackAtEnd;
            public int dropCount;
            /// <summary>Inspector flag set on the item's ItemUsageTracker. Default false.</summary>
            public bool isImportant;

            // Looking. Times are t_logger_s. lookCount counts registered
            // fixations (>= Min Look Duration); glanceCount counts shorter hits.
            public float totalGazeSeconds;
            public int lookCount;
            public float longestStareSeconds;
            public float firstLookAt = -1f;
            public int glanceCount;
            public float glanceSeconds;
            public int gazeFreezeCount;
            public float gazeFreezeTotalSeconds;
        }

        [Serializable]
        public class SessionSummary
        {
            public string participantId;
            public string startedAtIso;
            public string writtenAtIso;
            /// <summary>"exit_with_backpack", "manual", "session_timer" = complete run. "quit" = aborted.</summary>
            public string endReason;
            /// <summary>Which window the gaze/hold numbers cover. Removes any ambiguity at analysis time.</summary>
            public string gazeWindow = "logger_start..session_end";
            /// <summary>Every *At / *Seconds timestamp in this file is on this clock unless its name says otherwise.</summary>
            public string timebase = "t_logger_s (seconds since logger_start)";
            public float loggerDurationSeconds;
            /// <summary>t_logger_s at SessionTimer.StartSession(). -1 = session never started. t_session_s = t_logger_s - this.</summary>
            public float sessionStartLoggerSeconds = -1f;
            public float sessionElapsedSeconds;
            public int totalEvents;
            public float headPathMeters;
            public int freezeCount;
            public float totalFreezeSeconds;
            public int objectsLookedAt;
            public List<string> packingOrder = new List<string>();
            public List<string> finalBackpackContents = new List<string>();
            public CognitiveVR.Tasks.ToasterDataBridge.ToasterTaskSummary toasterTask =
                new CognitiveVR.Tasks.ToasterDataBridge.ToasterTaskSummary();
            public GuideDataBridge.GuideTaskSummary guideTask =
                new GuideDataBridge.GuideTaskSummary();
            public CognitiveVR.Tasks.WindowPuddleTaskBridge.WindowPuddleTaskSummary windowPuddleTask =
                new CognitiveVR.Tasks.WindowPuddleTaskBridge.WindowPuddleTaskSummary();
            public CognitiveVR.Tasks.KeyTaskBridge.KeyTaskSummary keyTask =
                new CognitiveVR.Tasks.KeyTaskBridge.KeyTaskSummary();
            public List<ItemSummary> items = new List<ItemSummary>();
        }
    }
}