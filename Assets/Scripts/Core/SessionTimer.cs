using System;
using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Core
{
    public class SessionTimer : MonoBehaviour
    {
        [Header("Session Configuration")]
        [Tooltip("Total session duration in seconds (7 minutes = 420s)")]
        public float SessionDuration = 420f;

        [Tooltip("Wall clock start time (hours, minutes)")]
        public int WallClockStartHour = 8;
        public int WallClockStartMinute = 52;

        [Header("Scheduled Events")]
        public List<ScheduledEvent> ScheduledEvents = new List<ScheduledEvent>();

        [Header("Runtime State")]
        [SerializeField] private float _elapsedTime;
        [SerializeField] private bool _isRunning;

        public float ElapsedTime => _elapsedTime;
        public bool IsRunning => _isRunning;
        public float RemainingTime => Mathf.Max(0f, SessionDuration - _elapsedTime);

        /// <summary>
        /// Current "wall clock" time as displayed on the in-scene clock.
        /// Starts at 08:52, advances in real time.
        /// </summary>
        public (int hours, int minutes, int seconds) WallClockTime
        {
            get
            {
                int totalSeconds = WallClockStartHour * 3600 + WallClockStartMinute * 60 + Mathf.FloorToInt(_elapsedTime);
                int h = (totalSeconds / 3600) % 24;
                int m = (totalSeconds % 3600) / 60;
                int s = totalSeconds % 60;
                return (h, m, s);
            }
        }

        public string WallClockFormatted
        {
            get
            {
                var (h, m, s) = WallClockTime;
                return $"{h:D2}:{m:D2}:{s:D2}";
            }
        }

        // Events
        public event Action<ScheduledEvent> OnScheduledEventTriggered;
        public event Action OnSessionStarted;
        public event Action OnSessionEnded;
        public event Action<float> OnTimeWarning;

        private bool _timeWarningFired;

        private void Awake()
        {
            InitializeDefaultEvents();
        }

        private void Update()
        {
            if (!_isRunning) return;

            _elapsedTime += Time.deltaTime;

            CheckScheduledEvents();

            if (!_timeWarningFired && _elapsedTime >= 300f)
            {
                _timeWarningFired = true;
                OnTimeWarning?.Invoke(_elapsedTime);
            }

            if (_elapsedTime >= SessionDuration)
            {
                EndSession();
            }
        }

        public void StartSession()
        {
            _elapsedTime = 0f;
            _isRunning = true;
            _timeWarningFired = false;

            foreach (var evt in ScheduledEvents)
                evt.Triggered = false;

            OnSessionStarted?.Invoke();
        }

        public void EndSession()
        {
            _isRunning = false;
            OnSessionEnded?.Invoke();
        }

        private void CheckScheduledEvents()
        {
            for (int i = 0; i < ScheduledEvents.Count; i++)
            {
                var evt = ScheduledEvents[i];
                if (!evt.Triggered && _elapsedTime >= evt.TriggerTime)
                {
                    evt.Triggered = true;
                    ScheduledEvents[i] = evt;
                    OnScheduledEventTriggered?.Invoke(evt);
                }
            }
        }

        private void InitializeDefaultEvents()
        {
            if (ScheduledEvents.Count > 0) return;

            ScheduledEvents = new List<ScheduledEvent>
            {
                new ScheduledEvent { Id = "sms_plan_change", TriggerTime = 165f, DisplayName = "הודעת SMS - שינוי תוכנית" },
                new ScheduledEvent { Id = "neighbor_knock", TriggerTime = 240f, DisplayName = "דפיקת שכנה בדלת" },
                new ScheduledEvent { Id = "clock_reminder", TriggerTime = 300f, DisplayName = "צליל תזכורת מהשעון (08:57)" },
                new ScheduledEvent { Id = "shelf_fall", TriggerTime = 360f, DisplayName = "מדף נופל ליד הדלת" },
            };
        }

        [Serializable]
        public class ScheduledEvent
        {
            public string Id;
            public float TriggerTime;
            public string DisplayName;
            [HideInInspector] public bool Triggered;
        }
    }
}
