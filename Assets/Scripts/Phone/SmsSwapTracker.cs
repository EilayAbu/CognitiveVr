using System;
using System.Collections.Generic;
using CognitiveVR.Core;
using CognitiveVR.Interaction;
using CognitiveVR.Models;
using CognitiveVR.Tasks;
using UnityEngine;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Tracks the laptop -> tablet corrective action triggered by the
    /// "change-of-plan" SMS. Computes:
    ///   FLEX_1 - SMS reaction time (SMS appearance -> first corrective action)
    ///   FLEX_2 - Laptop removed from bag (binary)
    ///   FLEX_3 - Tablet placed in bag (binary)
    ///   FLEX_4 - Total swap duration (SMS -> both items in correct state)
    /// Items are identified by their GameObject name (case-insensitive match
    /// against the configured name lists). Storage state comes directly from
    /// the <see cref="BackpackInventoryZone"/> item entered/exited events.
    /// Routes lifecycle through TaskApi for the SmsSwap task and exposes
    /// BuildRecords() for assessment harvesting (same pattern as ToasterMetrics).
    /// </summary>
    [DisallowMultipleComponent]
    public class SmsSwapTracker : MonoBehaviour
    {
        [Header("Bindings (auto-resolved)")]
        [SerializeField] private SessionTimer _sessionTimer;
        [SerializeField] private BackpackInventoryZone _backpack;

        [Header("Item Name Matching")]
        [Tooltip("An item whose GameObject name contains one of these (case-insensitive) counts as the laptop.")]
        [SerializeField] private List<string> _laptopNames = new List<string> { "Laptop" };
        [Tooltip("An item whose GameObject name contains one of these (case-insensitive) counts as the tablet.")]
        [SerializeField] private List<string> _tabletNames = new List<string> { "Tablet" };

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = true;

        [Header("Runtime State (read only)")]
        [SerializeField] private bool _smsAppeared;
        [SerializeField] private float _smsAppearedAt = -1f;
        [SerializeField] private float _firstActionAt = -1f;
        [SerializeField] private bool _laptopRemoved;
        [SerializeField] private float _laptopRemovedAt = -1f;
        [SerializeField] private bool _tabletPlaced;
        [SerializeField] private float _tabletPlacedAt = -1f;
        [SerializeField] private bool _swapCompletedReported;

        public bool LaptopRemoved => _laptopRemoved;
        public bool TabletPlaced => _tabletPlaced;
        public bool SwapCompleted => _laptopRemoved && _tabletPlaced;

        private bool _laptopEverStored;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (_backpack != null)
            {
                _backpack.WhenItemEntered += HandleItemEnteredBackpack;
                _backpack.WhenItemExited += HandleItemExitedBackpack;
                CaptureInitialInventoryState();
            }
            else
            {
                Debug.LogWarning($"[{nameof(SmsSwapTracker)}] No {nameof(BackpackInventoryZone)} found; swap tracking is inactive.", this);
            }
        }

        private void OnDisable()
        {
            if (_backpack != null)
            {
                _backpack.WhenItemEntered -= HandleItemEnteredBackpack;
                _backpack.WhenItemExited -= HandleItemExitedBackpack;
            }
        }

        public void OnSmsAppeared(float sessionTime)
        {
            if (_smsAppeared) return;

            _smsAppeared = true;
            _smsAppearedAt = sessionTime;

            try
            {
                TaskApi.ReportStarted(TaskType.SmsSwap, "SMS plan-change received");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(SmsSwapTracker)}] Failed to report SmsSwap start: {ex.Message}", this);
            }

            if (_verboseLogs)
                Debug.Log($"[{nameof(SmsSwapTracker)}] SMS reaction window opened at {sessionTime:F1}s.", this);
        }

        public List<MetricRecord> BuildRecords()
        {
            var records = new List<MetricRecord>(4);

            float reactionValue = (_smsAppearedAt >= 0f && _firstActionAt >= 0f)
                ? Mathf.Max(0f, _firstActionAt - _smsAppearedAt)
                : -1f;
            string reactionContext = reactionValue >= 0f
                ? $"First corrective action {reactionValue:F1}s after SMS"
                : "No corrective action";
            records.Add(new MetricRecord(
                MetricType.FLEX, 1,
                reactionValue >= 0f ? reactionValue : 0f,
                _smsAppearedAt >= 0f ? _smsAppearedAt : 0f,
                reactionContext));

            records.Add(new MetricRecord(
                MetricType.FLEX, 2,
                _laptopRemoved ? 1f : 0f,
                _laptopRemovedAt >= 0f ? _laptopRemovedAt : (_smsAppearedAt >= 0f ? _smsAppearedAt : 0f),
                _laptopRemoved ? "Laptop removed from bag" : "Laptop not removed"));

            records.Add(new MetricRecord(
                MetricType.FLEX, 3,
                _tabletPlaced ? 1f : 0f,
                _tabletPlacedAt >= 0f ? _tabletPlacedAt : (_smsAppearedAt >= 0f ? _smsAppearedAt : 0f),
                _tabletPlaced ? "Tablet placed in bag" : "Tablet not placed"));

            float swapValue = -1f;
            if (_smsAppearedAt >= 0f && SwapCompleted)
            {
                float lastAction = Mathf.Max(_laptopRemovedAt, _tabletPlacedAt);
                swapValue = Mathf.Max(0f, lastAction - _smsAppearedAt);
            }
            string swapContext = swapValue >= 0f
                ? $"Swap completed in {swapValue:F1}s"
                : "Swap not completed";
            records.Add(new MetricRecord(
                MetricType.FLEX, 4,
                swapValue >= 0f ? swapValue : 0f,
                _smsAppearedAt >= 0f ? _smsAppearedAt : 0f,
                swapContext));

            return records;
        }

        #region Internal

        private float SessionTimeNow()
        {
            if (_sessionTimer != null && _sessionTimer.IsRunning)
                return _sessionTimer.ElapsedTime;
            return Time.time;
        }

        private static bool NameMatches(string itemName, List<string> candidates)
        {
            if (string.IsNullOrEmpty(itemName) || candidates == null) return false;

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                if (itemName.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private void HandleItemEnteredBackpack(string itemName, BackpackSlot slot)
        {
            if (NameMatches(itemName, _tabletNames) && !_tabletPlaced)
            {
                float now = SessionTimeNow();
                _tabletPlaced = true;
                _tabletPlacedAt = now;
                RegisterFirstAction(now);
                try
                {
                    TaskApi.ReportStepCompleted(TaskType.SmsSwap, "place_tablet", "Tablet placed in bag");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[{nameof(SmsSwapTracker)}] Failed to report place_tablet: {ex.Message}", this);
                }

                if (_verboseLogs)
                    Debug.Log($"[{nameof(SmsSwapTracker)}] Tablet '{itemName}' placed at {now:F1}s.", this);

                TryReportCompletion();
            }
            else if (NameMatches(itemName, _laptopNames))
            {
                _laptopEverStored = true;
            }
        }

        private void HandleItemExitedBackpack(string itemName, BackpackSlot slot)
        {
            if (!NameMatches(itemName, _laptopNames)) return;
            if (!_laptopEverStored || _laptopRemoved) return;

            float now = SessionTimeNow();
            _laptopRemoved = true;
            _laptopRemovedAt = now;
            RegisterFirstAction(now);
            try
            {
                TaskApi.ReportStepCompleted(TaskType.SmsSwap, "remove_laptop", "Laptop removed from bag");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(SmsSwapTracker)}] Failed to report remove_laptop: {ex.Message}", this);
            }

            if (_verboseLogs)
                Debug.Log($"[{nameof(SmsSwapTracker)}] Laptop '{itemName}' removed at {now:F1}s.", this);

            TryReportCompletion();
        }

        private void RegisterFirstAction(float t)
        {
            if (_firstActionAt < 0f && _smsAppeared)
                _firstActionAt = t;
        }

        private void TryReportCompletion()
        {
            if (_swapCompletedReported) return;
            if (!SwapCompleted) return;
            _swapCompletedReported = true;

            try
            {
                TaskApi.ReportCompleted(TaskType.SmsSwap, "Laptop swapped for tablet");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(SmsSwapTracker)}] Failed to report SmsSwap complete: {ex.Message}", this);
            }

            if (_verboseLogs)
                Debug.Log($"[{nameof(SmsSwapTracker)}] SmsSwap completed.", this);
        }

        private void CaptureInitialInventoryState()
        {
            foreach (string storedName in _backpack.StoredItemNames)
            {
                if (NameMatches(storedName, _laptopNames))
                    _laptopEverStored = true;
            }
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

            if (_backpack == null)
            {
#if UNITY_2023_1_OR_NEWER
                _backpack = FindFirstObjectByType<BackpackInventoryZone>();
#else
                _backpack = FindObjectOfType<BackpackInventoryZone>();
#endif
            }
        }

        #endregion
    }
}
