using System.Collections.Generic;
using CognitiveVR.Models;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Collects raw timing data during the toaster task and produces
    /// MetricRecord entries for ATTEN.1 through ATTEN.5.
    /// </summary>
    public class ToasterMetrics
    {
        private float _activationTime = -1f;
        private float _toastReadyTime = -1f;
        private float _smokeStartTime = -1f;
        private float _firstCheckTime = -1f;
        private float _removalTime = -1f;
        private int _lidOpenCount;
        private BurnSeverity _burnSeverity;

        public bool HasChecked => _firstCheckTime >= 0f;
        public int LidOpenCount => _lidOpenCount;

        public void RecordActivation(float sessionTime)
        {
            _activationTime = sessionTime;
        }

        public void RecordToastReady(float sessionTime)
        {
            if (_toastReadyTime < 0f)
                _toastReadyTime = sessionTime;
        }

        public void RecordSmokeStarted(float sessionTime)
        {
            if (_smokeStartTime < 0f)
                _smokeStartTime = sessionTime;
        }

        public void RecordLidOpened(float sessionTime)
        {
            _lidOpenCount++;
            if (_firstCheckTime < 0f)
                _firstCheckTime = sessionTime;
        }

        public void RecordToastRemoved(float sessionTime, float cookingElapsed, float cookTime, BurnSeverity severity)
        {
            _removalTime = sessionTime;
            _burnSeverity = severity;
        }

        /// <summary>
        /// Builds the final list of MetricRecord for ATTEN.1-5.
        /// Call after the task is complete (toast removed or session ended).
        /// </summary>
        public List<MetricRecord> BuildRecords()
        {
            var records = new List<MetricRecord>(5);
            float now = _removalTime >= 0f ? _removalTime : Time.time;

            // ATTEN.1 - Time to first toaster check
            float firstCheckDelta = _firstCheckTime >= 0f
                ? _firstCheckTime - _activationTime
                : -1f;
            records.Add(new MetricRecord(
                MetricType.ATTEN, 1,
                firstCheckDelta >= 0f ? firstCheckDelta : now - _activationTime,
                _activationTime,
                firstCheckDelta >= 0f
                    ? $"First check at {firstCheckDelta:F1}s after activation"
                    : "Never checked"));

            // ATTEN.2 - Number of proactive toaster checks
            records.Add(new MetricRecord(
                MetricType.ATTEN, 2,
                _lidOpenCount,
                now,
                $"{_lidOpenCount} lid opens"));

            // ATTEN.3 - Overshoot time from toast ready
            float overshoot = 0f;
            if (_toastReadyTime >= 0f && _removalTime >= 0f)
                overshoot = Mathf.Max(0f, _removalTime - _toastReadyTime);
            else if (_toastReadyTime >= 0f)
                overshoot = Mathf.Max(0f, now - _toastReadyTime);
            records.Add(new MetricRecord(
                MetricType.ATTEN, 3,
                overshoot,
                _toastReadyTime >= 0f ? _toastReadyTime : now,
                $"Overshoot {overshoot:F1}s"));

            // ATTEN.4 - Burn severity level (categorical: 0=perfect, 1=overcooked, 2=burnt)
            records.Add(new MetricRecord(
                MetricType.ATTEN, 4,
                (float)_burnSeverity,
                now,
                _burnSeverity.ToString()));

            // ATTEN.5 - Smoke reaction time
            float smokeReaction = -1f;
            if (_smokeStartTime >= 0f && _firstCheckTime >= 0f && _firstCheckTime >= _smokeStartTime)
                smokeReaction = _firstCheckTime - _smokeStartTime;

            float smokeValue = 0f;
            string smokeContext;
            if (_smokeStartTime < 0f)
            {
                smokeContext = "Smoke never started (removed before cook time)";
            }
            else if (smokeReaction >= 0f)
            {
                smokeValue = smokeReaction;
                smokeContext = $"Reacted to smoke in {smokeReaction:F1}s";
            }
            else
            {
                smokeValue = now - _smokeStartTime;
                smokeContext = "No reaction to smoke";
            }

            records.Add(new MetricRecord(
                MetricType.ATTEN, 5,
                smokeValue,
                _smokeStartTime >= 0f ? _smokeStartTime : now,
                smokeContext));

            return records;
        }
    }
}
