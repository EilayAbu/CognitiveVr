using CognitiveVR.Core;
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

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = true;

        private bool _smsAlreadyDispatched;
        private bool _rainAlreadyDispatched;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (_phone != null)
                _phone.OnWeatherMessageOpenedEvent += HandleWeatherMessageOpened;

            if (_sessionTimer == null) return;

            _sessionTimer.OnScheduledEventTriggered += HandleScheduledEvent;
            _sessionTimer.OnSessionStarted += HandleSessionStarted;
        }

        private void OnDisable()
        {
            if (_phone != null)
                _phone.OnWeatherMessageOpenedEvent -= HandleWeatherMessageOpened;

            if (_sessionTimer == null) return;

            _sessionTimer.OnScheduledEventTriggered -= HandleScheduledEvent;
            _sessionTimer.OnSessionStarted -= HandleSessionStarted;
        }

        /// <summary>
        /// Fired when the user opens the weather message via its button. Treats
        /// this as the user having read the forecast so CheckWeather task
        /// progress advances only on the actual open action.
        /// </summary>
        private void HandleWeatherMessageOpened()
        {
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

            if (_verboseLogs)
                Debug.Log($"[{nameof(PhoneSessionEventBridge)}] Rain push dispatched at {now:F1}s ({timestampLabel}).", this);
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
    }
}
