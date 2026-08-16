using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;
using CognitiveVR.Core;
using CognitiveVR.Interaction;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Waits for a specific set of items to be collected into the backpack and
    /// fires a UnityEvent once the whole list is done. Wire
    /// <see cref="OnItemCollected"/> into
    /// BackpackInventoryZone > Data Collection Events > On Item Entered, and wire
    /// ScheduledEventRescheduler.Apply() into
    /// <see cref="onAllItemsCollected"/>.
    ///
    /// Progress is cumulative: once an item has been collected it stays checked
    /// even if the participant later takes it back out of the bag.
    /// </summary>
    [DisallowMultipleComponent]
    public class BackpackItemChecklist : MonoBehaviour
    {
        [Header("Required Items")]
        [Tooltip("Item (GameObject) names to wait for. Matching ignores case, surrounding spaces and a trailing '(Clone)' / ' 1' suffix.")]
        [SerializeField] private List<string> requiredItems = new List<string>();

        [Header("Bindings (optional)")]
        [Tooltip("If assigned, the checklist also subscribes directly to the zone, so it works even without the Inspector event wiring.")]
        [SerializeField] private BackpackInventoryZone backpack;
        [Tooltip("If assigned, Apply() is called directly in addition to the On All Items Collected event.")]
        [SerializeField] private ScheduledEventRescheduler rescheduler;

        [Header("Behaviour")]
        [Tooltip("Fire only once per session. Turn off if you reset and re-run in the same scene.")]
        [SerializeField] private bool oneShot = true;
        [SerializeField] private bool enableDebugLogs = true;

        [Header("Events")]
        [Tooltip("Fired when every required item has been collected. Hook ScheduledEventRescheduler.Apply() here.")]
        [SerializeField] private UnityEvent onAllItemsCollected = new UnityEvent();
        [Tooltip("Fired on every new required item with (collected, total). Useful for a counter UI.")]
        [SerializeField] private UnityEvent<int, int> onProgressChanged = new UnityEvent<int, int>();

        private static readonly Regex DuplicateSuffix = new Regex(@"\s*(\(clone\)|\(\d+\)|\s\d+)$", RegexOptions.IgnoreCase);

        private readonly HashSet<string> _requiredKeys = new HashSet<string>();
        private readonly HashSet<string> _collected = new HashSet<string>();
        private bool _completed;

        /// <summary>Normalized names of the required items already collected.</summary>
        public IReadOnlyCollection<string> CollectedItems => _collected;

        /// <summary>How many distinct items the list asks for.</summary>
        public int RequiredCount => _requiredKeys.Count;

        /// <summary>How many of them have been collected so far.</summary>
        public int CollectedCount => _collected.Count;

        public bool IsComplete => _completed;

        private void Awake()
        {
            BuildRequiredKeys();
        }

        private void OnEnable()
        {
            if (backpack != null)
            {
                backpack.WhenItemEntered += HandleItemEntered;
            }
        }

        private void OnDisable()
        {
            if (backpack != null)
            {
                backpack.WhenItemEntered -= HandleItemEntered;
            }
        }

        /// <summary>Wire this into BackpackInventoryZone.onItemEntered (dynamic string).</summary>
        public void OnItemCollected(string itemName)
        {
            if (_completed && oneShot)
            {
                return;
            }

            if (_requiredKeys.Count == 0)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning($"[{nameof(BackpackItemChecklist)}] No required items configured on {name}; nothing to complete.", this);
                }
                return;
            }

            string key = Normalize(itemName);
            if (!_requiredKeys.Contains(key))
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(BackpackItemChecklist)}] '{itemName}' is not on the required list. Ignored.", this);
                }
                return;
            }

            // The same item can arrive twice (Inspector wiring plus the direct
            // subscription, or a re-store after removal); only the first counts.
            if (!_collected.Add(key))
            {
                return;
            }

            onProgressChanged.Invoke(_collected.Count, RequiredCount);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackItemChecklist)}] '{itemName}' collected ({_collected.Count}/{RequiredCount}).", this);
            }

            if (_collected.Count < RequiredCount)
            {
                return;
            }

            _completed = true;
            FireEvent();
        }

        /// <summary>Invokes the completion event and the optional direct rescheduler call.</summary>
        public void FireEvent()
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackItemChecklist)}] All {RequiredCount} required items collected -> firing event.", this);
            }

            onAllItemsCollected.Invoke();

            if (rescheduler != null)
            {
                rescheduler.Apply();
            }
        }

        /// <summary>Clears progress so the checklist can be completed again.</summary>
        public void ResetChecklist()
        {
            _collected.Clear();
            _completed = false;
            onProgressChanged.Invoke(0, RequiredCount);
        }

        private void HandleItemEntered(string itemName, BackpackSlot slot)
        {
            OnItemCollected(itemName);
        }

        private void BuildRequiredKeys()
        {
            _requiredKeys.Clear();

            if (requiredItems == null)
            {
                return;
            }

            foreach (string item in requiredItems)
            {
                string key = Normalize(item);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                // Two entries can collapse into one key ("Box 1" / "Box 2"),
                // which would quietly lower the required count.
                if (!_requiredKeys.Add(key) && enableDebugLogs)
                {
                    Debug.LogWarning($"[{nameof(BackpackItemChecklist)}] '{item}' matches an earlier required item ('{key}') and is counted only once.", this);
                }
            }

            if (_requiredKeys.Count == 0 && enableDebugLogs)
            {
                Debug.LogWarning($"[{nameof(BackpackItemChecklist)}] {name} has no required items configured.", this);
            }
        }

        private static string Normalize(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
            {
                return string.Empty;
            }

            string trimmed = itemName.Trim().ToLowerInvariant();
            return DuplicateSuffix.Replace(trimmed, string.Empty).Trim();
        }

#if UNITY_EDITOR
        [ContextMenu("Force Complete")]
        private void EditorForceComplete()
        {
            _completed = true;
            FireEvent();
        }

        [ContextMenu("Reset Checklist")]
        private void EditorResetChecklist()
        {
            ResetChecklist();
        }
#endif
    }
}
