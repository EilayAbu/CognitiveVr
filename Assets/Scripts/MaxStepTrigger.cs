using Oculus.Interaction.Locomotion;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Place on a trigger collider. While the assigned <see cref="targetController"/>
    /// is inside this trigger, its Max Step is overridden to <see cref="newMaxStep"/>.
    /// On exit, the controller's original Max Step is restored.
    ///
    /// Assign the target object (the one carrying the CharacterController script) in
    /// the Inspector rather than relying on collision detection to find it, since you
    /// said you'll wire it up manually.
    ///
    /// Optional: assign <see cref="heldGate"/> (e.g. a ChairGrabState on the chair)
    /// to suppress the override while that object reports itself as held. This is
    /// re-checked every frame the player is inside the zone, so releasing the chair
    /// while standing inside the trigger applies the override immediately, and
    /// re-grabbing it restores the original Max Step immediately.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MaxStepTrigger : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The object whose CharacterController's Max Step will be changed. " +
                 "Assign the PlayerController object here.")]
        [SerializeField] private Oculus.Interaction.Locomotion.CharacterController targetController;

        [Header("Override Value")]
        [Tooltip("Max Step applied while the target is inside this trigger.")]
        [SerializeField] private float newMaxStep = 0.3f;

        [Header("Filter (optional)")]
        [Tooltip("If set, only colliders with this tag will trigger the override. " +
                 "Leave as \"Untagged\" to react to any collider entering (still only " +
                 "applies the override to targetController, not the collider itself).")]
        [SerializeField] private string requiredTag = "Player";

        [Header("Held Gate (optional)")]
        [Tooltip("If assigned, the override is suppressed while this reports IsHeld = true " +
                 "(e.g. the player is holding the chair). Checked every frame the player is " +
                 "inside the zone, so grabbing/releasing while inside reacts immediately.")]
        [SerializeField] private CognitiveVR.Tasks.ChairGrabState heldGate;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        private float _originalMaxStep;
        private bool _isOverridden;
        private bool _playerInZone;

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnValidate()
        {
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                Debug.LogWarning($"[{nameof(MaxStepTrigger)}] Collider on {name} is not set to " +
                                  "'Is Trigger'. This component relies on trigger events.", this);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (targetController == null)
            {
                Debug.LogWarning($"[{nameof(MaxStepTrigger)}] No target controller assigned on {name}.", this);
                return;
            }

            if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            {
                return;
            }

            _playerInZone = true;
            TryApply();
        }

        private void OnTriggerExit(Collider other)
        {
            if (targetController == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            {
                return;
            }

            _playerInZone = false;
            TryRestore();
        }

        private void Update()
        {
            // Re-evaluate every frame while the player is in the zone so that
            // grabbing/releasing the chair (which doesn't fire trigger events)
            // still applies or restores Max Step immediately.
            if (!_playerInZone || targetController == null)
            {
                return;
            }

            bool isHeld = heldGate != null && heldGate.IsHeld;

            if (isHeld)
            {
                TryRestore();
            }
            else
            {
                TryApply();
            }
        }

        private void TryApply()
        {
            if (_isOverridden || targetController == null)
            {
                return;
            }

            if (heldGate != null && heldGate.IsHeld)
            {
                // Chair is in hand right now; don't apply while held.
                return;
            }

            _originalMaxStep = targetController.MaxStep;
            targetController.MaxStep = newMaxStep;
            _isOverridden = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(MaxStepTrigger)}] Max Step overridden: " +
                          $"{_originalMaxStep:F3} -> {newMaxStep:F3}", this);
            }
        }

        private void TryRestore()
        {
            if (!_isOverridden || targetController == null)
            {
                return;
            }

            targetController.MaxStep = _originalMaxStep;
            _isOverridden = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(MaxStepTrigger)}] Max Step restored to {_originalMaxStep:F3}.", this);
            }
        }

        private void OnDisable()
        {
            // Never leave the controller stuck with the overridden value if this
            // trigger is disabled/destroyed while the target is still inside.
            TryRestore();
            _playerInZone = false;
        }
    }
}