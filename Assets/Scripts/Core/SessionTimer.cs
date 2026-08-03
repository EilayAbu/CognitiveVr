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
        public event Action<ScheduledEvent> OnScheduledEventRescheduled;
        public event Action OnSessionStarted;
        public event Action OnSessionEnded;
        public event Action<float> OnTimeWarning;

        private bool _timeWarningFired;

        private void Awake()
        {
            InitializeDefaultEvents();
        }
        private void Start()
        {
            //StartSession();
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
            if (!_isRunning)
            {
                _elapsedTime = 0f;
                _isRunning = true;
                _timeWarningFired = false;

                foreach (var evt in ScheduledEvents)
                    evt.Triggered = false;

                OnSessionStarted?.Invoke();
            }
            
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

        /// <summary>
        /// Finds a scheduled event by its Id. Returns null when no event matches.
        /// </summary>
        public ScheduledEvent GetEvent(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < ScheduledEvents.Count; i++)
            {
                var evt = ScheduledEvents[i];
                if (evt != null && evt.Id == id)
                    return evt;
            }

            return null;
        }

        public bool TryGetEvent(string id, out ScheduledEvent evt)
        {
            evt = GetEvent(id);
            return evt != null;
        }

        /// <summary>
        /// Moves an event to fire <paramref name="delaySeconds"/> after the current
        /// elapsed time. Use after finishing an action to chain the next event.
        /// </summary>
        public bool RescheduleEventFromNow(string id, float delaySeconds, bool allowRetrigger = true)
        {
            return SetEventTriggerTime(id, _elapsedTime + Mathf.Max(0f, delaySeconds), allowRetrigger);
        }

        /// <summary>
        /// Adds <paramref name="offsetSeconds"/> to the event's existing trigger time.
        /// </summary>
        public bool ShiftEventTriggerTime(string id, float offsetSeconds, bool allowRetrigger = true)
        {
            var evt = GetEvent(id);
            if (evt == null)
            {
                LogMissingEvent(id);
                return false;
            }

            return SetEventTriggerTime(id, evt.TriggerTime + offsetSeconds, allowRetrigger);
        }

        /// <summary>
        /// Sets an absolute trigger time (seconds from session start).
        /// When <paramref name="allowRetrigger"/> is true the event may fire again
        /// even if it already fired once.
        /// </summary>
        public bool SetEventTriggerTime(string id, float triggerTime, bool allowRetrigger = true)
        {
            var evt = GetEvent(id);
            if (evt == null)
            {
                LogMissingEvent(id);
                return false;
            }

            evt.TriggerTime = Mathf.Max(0f, triggerTime);
            if (allowRetrigger)
                evt.Triggered = false;

            OnScheduledEventRescheduled?.Invoke(evt);
            return true;
        }

        /// <summary>
        /// Fires an event immediately, regardless of its trigger time.
        /// </summary>
        public bool TriggerEventNow(string id)
        {
            var evt = GetEvent(id);
            if (evt == null)
            {
                LogMissingEvent(id);
                return false;
            }

            evt.TriggerTime = _elapsedTime;
            evt.Triggered = true;
            OnScheduledEventTriggered?.Invoke(evt);
            return true;
        }

        private void LogMissingEvent(string id)
        {
            Debug.LogWarning($"[{nameof(SessionTimer)}] No scheduled event with Id '{id}'.", this);
        }

        private void InitializeDefaultEvents()
        {
            if (ScheduledEvents.Count > 0) return;

            ScheduledEvents = new List<ScheduledEvent>
            {
                new ScheduledEvent { Id = "sms_plan_change", TriggerTime = 120f, DisplayName = "הודעת SMS - שינוי תוכנית" },
                new ScheduledEvent { Id = "rain_push", TriggerTime = 240f, DisplayName = "התראת גשם בטלפון" },
                new ScheduledEvent { Id = "neighbor_knock", TriggerTime = 180f, DisplayName = "דפיקת שכנה בדלת" },
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
