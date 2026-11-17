using System;
using System.Globalization;
using CognitiveVR.Core;
using CognitiveVR.Data;
using UnityEngine;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Bridges the global SessionTimer's scheduled events into the phone's
    /// notification system. Specifically:
    ///   - "sms_plan_change" (T+2:00) becomes the laptop->tablet SMS.
    ///   - "rain_push" (T+4:00) becomes the rain push alert.
    /// Hebrew text is hard-coded per spec. Reaction-time tracking starts when
    /// the SMS arrives via the wired SmsSwapTracker.
    ///
    /// Also the data collection point for "did the participant actually read
    /// the message". Logged under category "task", object BossSmsMessage or
    /// WeatherAlertMessage:
    ///   phone_msg_arrived     - the open-message button lit up. value =
    ///                           t_logger_s of the arrival.
    ///   phone_msg_opened      - the message was revealed. value = seconds
    ///                           between arrival and this open.
    ///   phone_msg_closed      - the message was dismissed. value = seconds it
    ///                           stayed on screen.
    ///   phone_msg_never_opened- written once at session end for a message that
    ///                           arrived and was never opened, so a message the
    ///                           participant ignored is explicit in the data.
    /// The same numbers accumulate into a <see cref="PhoneMessagesSummary"/>
    /// that ExperimentDataManager embeds in the session summary JSON.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneSessionEventBridge : MonoBehaviour
    {
        public const string SmsPlanChangeId = "sms_plan_change";
        public const string RainPushId = "rain_push";

        private const string SmsTitle = "הודעת SMS";
        private const string SmsBody = "שינוי בתוכנית: אל תביא לפטופ. קח את הטאבלט שנמצא על הספה";

        private const string RainTitle = "התראת מזג אוויר";
        private const string RainBody = "התרעה: סופת גשם צפויה בשעות הקרובות. שקול לקחת מטריה.";

        [Header("Bindings (auto-resolved if empty)")]
        [SerializeField] private SessionTimer _sessionTimer;
        [SerializeField] private PhoneScreenController _phone;
        [SerializeField] private PhoneNotificationManager _notificationManager;
        [SerializeField] private SmsSwapTracker _smsSwapTracker;
        [SerializeField] private WeatherAppScreen _weatherAppScreen;

        [Header("Read Detection (gaze while open)")]
        [Tooltip("Resolved GazeObjectTracker names that count as 'looking at the phone' (exact match).")]
        [SerializeField] private string[] _phoneGazeNames = { "Phone", "Phone Gaze" };
        [Tooltip("Seconds of head gaze on the phone while a message is open before it counts as read (phone_msg_read).")]
        [SerializeField] private float _readThresholdSeconds = 1.5f;

        [Header("Umbrella vs. Rain Alert")]
        [Tooltip("Backpack item name of the umbrella (InventoryItemMetaBridge = GameObject name).")]
        [SerializeField] private string _umbrellaItemName = "umbrella";
        [SerializeField] private CognitiveVR.Interaction.BackpackInventoryZone _backpack;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = true;

        private const string BossLogName = "BossSmsMessage";
        private const string WeatherLogName = "WeatherAlertMessage";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private bool _smsAlreadyDispatched;
        private bool _rainAlreadyDispatched;

        private readonly PhoneMessagesSummary _messagesSummary = new PhoneMessagesSummary();
        private MessageTracker _bossTracker;
        private MessageTracker _weatherTracker;
        private bool _sessionEndFlushed;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            Instance = this;
            ResolveReferences();
            EnsureTrackers();
        }

        private void OnEnable()
        {
            Instance = this;
            ResolveReferences();
            EnsureTrackers();

            if (_backpack != null)
                _backpack.WhenItemEntered += HandleBackpackItemEntered;

            if (_phone != null)
            {
                _phone.OnBossMessageButtonShownEvent += HandleBossMessageArrived;
                _phone.OnBossMessageOpenedEvent += HandleBossMessageOpened;
                _phone.OnBossMessageClosedEvent += HandleBossMessageClosed;
                _phone.OnWeatherMessageButtonShownEvent += HandleWeatherMessageArrived;
                _phone.OnWeatherMessageOpenedEvent += HandleWeatherMessageOpened;
                _phone.OnWeatherMessageClosedEvent += HandleWeatherMessageClosed;
            }

            // Push model, same as GuideDataBridge: the manager keeps the live
            // bridge so the phone can be inactive at scene load and still end up
            // in the summary JSON.
            Manager?.RegisterPhoneMessagesBridge(this);

            if (_sessionTimer == null) return;

            _sessionTimer.OnScheduledEventTriggered += HandleScheduledEvent;
            _sessionTimer.OnSessionStarted += HandleSessionStarted;
        }

        private void OnDisable()
        {
            if (_backpack != null)
                _backpack.WhenItemEntered -= HandleBackpackItemEntered;

            if (_phone != null)
            {
                _phone.OnBossMessageButtonShownEvent -= HandleBossMessageArrived;
                _phone.OnBossMessageOpenedEvent -= HandleBossMessageOpened;
                _phone.OnBossMessageClosedEvent -= HandleBossMessageClosed;
                _phone.OnWeatherMessageButtonShownEvent -= HandleWeatherMessageArrived;
                _phone.OnWeatherMessageOpenedEvent -= HandleWeatherMessageOpened;
                _phone.OnWeatherMessageClosedEvent -= HandleWeatherMessageClosed;
            }

            if (_sessionTimer == null) return;

            _sessionTimer.OnScheduledEventTriggered -= HandleScheduledEvent;
            _sessionTimer.OnSessionStarted -= HandleSessionStarted;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Fallback for aborted runs. A normal run has already been flushed by
            // ExperimentDataManager.FinalizeSession, while the log was still open.
            // Not OnDisable: the phone may legitimately be toggled off mid-session.
            FlushSessionEnd();
        }

        private void Update()
        {
            // Keeps totalOpenSeconds honest even if the session ends while a
            // message is still on screen - the manager reads the same object.
            TickOpenWindow(_bossTracker);
            TickOpenWindow(_weatherTracker);

            bool lookingAtPhone = IsLookingAtPhone();
            TickReadGaze(_bossTracker, lookingAtPhone);
            TickReadGaze(_weatherTracker, lookingAtPhone);
        }

        // ------------------------------------------------------------------ //
        // "Read" = head gaze on the phone while the message is on screen
        // ------------------------------------------------------------------ //

        private bool IsLookingAtPhone()
        {
            GazeObjectTracker gaze = GazeObjectTracker.Instance;
            if (gaze == null || _phoneGazeNames == null) return false;

            string current = gaze.CurrentlyLookingAt;
            if (string.IsNullOrEmpty(current)) return false;

            for (int i = 0; i < _phoneGazeNames.Length; i++)
                if (current == _phoneGazeNames[i]) return true;
            return false;
        }

        /// <summary>
        /// Accumulates gaze-on-phone seconds while this message is open and writes
        /// phone_msg_read the first time the total crosses the threshold.
        /// </summary>
        private void TickReadGaze(MessageTracker tracker, bool lookingAtPhone)
        {
            if (tracker == null || tracker.OpenStartedAt < 0f || !lookingAtPhone) return;

            MessageStats stats = tracker.Stats;
            float dt = Time.unscaledDeltaTime;
            tracker.GazeThisOpen += dt;
            stats.gazeWhileOpenSeconds += dt;

            if (stats.read || stats.gazeWhileOpenSeconds < _readThresholdSeconds) return;

            float now = LoggerNow();
            stats.read = true;
            stats.readAt = now;
            stats.readAtSession = ResolveSessionTime();

            float sinceOpen = Mathf.Max(0f, now - tracker.OpenStartedAt);
            float sinceArrival = stats.arrivedAt >= 0f ? Mathf.Max(0f, now - stats.arrivedAt) : -1f;

            Manager?.Log("task", "phone_msg_read", tracker.LogName, stats.gazeWhileOpenSeconds,
                $"threshold_s={_readThresholdSeconds.ToString("F2", Inv)}" +
                $"|since_open_s={sinceOpen.ToString("F2", Inv)}" +
                (sinceArrival >= 0f ? $"|since_arrival_s={sinceArrival.ToString("F2", Inv)}" : "") +
                $"|t_session_s={stats.readAtSession.ToString("F2", Inv)}");
        }

        /// <summary>
        /// Fired when the user opens the weather message via its button. Treats
        /// this as the user having read the forecast so CheckWeather task
        /// progress advances only on the actual open action.
        /// </summary>
        private void HandleWeatherMessageOpened()
        {
            RegisterOpened(_weatherTracker);

            if (_weatherAppScreen == null && _phone != null)
                _weatherAppScreen = _phone.GetComponent<WeatherAppScreen>();
            if (_weatherAppScreen == null)
#if UNITY_2023_1_OR_NEWER
                _weatherAppScreen = FindFirstObjectByType<WeatherAppScreen>();
#else
                _weatherAppScreen = FindObjectOfType<WeatherAppScreen>();
#endif
            if (_weatherAppScreen != null)
                _weatherAppScreen.NotifyOpened();
        }

        // ------------------------------------------------------------------ //
        // Message read-tracking
        // ------------------------------------------------------------------ //

        private void HandleBossMessageArrived() => RegisterArrived(_bossTracker);
        private void HandleBossMessageOpened() => RegisterOpened(_bossTracker);
        private void HandleBossMessageClosed() => RegisterClosed(_bossTracker);
        private void HandleWeatherMessageArrived() => RegisterArrived(_weatherTracker);
        private void HandleWeatherMessageClosed() => RegisterClosed(_weatherTracker);

        private void EnsureTrackers()
        {
            if (_bossTracker == null)
                _bossTracker = new MessageTracker(BossLogName, _messagesSummary.bossSms);
            if (_weatherTracker == null)
                _weatherTracker = new MessageTracker(WeatherLogName, _messagesSummary.weatherAlert);
        }

        private void RegisterArrived(MessageTracker tracker)
        {
            MessageStats stats = tracker.Stats;
            if (stats.arrived) return;

            stats.arrived = true;
            stats.arrivedAt = LoggerNow();
            stats.arrivedAtSession = ResolveSessionTime();

            Manager?.Log("task", "phone_msg_arrived", tracker.LogName, stats.arrivedAt,
                $"t_session_s={stats.arrivedAtSession.ToString("F2", Inv)}" +
                $"|wall_clock={ResolveWallClockLabel()}");
        }

        private void RegisterOpened(MessageTracker tracker)
        {
            MessageStats stats = tracker.Stats;
            float now = LoggerNow();

            tracker.OpenStartedAt = now;
            tracker.GazeThisOpen = 0f;
            stats.openCount++;
            stats.stillOpenAtEnd = true;

            float sinceArrival = stats.arrived && stats.arrivedAt >= 0f
                ? Mathf.Max(0f, now - stats.arrivedAt)
                : -1f;

            bool firstOpen = !stats.opened;
            if (firstOpen)
            {
                stats.opened = true;
                stats.firstOpenAt = now;
                stats.firstOpenAtSession = ResolveSessionTime();
                stats.openLatencySeconds = sinceArrival;
            }

            Manager?.Log("task", "phone_msg_opened", tracker.LogName,
                sinceArrival >= 0f ? sinceArrival : (float?)null,
                $"first_open={(firstOpen ? 1 : 0)}" +
                $"|open_count={stats.openCount}" +
                $"|t_session_s={ResolveSessionTime().ToString("F2", Inv)}" +
                (sinceArrival >= 0f
                    ? $"|since_arrival_s={sinceArrival.ToString("F2", Inv)}"
                    : "|arrival_not_recorded=1"));

            if (_verboseLogs)
                Debug.Log($"[{nameof(PhoneSessionEventBridge)}] {tracker.LogName} opened " +
                          $"(open #{stats.openCount}, {sinceArrival:F1}s after arrival).", this);
        }

        private void RegisterClosed(MessageTracker tracker)
        {
            MessageStats stats = tracker.Stats;
            float openSeconds = CommitOpenWindow(tracker);
            stats.stillOpenAtEnd = false;

            Manager?.Log("task", "phone_msg_closed", tracker.LogName,
                openSeconds >= 0f ? openSeconds : (float?)null,
                $"open_count={stats.openCount}" +
                $"|total_open_s={stats.totalOpenSeconds.ToString("F2", Inv)}" +
                (openSeconds >= 0f
                    ? $"|open_s={openSeconds.ToString("F2", Inv)}"
                    : "|open_not_recorded=1") +
                $"|gaze_while_open_s={tracker.GazeThisOpen.ToString("F2", Inv)}" +
                $"|read={(stats.read ? 1 : 0)}");
        }

        /// <summary>
        /// Advances the live open-duration numbers while a message is on screen,
        /// so the summary object the manager holds is never stale.
        /// </summary>
        private static void TickOpenWindow(MessageTracker tracker)
        {
            if (tracker == null || tracker.OpenStartedAt < 0f) return;

            float window = Mathf.Max(0f, LoggerNow() - tracker.OpenStartedAt);
            tracker.Stats.totalOpenSeconds = tracker.CommittedOpenSeconds + window;
            if (window > tracker.Stats.longestOpenSeconds)
                tracker.Stats.longestOpenSeconds = window;
        }

        /// <summary>Closes the current open window and returns its length, or -1.</summary>
        private static float CommitOpenWindow(MessageTracker tracker)
        {
            if (tracker.OpenStartedAt < 0f) return -1f;

            float window = Mathf.Max(0f, LoggerNow() - tracker.OpenStartedAt);
            tracker.OpenStartedAt = -1f;
            tracker.CommittedOpenSeconds += window;
            tracker.Stats.totalOpenSeconds = tracker.CommittedOpenSeconds;
            if (window > tracker.Stats.longestOpenSeconds)
                tracker.Stats.longestOpenSeconds = window;

            return window;
        }

        /// <summary>
        /// Closes any open message window and writes one phone_msg_never_opened
        /// row per message that arrived and was ignored. Called by
        /// ExperimentDataManager.FinalizeSession while the log is still open, and
        /// again from OnDestroy as a fallback. Safe to call more than once.
        /// </summary>
        public void FlushSessionEnd()
        {
            if (_sessionEndFlushed) return;
            _sessionEndFlushed = true;

            FlushOne(_bossTracker);
            FlushOne(_weatherTracker);
        }

        private void FlushOne(MessageTracker tracker)
        {
            if (tracker == null) return;

            bool wasOpen = tracker.OpenStartedAt >= 0f;
            float openSeconds = CommitOpenWindow(tracker);
            MessageStats stats = tracker.Stats;

            if (wasOpen)
            {
                Manager?.Log("task", "phone_msg_closed", tracker.LogName,
                    openSeconds >= 0f ? openSeconds : (float?)null,
                    $"open_count={stats.openCount}" +
                    $"|total_open_s={stats.totalOpenSeconds.ToString("F2", Inv)}" +
                    $"|gaze_while_open_s={tracker.GazeThisOpen.ToString("F2", Inv)}" +
                    $"|read={(stats.read ? 1 : 0)}" +
                    "|closed_by=session_end");
            }

            // Opened (or not) but never looked at long enough to count as read.
            if (stats.arrived && !stats.read)
            {
                Manager?.Log("task", "phone_msg_never_read", tracker.LogName, stats.gazeWhileOpenSeconds,
                    $"opened={(stats.opened ? 1 : 0)}" +
                    $"|gaze_while_open_s={stats.gazeWhileOpenSeconds.ToString("F2", Inv)}" +
                    $"|threshold_s={_readThresholdSeconds.ToString("F2", Inv)}");
            }

            if (!stats.arrived || stats.opened) return;

            float ignoredFor = stats.arrivedAt >= 0f
                ? Mathf.Max(0f, LoggerNow() - stats.arrivedAt)
                : -1f;

            Manager?.Log("task", "phone_msg_never_opened", tracker.LogName,
                ignoredFor >= 0f ? ignoredFor : (float?)null,
                $"arrived_at={stats.arrivedAt.ToString("F2", Inv)}" +
                (ignoredFor >= 0f ? $"|ignored_for_s={ignoredFor.ToString("F2", Inv)}" : ""));
        }

        /// <summary>
        /// Snapshot of the phone messages for the session summary JSON. The
        /// object is live: the manager can hold it and read it at session end.
        /// </summary>
        public PhoneMessagesSummary BuildSummary()
        {
            TickOpenWindow(_bossTracker);
            TickOpenWindow(_weatherTracker);
            return _messagesSummary;
        }

        private static float LoggerNow()
        {
            return Manager != null ? Manager.LoggerElapsed : Time.realtimeSinceStartup;
        }

        private void HandleSessionStarted()
        {
            _smsAlreadyDispatched = false;
            _rainAlreadyDispatched = false;
        }

        private void HandleScheduledEvent(SessionTimer.ScheduledEvent evt)
        {
            if (evt == null) return;

            switch (evt.Id)
            {
                case SmsPlanChangeId:
                    DispatchSms();
                    break;

                case RainPushId:
                    DispatchRainAlert();
                    break;
            }
        }

        private void DispatchSms()
        {
            if (_smsAlreadyDispatched) return;
            _smsAlreadyDispatched = true;

            if (_notificationManager == null)
            {
                if (_verboseLogs)
                    Debug.LogWarning($"[{nameof(PhoneSessionEventBridge)}] Notification manager not bound; cannot push SMS.", this);
                return;
            }

            float now = ResolveSessionTime();
            string timestampLabel = ResolveWallClockLabel();

            var data = new PhoneNotificationData(
                id: SmsPlanChangeId,
                kind: PhoneNotificationKind.Sms,
                title: SmsTitle,
                body: SmsBody,
                timestampLabel: timestampLabel,
                createdAt: now);

            _notificationManager.PushNotification(data);

            if (_phone != null)
                _phone.ShowBossMessageButton();

            if (_smsSwapTracker == null && _phone != null)
                _smsSwapTracker = _phone.GetComponent<SmsSwapTracker>();
            if (_smsSwapTracker == null)
#if UNITY_2023_1_OR_NEWER
                _smsSwapTracker = FindFirstObjectByType<SmsSwapTracker>();
#else
                _smsSwapTracker = FindObjectOfType<SmsSwapTracker>();
#endif

            if (_smsSwapTracker != null)
                _smsSwapTracker.OnSmsAppeared(now);

            if (_verboseLogs)
                Debug.Log($"[{nameof(PhoneSessionEventBridge)}] SMS plan-change dispatched at {now:F1}s ({timestampLabel}).", this);
        }

        private void DispatchRainAlert()
        {
            if (_rainAlreadyDispatched) return;
            _rainAlreadyDispatched = true;

            if (_notificationManager == null)
            {
                if (_verboseLogs)
                    Debug.LogWarning($"[{nameof(PhoneSessionEventBridge)}] Notification manager not bound; cannot push rain alert.", this);
                return;
            }

            float now = ResolveSessionTime();
            string timestampLabel = ResolveWallClockLabel();

            var data = new PhoneNotificationData(
                id: RainPushId,
                kind: PhoneNotificationKind.WeatherAlert,
                title: RainTitle,
                body: RainBody,
                timestampLabel: timestampLabel,
                createdAt: now);

            _notificationManager.PushNotification(data);

            if (_phone != null)
                _phone.ShowWeatherMessageButton();

            LogUmbrellaAtRainAlert(now);

            if (_verboseLogs)
                Debug.Log($"[{nameof(PhoneSessionEventBridge)}] Rain push dispatched at {now:F1}s ({timestampLabel}).", this);
        }

        // ------------------------------------------------------------------ //
        // Umbrella vs. rain alert
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Writes task/umbrella_at_rain_alert the moment the rain alert is
        /// dispatched. value = 1 if the umbrella was already in the backpack.
        /// </summary>
        private void LogUmbrellaAtRainAlert(float sessionNow)
        {
            _messagesSummary.rainAlertAtSession = sessionNow;

            bool inBag = IsInBackpack(_umbrellaItemName);
            _messagesSummary.umbrellaInBagBeforeRain = inBag ? 1 : 0;

            Manager?.Log("task", "umbrella_at_rain_alert", _umbrellaItemName, inBag ? 1f : 0f,
                $"in_bag={(inBag ? 1 : 0)}" +
                $"|rain_alert_t_session_s={sessionNow.ToString("F2", Inv)}");
        }

        private void HandleBackpackItemEntered(string itemName, CognitiveVR.Interaction.BackpackSlot slot)
        {
            if (itemName != _umbrellaItemName) return;
            if (_messagesSummary.rainAlertAtSession < 0f) return;          // alert not yet: nothing to measure
            if (_messagesSummary.umbrellaBaggedAfterRainSeconds >= 0f) return; // first time only

            float since = Mathf.Max(0f, ResolveSessionTime() - _messagesSummary.rainAlertAtSession);
            _messagesSummary.umbrellaBaggedAfterRainSeconds = since;

            Manager?.Log("task", "umbrella_bagged_after_rain", _umbrellaItemName, since,
                $"since_rain_alert_s={since.ToString("F2", Inv)}" +
                $"|was_in_bag_at_alert={_messagesSummary.umbrellaInBagBeforeRain}");
        }

        private bool IsInBackpack(string itemName)
        {
            if (_backpack == null || string.IsNullOrEmpty(itemName)) return false;
            foreach (string stored in _backpack.StoredItemNames)
                if (stored == itemName) return true;
            return false;
        }

        private float ResolveSessionTime()
        {
            return _sessionTimer != null && _sessionTimer.IsRunning
                ? _sessionTimer.ElapsedTime
                : Time.time;
        }

        private string ResolveWallClockLabel()
        {
            if (_sessionTimer == null) return "08:52";
            var (h, m, _) = _sessionTimer.WallClockTime;
            return $"{h:D2}:{m:D2}";
        }

        private void ResolveReferences()
        {
            if (_backpack == null)
            {
#if UNITY_2023_1_OR_NEWER
                _backpack = FindFirstObjectByType<CognitiveVR.Interaction.BackpackInventoryZone>();
#else
                _backpack = FindObjectOfType<CognitiveVR.Interaction.BackpackInventoryZone>();
#endif
            }

            if (_sessionTimer == null)
            {
#if UNITY_2023_1_OR_NEWER
                _sessionTimer = FindFirstObjectByType<SessionTimer>();
#else
                _sessionTimer = FindObjectOfType<SessionTimer>();
#endif
            }

            if (_phone == null)
                _phone = GetComponentInParent<PhoneScreenController>();
            if (_phone == null)
            {
#if UNITY_2023_1_OR_NEWER
                _phone = FindFirstObjectByType<PhoneScreenController>();
#else
                _phone = FindObjectOfType<PhoneScreenController>();
#endif
            }

            if (_notificationManager == null && _phone != null)
                _notificationManager = _phone.NotificationManager != null
                    ? _phone.NotificationManager
                    : _phone.GetComponent<PhoneNotificationManager>();

            if (_notificationManager == null)
            {
#if UNITY_2023_1_OR_NEWER
                _notificationManager = FindFirstObjectByType<PhoneNotificationManager>();
#else
                _notificationManager = FindObjectOfType<PhoneNotificationManager>();
#endif
            }
        }

        // ------------------------------------------------------------------ //
        // Data shapes
        // ------------------------------------------------------------------ //

        /// <summary>Per-message runtime bookkeeping that never reaches the JSON.</summary>
        private sealed class MessageTracker
        {
            public readonly string LogName;
            public readonly MessageStats Stats;

            /// <summary>t_logger_s the message was last revealed, -1 = closed.</summary>
            public float OpenStartedAt = -1f;
            /// <summary>Seconds from windows that have already been closed.</summary>
            public float CommittedOpenSeconds;
            /// <summary>Gaze-on-phone seconds during the current/last open window.</summary>
            public float GazeThisOpen;

            public MessageTracker(string logName, MessageStats stats)
            {
                LogName = logName;
                Stats = stats;
            }
        }

        /// <summary>
        /// Did the participant read the message, when, and for how long. All
        /// timestamps use the t_logger_s clock (same as the CSV); -1 = never.
        /// </summary>
        [Serializable]
        public class MessageStats
        {
            /// <summary>The open-message button lit up, i.e. the message was delivered.</summary>
            public bool arrived;
            public float arrivedAt = -1f;
            /// <summary>Same moment on the SessionTimer clock.</summary>
            public float arrivedAtSession = -1f;

            /// <summary>The message was revealed at least once.</summary>
            public bool opened;
            public float firstOpenAt = -1f;
            public float firstOpenAtSession = -1f;
            /// <summary>Seconds between delivery and the first open. -1 = never opened.</summary>
            public float openLatencySeconds = -1f;

            public int openCount;
            public float totalOpenSeconds;
            public float longestOpenSeconds;
            /// <summary>The message was still on screen when the session ended.</summary>
            public bool stillOpenAtEnd;

            /// <summary>Head gaze on the phone accumulated while this message was open (all opens).</summary>
            public float gazeWhileOpenSeconds;
            /// <summary>gazeWhileOpenSeconds reached the read threshold.</summary>
            public bool read;
            public float readAt = -1f;
            public float readAtSession = -1f;
        }

        [Serializable]
        public class PhoneMessagesSummary
        {
            public MessageStats bossSms = new MessageStats();
            public MessageStats weatherAlert = new MessageStats();

            /// <summary>Session-clock seconds when the rain alert was dispatched. -1 = never.</summary>
            public float rainAlertAtSession = -1f;
            /// <summary>1 = umbrella already in the backpack when the rain alert arrived, 0 = not, -1 = alert never came.</summary>
            public int umbrellaInBagBeforeRain = -1;
            /// <summary>Session seconds after the alert that the umbrella was bagged. -1 = not bagged after the alert.</summary>
            public float umbrellaBaggedAfterRainSeconds = -1f;
        }

        /// <summary>
        /// Session-clock (t_session_s) time of the rain alert, -1 if it has not
        /// arrived yet. Read by WindowPuddleTaskBridge for since_rain_alert_s.
        /// </summary>
        public float RainAlertAtSession => _messagesSummary.rainAlertAtSession;

        /// <summary>Last enabled bridge, so other task bridges can read the rain-alert time.</summary>
        public static PhoneSessionEventBridge Instance { get; private set; }
    }
}
