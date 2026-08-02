using UnityEngine;
using CognitiveVR.Interaction;

namespace CognitiveVR.Tutorial
{
    /// <summary>
    /// Non-invasive bridge: forwards a "backpack got an item" event to the
    /// tutorial as a CompleteLevel() signal, but ONLY while the tutorial is
    /// sitting on the target level (default: level 7 -> index 6). Nothing on
    /// the backpack or the tutorial is modified -- this just listens to
    /// <see cref="BackpackInventoryZone.WhenItemEntered"/> and calls the
    /// tutorial's public <see cref="TutorialFlow.CompleteLevel"/>.
    ///
    /// Because it hands off to CompleteLevel(), the level's own
    /// "Signals Needed" decides how many items are required:
    ///   Signals Needed = 1  -> the first item finishes the level (what you want)
    ///   Signals Needed = 3  -> three separate items finish it
    /// Leave level 7's Touch Any / Zone goal fields empty; this is its signal.
    /// </summary>
    [DisallowMultipleComponent]
    public class BackpackTutorialBridge : MonoBehaviour
    {
        [Header("References (assign in Inspector)")]
        [SerializeField] private TutorialFlow tutorial;
        [SerializeField] private BackpackInventoryZone backpack;

        [Header("Which level")]
        [Tooltip("1-based tutorial level this bridge completes. Level 7 -> 7.")]
        [SerializeField] private int targetLevelNumber = 7;

        [SerializeField] private bool enableDebugLogs = true;

        private void OnEnable()
        {
            if (backpack != null)
            {
                backpack.WhenItemEntered += HandleItemEntered;
            }
            else if (enableDebugLogs)
            {
                Debug.LogWarning($"[{nameof(BackpackTutorialBridge)}] No BackpackInventoryZone assigned.", this);
            }
        }

        private void OnDisable()
        {
            if (backpack != null)
            {
                backpack.WhenItemEntered -= HandleItemEntered;
            }
        }

        private void HandleItemEntered(string itemName, BackpackSlot slot)
        {
            if (tutorial == null || !tutorial.IsRunning)
            {
                return;
            }

            // TutorialFlow.CurrentIndex is 0-based; level 7 -> index 6.
            if (tutorial.CurrentIndex != targetLevelNumber - 1)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(BackpackTutorialBridge)}] '{itemName}' stored, but tutorial is on level {tutorial.CurrentIndex + 1} (waiting for {targetLevelNumber}). Ignored.", this);
                }
                return;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackTutorialBridge)}] '{itemName}' stored -> signalling level {targetLevelNumber}.", this);
            }

            tutorial.CompleteLevel();
        }
    }
}
