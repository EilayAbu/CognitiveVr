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

        [Header("Continuous Tracking")]
        [Tooltip("Seconds between head pose samples written to the CSV. 0 = disabled.")]
        [SerializeField] private float poseSampleInterval = 1f;

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
        private List<string> _finalContents = new List<string>();
        private bool _hasFinalContents;
        private float _finalSessionElapsed;

        // Interaction bookkeeping (keyed by item name).
        private readonly Dictionary<string, float> _selectStartTimes = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _lastHoverEnter = new Dictionary<string, float>();
        private readonly Dictionary<string, ItemSummary> _itemStats = new Dictionary<string, ItemSummary>();
        private readonly List<string> _packingOrder = new List<string>();

        // Pose sampling.
        private float _nextPoseSampleAt;
        private Vector3 _lastHeadPos;
        private bool _hasLastHeadPos;
        private float _headPathMeters;

        /// <summary>Seconds since the logger started (unscaled, pause-proof).</summary>
        public float LoggerElapsed => Time.realtimeSinceStartup - _loggerStartRealtime;

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
            if (poseSampleInterval > 0f && Time.realtimeSinceStartup >= _nextPoseSampleAt)
            {
                _nextPoseSampleAt = Time.realtimeSinceStartup + poseSampleInterval;
                SamplePose();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            // Quest apps can be killed while backgrounded — make sure data survives.
            if (paused)
            {
                _writer?.Flush();
                _gazeWriter?.Flush();
                WriteSummary();
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            // Order matters: the tracker's own OnDisable may run after this, by
            // which point the writers are gone. Flush its totals first.
            FinalizeSession("logger_destroyed");

            if (_writer != null)
            {
                Log("session", "logger_stop", "", LoggerDurationSeconds, null);
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
            if (gazeTracker != null)
                gazeTracker.DumpTotalsToLog();

            if (!_finalized)
            {
                _finalized = true;

                // Snapshot the backpack NOW. By the time OnDestroy runs, the zone
                // may already have been torn down and would report empty.
                _finalContents = backpack != null
                    ? new List<string>(backpack.StoredItemNames)
                    : new List<string>();
                _hasFinalContents = true;

                // Same reason: the SessionTimer may be stopped or reset by the
                // time OnDestroy rewrites the summary, reporting 0 elapsed.
                _finalSessionElapsed = sessionTimer != null ? sessionTimer.ElapsedTime : 0f;

                Log("session", "session_end", "", sessionTimer != null ? sessionTimer.ElapsedTime : (float?)null,
                    $"reason={reason}");
            }

            WriteSummary();

            _writer?.Flush();
            _gazeWriter?.Flush();
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
                held = now - startedAt;
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

        // ------------------------------------------------------------------ //
        // Subscribed handlers
        // ------------------------------------------------------------------ //

        private void HandleSessionStarted()
        {
            _sessionStarted = true;
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
            Transform head = gazeTracker != null
                ? gazeTracker.HeadTransform
                : (Camera.main != null ? Camera.main.transform : null);

            if (head == null) return;

            if (_hasLastHeadPos)
            {
                _headPathMeters += Vector3.Distance(head.position, _lastHeadPos);
            }
            _lastHeadPos = head.position;
            _hasLastHeadPos = true;

            var sb = new StringBuilder(64);
            sb.Append("pos=").Append(V(head.position)).Append("|rot=").Append(V(head.eulerAngles));

            if (gazeTracker != null)
            {
                sb.Append("|looking_at=").Append(gazeTracker.CurrentlyLookingAt);
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
                loggerDurationSeconds = LoggerDurationSeconds,
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
                    : (backpack != null ? new List<string>(backpack.StoredItemNames) : new List<string>())
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

            // Looking.
            public float totalGazeSeconds;
            public int lookCount;
            public float longestStareSeconds;
            public float firstLookAt = -1f;
            public int gazeFreezeCount;
            public float gazeFreezeTotalSeconds;
        }

        [Serializable]
        public class SessionSummary
        {
            public string participantId;
            public string startedAtIso;
            public string writtenAtIso;
            public float loggerDurationSeconds;
            public float sessionElapsedSeconds;
            public int totalEvents;
            public float headPathMeters;
            public int freezeCount;
            public float totalFreezeSeconds;
            public int objectsLookedAt;
            public List<string> packingOrder = new List<string>();
            public List<string> finalBackpackContents = new List<string>();
            public List<ItemSummary> items = new List<ItemSummary>();
        }
    }
}