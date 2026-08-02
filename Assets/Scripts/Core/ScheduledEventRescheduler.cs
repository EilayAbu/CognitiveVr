using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Core
{
    /// <summary>
    /// Retimes SessionTimer scheduled events by Id in response to a gameplay action.
    /// Wire Apply() into any UnityEvent (a phone message reply, a finished task, a
    /// button press) and every configured target is moved to
    /// SessionTimer.ElapsedTime + DelaySeconds so it fires later in the session.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScheduledEventRescheduler : MonoBehaviour
    {
        public enum RescheduleMode
        {
            /// <summary>TriggerTime = ElapsedTime + DelaySeconds.</summary>
            FromNow,

            /// <summary>TriggerTime += DelaySeconds.</summary>
            ShiftExisting,

            /// <summary>TriggerTime = DelaySeconds (seconds from session start).</summary>
            Absolute,

            /// <summary>Fires the event right away.</summary>
            Immediate
        }

        [Serializable]
        public class Target
        {
            [Tooltip("Id of the SessionTimer scheduled event to retime (e.g. rain_push).")]
            public string EventId;

            [Tooltip("Seconds. Meaning depends on Mode.")]
            public float DelaySeconds = 30f;

            public RescheduleMode Mode = RescheduleMode.FromNow;

            [Tooltip("Clear the Triggered flag so the event can fire again even if it already fired.")]
            public bool AllowRetrigger = true;
        }

        [Header("Bindings (auto-resolved if empty)")]
        [SerializeField] private SessionTimer _sessionTimer;

        [Header("Targets")]
        [SerializeField] private List<Target> _targets = new List<Target>();

        [Header("Options")]
        [Tooltip("Ignore any call after the first successful one.")]
        [SerializeField] private bool _onlyOnce = true;

        [SerializeField] private bool _verboseLogs = true;

        [Header("Event")]
        [SerializeField] private UnityEvent _onApplied;

        private bool _applied;

        public IReadOnlyList<Target> Targets => _targets;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>Applies every configured target. Wire this into a UnityEvent.</summary>
        public void Apply()
        {
            if (_onlyOnce && _applied) return;
            if (!ResolveReferences()) return;

            bool any = false;
            for (int i = 0; i < _targets.Count; i++)
                any |= ApplyTarget(_targets[i]);

            if (!any) return;

            _applied = true;
            _onApplied?.Invoke();
        }

        /// <summary>Applies only the target whose EventId matches.</summary>
        public void Apply(string eventId)
        {
            if (_onlyOnce && _applied) return;
            if (!ResolveReferences()) return;

            bool any = false;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i] != null && _targets[i].EventId == eventId)
                    any |= ApplyTarget(_targets[i]);
            }

            if (!any)
            {
                if (_verboseLogs)
                    Debug.LogWarning($"[{nameof(ScheduledEventRescheduler)}] No target configured for Id '{eventId}'.", this);
                return;
            }

            _applied = true;
            _onApplied?.Invoke();
        }

        /// <summary>Applies every target with a runtime delay that overrides the Inspector value.</summary>
        public void ApplyWithDelay(float delaySeconds)
        {
            if (_onlyOnce && _applied) return;
            if (!ResolveReferences()) return;

            bool any = false;
            for (int i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (target == null) continue;

                any |= ApplyTarget(new Target
                {
                    EventId = target.EventId,
                    DelaySeconds = delaySeconds,
                    Mode = target.Mode == RescheduleMode.Immediate ? RescheduleMode.FromNow : target.Mode,
                    AllowRetrigger = target.AllowRetrigger
                });
            }

            if (!any) return;

            _applied = true;
            _onApplied?.Invoke();
        }

        /// <summary>Allows Apply() to run again after _onlyOnce blocked it.</summary>
        public void ResetApplied()
        {
            _applied = false;
        }

        private bool ApplyTarget(Target target)
        {
            if (target == null || string.IsNullOrEmpty(target.EventId)) return false;

            bool ok;
            switch (target.Mode)
            {
                case RescheduleMode.ShiftExisting:
                    ok = _sessionTimer.ShiftEventTriggerTime(target.EventId, target.DelaySeconds, target.AllowRetrigger);
                    break;

                case RescheduleMode.Absolute:
                    ok = _sessionTimer.SetEventTriggerTime(target.EventId, target.DelaySeconds, target.AllowRetrigger);
                    break;

                case RescheduleMode.Immediate:
                    ok = _sessionTimer.TriggerEventNow(target.EventId);
                    break;

                default:
                    ok = _sessionTimer.RescheduleEventFromNow(target.EventId, target.DelaySeconds, target.AllowRetrigger);
                    break;
            }

            if (ok && _verboseLogs)
            {
                var evt = _sessionTimer.GetEvent(target.EventId);
                Debug.Log($"[{nameof(ScheduledEventRescheduler)}] '{target.EventId}' -> {evt?.TriggerTime:F1}s " +
                          $"({target.Mode}, now {_sessionTimer.ElapsedTime:F1}s).", this);
            }

            return ok;
        }

        private bool ResolveReferences()
        {
            if (_sessionTimer == null)
            {
#if UNITY_2023_1_OR_NEWER
                _sessionTimer = FindFirstObjectByType<SessionTimer>();
#else
                _sessionTimer = FindObjectOfType<SessionTimer>();
#endif
            }

            if (_sessionTimer == null && _verboseLogs)
                Debug.LogWarning($"[{nameof(ScheduledEventRescheduler)}] No SessionTimer in scene; nothing to reschedule.", this);

            return _sessionTimer != null;
        }
    }
}
