using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Bootstraps a RigPocket whose permanent item isn't known at scene load.
    /// Put this on the SAME GameObject as your RigPocketZone (the one with the
    /// trigger collider). When the item first enters the trigger, this assigns it
    /// as the pocket's permanent item and runs the pocket's initialization --
    /// the caching / event-subscription / store step that normally only happens
    /// in RigPocket.Start().
    ///
    /// WHY THIS WORKS WITHOUT EDITING RigPocket:
    /// RigPocket does all its setup in Start(), and Start() runs once -- the first
    /// time the component becomes enabled. So leave the RigPocket component
    /// DISABLED in the Inspector; this script enables it on connect, which makes
    /// Start() run at THAT moment, with permanentItem already assigned.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class RigPocketTutorial : MonoBehaviour
    {
        [Tooltip("Pocket to initialize. Leave it DISABLED in the Inspector -- this script enables it on connect.")]
        [SerializeField] private RigPocket pocket;

        [Tooltip("The item (phone) that connects to the pocket when it enters this trigger.")]
        [SerializeField] private GameObject item;

        [Tooltip("Only connect + initialize the first time the item enters.")]
        [SerializeField] private bool onlyOnce = true;

        [SerializeField] private bool enableDebugLogs;

        private bool _done;

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            if (pocket == null)
            {
                pocket = GetComponent<RigPocket>();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_done && onlyOnce)
            {
                return;
            }

            if (pocket == null || item == null)
            {
                return;
            }

            // Only react to the assigned item (ignore hands / other objects).
            if (other.transform != item.transform && !other.transform.IsChildOf(item.transform))
            {
                return;
            }

            Grabbable grabbable = item.GetComponent<Grabbable>();
            if (grabbable == null)
            {
                Debug.LogError($"[{nameof(RigPocketTutorial)}] {item.name} has no Grabbable.", this);
                return;
            }

            // Assign the item, then run RigPocket.Start() by enabling the pocket now.
            pocket.permanentItem = grabbable;

            if (pocket.enabled)
            {
                // Start() already ran (and bailed on a null item). Enabling won't
                // re-run it, so the pocket can't initialize this way.
                Debug.LogWarning(
                    $"[{nameof(RigPocketTutorial)}] {pocket.name} was already enabled, so its Start() " +
                    $"already ran and won't run again. Disable the RigPocket component in the Inspector " +
                    $"so this script can trigger its initialization on connect.", this);
                return;
            }

            pocket.enabled = true; // -> RigPocket.Start(): caches refs, subscribes, StoreItem()

            _done = true;

            Destroy(gameObject);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocketTutorial)}] Connected {item.name} and initialized {pocket.name}.", this);
            }
        }
    }
}
