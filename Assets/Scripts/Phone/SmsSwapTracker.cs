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
    /// Routes lifecycle through TaskApi for the SmsSwap task and exposes
    /// BuildRecords() for assessment harvesting (same pattern as ToasterMetrics).
    /// </summary>
    [DisallowMultipleComponent]
    public class SmsSwapTracker : MonoBehaviour
    {
        [Header("Bindings (auto-resolved)")]
        [SerializeField] private SessionTimer _sessionTimer;
        [SerializeField] private BackpackInventoryZone _backpack;

        [Header("Optional explicit item refs")]
        [Tooltip("If provided, only these specific items are watched. If empty, all InventoryItemMetaBridge components in the scene with a matching ItemId are watched.")]
        [SerializeField] private InventoryItemMetaBridge _laptop;
        [SerializeField] private InventoryItemMetaBridge _tablet;

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

        private readonly HashSet<InventoryItemMetaBridge> _watched = new HashSet<InventoryItemMetaBridge>();
        private readonly HashSet<InventoryItemMetaBridge> _everStoredLaptops = new HashSet<InventoryItemMetaBridge>();
        private readonly HashSet<InventoryItemMetaBridge> _everStoredTablets = new HashSet<InventoryItemMetaBridge>();

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindWatchedItems();
            CaptureInitialInventoryState();
        }

        private void OnDisable()
        {
            UnbindWatchedItems();
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

        private void HandleItemSelected(InventoryItemMetaBridge item)
        {
            if (item == null) return;

            switch (item.ItemId)
            {
                case ItemId.Laptop:
                    if (_everStoredLaptops.Contains(item) && !_laptopRemoved)
                    {
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
                            Debug.Log($"[{nameof(SmsSwapTracker)}] Laptop removed at {now:F1}s.", item);

                        TryReportCompletion();
                    }
                    break;

                case ItemId.Tablet:
                    // No-op on select for tablet; we record it on Released-into-bag.
                    break;
            }
        }

        private void HandleItemReleased(InventoryItemMetaBridge item)
        {
            if (item == null) return;

            // This event fires on release; the BackpackInventoryZone separately
            // decides if the item ended up inside the inventory volume. We use a
            // post-frame check via item.IsStoredInInventory.
            StartCoroutineCheckStored(item);
        }

        private void StartCoroutineCheckStored(InventoryItemMetaBridge item)
        {
            if (!isActiveAndEnabled) return;
            StartCoroutine(CheckStoredNextFrame(item));
        }

        private System.Collections.IEnumerator CheckStoredNextFrame(InventoryItemMetaBridge item)
        {
            yield return null;
            if (item == null) yield break;

            if (item.IsStoredInInventory)
            {
                if (item.ItemId == ItemId.Tablet && !_tabletPlaced)
                {
                    float now = SessionTimeNow();
                    _tabletPlaced = true;
                    _tabletPlacedAt = now;
                    _everStoredTablets.Add(item);
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
                        Debug.Log($"[{nameof(SmsSwapTracker)}] Tablet placed at {now:F1}s.", item);

                    TryReportCompletion();
                }
                else if (item.ItemId == ItemId.Laptop)
                {
                    _everStoredLaptops.Add(item);
                }
            }
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

        private void BindWatchedItems()
        {
            UnbindWatchedItems();

            if (_laptop != null) AddWatched(_laptop);
            if (_tablet != null) AddWatched(_tablet);

            if (_watched.Count == 0)
            {
#if UNITY_2023_1_OR_NEWER
                InventoryItemMetaBridge[] all = FindObjectsByType<InventoryItemMetaBridge>(FindObjectsSortMode.None);
#else
                InventoryItemMetaBridge[] all = FindObjectsOfType<InventoryItemMetaBridge>();
#endif
                foreach (InventoryItemMetaBridge item in all)
                {
                    if (item == null) continue;
                    if (item.ItemId == ItemId.Laptop || item.ItemId == ItemId.Tablet)
                        AddWatched(item);
                }
            }
        }

        private void AddWatched(InventoryItemMetaBridge item)
        {
            if (item == null || _watched.Contains(item)) return;
            _watched.Add(item);
            item.WhenItemSelected += HandleItemSelected;
            item.WhenItemReleased += HandleItemReleased;
        }

        private void UnbindWatchedItems()
        {
            foreach (InventoryItemMetaBridge item in _watched)
            {
                if (item == null) continue;
                item.WhenItemSelected -= HandleItemSelected;
                item.WhenItemReleased -= HandleItemReleased;
            }
            _watched.Clear();
        }

        private void CaptureInitialInventoryState()
        {
            foreach (InventoryItemMetaBridge item in _watched)
            {
                if (item == null) continue;
                if (!item.IsStoredInInventory) continue;

                if (item.ItemId == ItemId.Laptop)
                    _everStoredLaptops.Add(item);
                else if (item.ItemId == ItemId.Tablet)
                    _everStoredTablets.Add(item);
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
