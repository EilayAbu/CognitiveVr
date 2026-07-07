using UnityEngine;
using CharacterControllerLocomotion = Oculus.Interaction.Locomotion.CharacterController;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Put this on a zone object that has a Collider with Is Trigger checked.
    /// While the player's CharacterController is inside the zone, its Max Step is
    /// raised to <see cref="maxStepInZone"/> (so it can climb onto the stool). On exit,
    /// the original Max Step is restored.
    ///
    /// The player capsule usually has NO Rigidbody, so trigger events won't fire on
    /// their own. Add a Rigidbody to THIS zone object with Is Kinematic checked to make
    /// the trigger work against the moving capsule collider.
    /// </summary>
    public class MaxStepZone : MonoBehaviour
    {
        [Tooltip("Max Step value to apply while the player is inside the zone.")]
        [SerializeField] private float maxStepInZone = 0.84f;

        [Tooltip("Optional. The specific player CharacterController to affect. " +
                 "Leave empty to affect whichever CharacterController enters.")]
        [SerializeField] private CharacterControllerLocomotion characterController;

        [SerializeField] private bool enableDebugLogs = true;

        private CharacterControllerLocomotion _current;
        private float _originalMaxStep;

        private void OnTriggerEnter(Collider other)
        {
            if (_current != null)
                return; // already handling someone inside

            var entered = other.GetComponentInParent<CharacterControllerLocomotion>();
            if (entered == null)
                return; // not the player
            if (characterController != null && entered != characterController)
                return; // not the specific player we assigned

            _current = entered;
            _originalMaxStep = _current.MaxStep;
            _current.MaxStep = maxStepInZone;

            if (enableDebugLogs)
                Debug.Log($"[MaxStepZone] Raised Max Step {_originalMaxStep} -> {maxStepInZone}.", this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_current == null)
                return;

            var exited = other.GetComponentInParent<CharacterControllerLocomotion>();
            if (exited != _current)
                return;

            _current.MaxStep = _originalMaxStep;

            if (enableDebugLogs)
                Debug.Log($"[MaxStepZone] Restored Max Step to {_originalMaxStep}.", this);

            _current = null;
        }

        // Safety: if the zone is disabled while the player is still inside,
        // don't leave Max Step stuck at the raised value.
        private void OnDisable()
        {
            if (_current != null)
            {
                _current.MaxStep = _originalMaxStep;
                _current = null;
            }
        }
    }
}
