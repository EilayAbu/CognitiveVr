using UnityEngine;
using UnityEngine.Events;
using CognitiveVR.Core;
using CognitiveVR.Data;
using CognitiveVR.Interaction;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// End-of-session trigger. Put this on a trigger collider spanning the room
    /// exit (doorway). When the backpack passes through it carrying at least
    /// MinimumItems, the session is finalized: gaze totals are flushed, a
    /// session_end row is written and the JSON summary is dumped.
    ///
    /// Physics requirement: the trigger needs a collider with Is Trigger on, and
    /// the backpack needs a Rigidbody (it will have one if it's grabbable).
    ///
    /// Failed attempts - walking out with too few items - are logged too, as
    /// task/exit_denied rows. Those are useful data: they tell you the
    /// participant thought they were finished when they weren't.
    /// </summary>
    public class SessionEndTrigger : MonoBehaviour
    {
        [Header("What has to pass through")]
        [Tooltip("The backpack. Its collider (or any child collider) entering this zone is what counts as leaving with the bag.")]
        [SerializeField] private Transform backpackObject;
        [Tooltip("The inventory zone whose contents are counted. Auto-found if left empty.")]
        [SerializeField] private BackpackInventoryZone backpack;

        [Header("Conditions")]
        [Tooltip("How many items must be in the backpack for the exit to count.")]
        [SerializeField] private int minimumItems = 5;
        [Tooltip("Also require the player's head to be inside the zone, so the bag can't end it by being thrown through the door.")]
        [SerializeField] private bool requirePlayerPresent = true;
        [Tooltip("Head / center eye. Auto-found from GazeObjectTracker if left empty.")]
        [SerializeField] private Transform playerHead;
        [Tooltip("How close the head must be to the zone center to count as present (meters).")]
        [SerializeField] private float playerRadius = 2f;

        [Header("Behaviour")]
        [Tooltip("Ignore repeat triggers for this long, so one walk-through fires once.")]
        [SerializeField] private float cooldown = 1f;
        [Tooltip("Fire only once per session. Turn off if you reset and re-run in the same scene.")]
        [SerializeField] private bool oneShot = true;

        [Header("Events")]
        [Tooltip("Fired when the exit succeeds and logging is finalized. Hook your popup message here.")]
        public UnityEvent onSessionComplete;
        [Tooltip("Fired when the player exits without enough items. Hook a 'you forgot something' popup here.")]
        public UnityEvent onExitDenied;

        [Header("Runtime (Read Only)")]
        [SerializeField] private bool _hasFired;
        [SerializeField] private int _deniedAttempts;

        private float _lastTriggerTime = -999f;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        /// <summary>How many items are currently in the backpack.</summary>
        public int CurrentItemCount
        {
            get
            {
                if (backpack == null) return 0;

                int count = 0;
                foreach (string _ in backpack.StoredItemNames) count++;
                return count;
            }
        }

        private void Awake()
        {
            if (backpack == null)
                backpack = FindFirstObjectByType<BackpackInventoryZone>();

            if (playerHead == null && GazeObjectTracker.Instance != null)
                playerHead = GazeObjectTracker.Instance.HeadTransform;

            if (playerHead == null && Camera.main != null)
                playerHead = Camera.main.transform;

            Collider col = GetComponent<Collider>();
            if (col == null || !col.isTrigger)
                Debug.LogWarning($"[{nameof(SessionEndTrigger)}] {name}: collider missing or not set to Is Trigger.", this);

            if (backpackObject == null)
                Debug.LogWarning($"[{nameof(SessionEndTrigger)}] {name}: no backpack object assigned - nothing will trigger the exit.", this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasFired && oneShot) return;
            if (Time.time - _lastTriggerTime < cooldown) return;
            if (!IsBackpack(other)) return;

            _lastTriggerTime = Time.time;

            if (requirePlayerPresent && !IsPlayerNearby())
                return;

            int count = CurrentItemCount;

            if (count < minimumItems)
            {
                _deniedAttempts++;
                Manager?.Log("task", "exit_denied", "ExitZone", count,
                    $"items={count}|required={minimumItems}|attempt={_deniedAttempts}");
                onExitDenied?.Invoke();
                return;
            }

            _hasFired = true;

            Manager?.Log("task", "exit_complete", "ExitZone", count,
                $"items={count}|required={minimumItems}|denied_attempts={_deniedAttempts}");

            Manager?.FinalizeSession("exit_with_backpack");

            onSessionComplete?.Invoke();
        }

        private bool IsBackpack(Collider other)
        {
            if (backpackObject == null) return false;

            // Matches the bag itself, any child collider, and the rigidbody root.
            if (other.transform == backpackObject || other.transform.IsChildOf(backpackObject))
                return true;

            Rigidbody rb = other.attachedRigidbody;
            return rb != null && (rb.transform == backpackObject || rb.transform.IsChildOf(backpackObject));
        }

        private bool IsPlayerNearby()
        {
            if (playerHead == null) return true;
            return Vector3.Distance(playerHead.position, transform.position) <= playerRadius;
        }

        /// <summary>Manual finalize, e.g. from an experimenter button.</summary>
        public void ForceComplete()
        {
            if (_hasFired && oneShot) return;

            _hasFired = true;
            Manager?.Log("task", "exit_complete", "ExitZone", CurrentItemCount, "reason=manual");
            Manager?.FinalizeSession("manual");
            onSessionComplete?.Invoke();
        }

        /// <summary>Allows the trigger to fire again after a ResetPuddle-style restart.</summary>
        public void ResetTrigger()
        {
            _hasFired = false;
            _deniedAttempts = 0;
            _lastTriggerTime = -999f;
        }

        private void OnDrawGizmosSelected()
        {
            if (!requirePlayerPresent) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, playerRadius);
        }
    }
}