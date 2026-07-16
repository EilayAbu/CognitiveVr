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
    /// Central experiment logger. Writes one timestamped CSV of every event
    /// plus a JSON summary at session end. Purely a subscriber: it hooks into
    /// the existing SessionTimer, FreezeDetector and BackpackInventoryZone
    /// events without modifying any of them.
    ///
    /// Item interactions arrive either from <see cref="ItemUsageTracker"/>
    /// components (recommended, zero wiring) or from Inspector-wired
    /// UnityEvents calling the public Log* methods (e.g. an
    /// InteractableUnityEventWrapper "When Select" list).
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

        [Header("Scene References (auto-found if left empty)")]
        [SerializeField] private SessionTimer sessionTimer;
        [SerializeField] private FreezeDetector freezeDetector;
        [SerializeField] private BackpackInventoryZone backpack;

        [Header("Continuous Tracking")]
        [Tooltip("Seconds between head/hand pose samples written to the CSV. 0 = disabled.")]
        [SerializeField] private float poseSampleInterval = 1f;

        // ------------------------------------------------------------------ //

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private StreamWriter _writer;
        private string _csvPath;
        private string _summaryPath;

        private float _loggerStartRealtime;
        private DateTime _startedAt;
        private int _eventCount;
        private bool _sessionStarted;

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
            if (freezeDetector == null) freezeDetector = FindFirstObjectByType<FreezeDetector>();
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

            if (freezeDetector != null)
            {
                freezeDetector.OnFreezeStarted += HandleFreezeStarted;
                freezeDetector.OnFreezeEnded += HandleFreezeEnded;
                freezeDetector.OnGazeFreezeReported += HandleGazeFreezeReported;
            }

            if (backpack != null)
            {
                backpack.WhenItemEntered += HandleBackpackItemEntered;
                backpack.WhenItemExited += HandleBackpackItemExited;
            }
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

            if (freezeDetector != null)
            {
                freezeDetector.OnFreezeStarted -= HandleFreezeStarted;
                freezeDetector.OnFreezeEnded -= HandleFreezeEnded;
                freezeDetector.OnGazeFreezeReported -= HandleGazeFreezeReported;
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
                WriteSummary();
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            WriteSummary();

            if (_writer != null)
            {
                Log("session", "logger_stop", "", LoggerElapsed, null);
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }

            Instance = null;
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

        /// <summary>Convenience for simple UI / poke buttons wired in the Inspector.</summary>
        public void LogButtonPress(string buttonName)
        {
            ItemSummary stats = GetStats(buttonName);
            stats.selectCount++;
            if (stats.firstInteractionAt < 0f) stats.firstInteractionAt = LoggerElapsed;

            Log("interaction", "button_press", buttonName, null, null);
        }

        /// <summary>
        /// Optional hook for per-object gaze scripts (e.g. GazeFreezeReporter)
        /// to record EVERY glance, not only long-stare freezes.
        /// </summary>
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
            Log("session", "session_end", "",
                sessionTimer != null ? sessionTimer.ElapsedTime : (float?)null, null);
            WriteSummary();
        }

        private void HandleScheduledEvent(SessionTimer.ScheduledEvent evt)
        {
            Log("session", "scheduled_event", evt.Id, evt.TriggerTime, $"display={evt.DisplayName}");
        }

        private void HandleTimeWarning(float elapsed)
        {
            Log("session", "time_warning", "", elapsed, null);
        }

        private void HandleFreezeStarted(float idleTime, Vector3 gazePos)
        {
            Log("freeze", "freeze_start", "", idleTime, $"gaze_pos={V(gazePos)}");
        }

        private void HandleFreezeEnded(float duration, Vector3 gazePos)
        {
            Log("freeze", "freeze_end", "", duration, $"gaze_pos={V(gazePos)}");
        }

        private void HandleGazeFreezeReported(GazeFreezeRecord record)
        {
            ItemSummary stats = GetStats(record.ObjectName);
            stats.gazeFreezeCount++;
            stats.gazeFreezeTotalSeconds += record.Duration;

            Log("gaze", "gaze_freeze", record.ObjectName, record.Duration,
                $"distance_m={record.Distance.ToString("F2", Inv)}");
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
        /// </summary>
        public void Log(string category, string eventName, string objectName, float? value, string details)
        {
            if (_writer == null) return;

            _eventCount++;

            string realTime = DateTime.Now.ToString("HH:mm:ss.fff", Inv);
            string tLogger = LoggerElapsed.ToString("F3", Inv);
            string tSession = (_sessionStarted && sessionTimer != null)
                ? sessionTimer.ElapsedTime.ToString("F3", Inv)
                : "";
            string wallClock = sessionTimer != null ? sessionTimer.WallClockFormatted : "";
            string valueStr = value.HasValue ? value.Value.ToString("F3", Inv) : "";

            _writer.WriteLine(string.Join(",",
                Esc(realTime), Esc(tLogger), Esc(tSession), Esc(wallClock),
                Esc(category), Esc(eventName), Esc(objectName), Esc(valueStr), Esc(details ?? "")));

            if (logToConsole && category != "tracking")
            {
                Debug.Log($"[Data {tLogger}s] {category}/{eventName} {objectName} {details}");
            }
        }

        private void OpenLogFile()
        {
            string dir = Path.Combine(Application.persistentDataPath, outputSubfolder);
            Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
            _csvPath = Path.Combine(dir, $"{participantId}_{stamp}_events.csv");
            _summaryPath = Path.Combine(dir, $"{participantId}_{stamp}_summary.json");

            // UTF-8 with BOM so Hebrew display names open correctly in Excel.
            _writer = new StreamWriter(_csvPath, false, new UTF8Encoding(true)) { AutoFlush = true };
            _writer.WriteLine("real_time,t_logger_s,t_session_s,wall_clock,category,event,object,value,details");

            _loggerStartRealtime = Time.realtimeSinceStartup;
            _startedAt = DateTime.Now;
            _nextPoseSampleAt = Time.realtimeSinceStartup + Mathf.Max(poseSampleInterval, 0.01f);

            Log("session", "logger_start", "", null,
                $"participant={participantId}|date={_startedAt.ToString("yyyy-MM-dd", Inv)}");

            Debug.Log($"[{nameof(ExperimentDataManager)}] Logging to: {_csvPath}");
        }

        private void SamplePose()
        {
            Transform head = freezeDetector != null
                ? freezeDetector.HeadTransform
                : (Camera.main != null ? Camera.main.transform : null);

            if (head == null) return;

            if (_hasLastHeadPos)
            {
                _headPathMeters += Vector3.Distance(head.position, _lastHeadPos);
            }
            _lastHeadPos = head.position;
            _hasLastHeadPos = true;

            var sb = new StringBuilder(96);
            sb.Append("pos=").Append(V(head.position)).Append("|rot=").Append(V(head.eulerAngles));

            if (freezeDetector != null)
            {
                if (freezeDetector.LeftHand != null)
                    sb.Append("|left=").Append(V(freezeDetector.LeftHand.position));
                if (freezeDetector.RightHand != null)
                    sb.Append("|right=").Append(V(freezeDetector.RightHand.position));
            }

            Log("tracking", "pose", "head", null, sb.ToString());
        }

        private void WriteSummary()
        {
            if (string.IsNullOrEmpty(_summaryPath)) return;

            var summary = new SessionSummary
            {
                participantId = participantId,
                startedAtIso = _startedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                writtenAtIso = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                loggerDurationSeconds = LoggerElapsed,
                sessionElapsedSeconds = sessionTimer != null ? sessionTimer.ElapsedTime : 0f,
                totalEvents = _eventCount,
                headPathMeters = _headPathMeters,
                freezeCount = freezeDetector != null ? freezeDetector.FreezeCount : 0,
                totalFreezeSeconds = freezeDetector != null ? freezeDetector.TotalFreezeTime : 0f,
                packingOrder = new List<string>(_packingOrder),
                finalBackpackContents = backpack != null
                    ? new List<string>(backpack.StoredItemNames)
                    : new List<string>()
            };

            foreach (KeyValuePair<string, ItemSummary> pair in _itemStats)
            {
                pair.Value.inBackpackAtEnd = summary.finalBackpackContents.Contains(pair.Key);
                summary.items.Add(pair.Value);
            }

            try
            {
                File.WriteAllText(_summaryPath, JsonUtility.ToJson(summary, true), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(ExperimentDataManager)}] Failed to write summary: {e.Message}", this);
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
            public int selectCount;
            public float totalHeldSeconds;
            public float firstInteractionAt = -1f;
            public int gazeFreezeCount;
            public float gazeFreezeTotalSeconds;
            public int backpackInCount;
            public int backpackOutCount;
            public bool inBackpackAtEnd;
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
            public List<string> packingOrder = new List<string>();
            public List<string> finalBackpackContents = new List<string>();
            public List<ItemSummary> items = new List<ItemSummary>();
        }
    }
}
