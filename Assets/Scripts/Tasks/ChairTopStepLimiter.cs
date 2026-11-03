using UnityEngine;
using CharacterControllerLocomotion = Oculus.Interaction.Locomotion.CharacterController;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Lowers the player's Max Step while they are standing ON the chair, so they
    /// can't step from the chair up onto a higher surface (e.g. the counter).
    /// When they step off the chair, the previous Max Step is restored.
    ///
    /// Works alongside MaxStepTrigger without modifying it: MaxStepTrigger raises
    /// Max Step so the player can get onto the chair; this script drops it back
    /// down once they're on top.
    ///
    /// Setup: put this on any GameObject (e.g. the zone), assign the player's
    /// CharacterController and the chair's root transform.
    /// </summary>
    [DefaultExecutionOrder(100)] // run after MaxStepTrigger
    public class ChairTopStepLimiter : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The player's Interaction SDK CharacterController (same one MaxStepTrigger uses).")]
        [SerializeField] private CharacterControllerLocomotion targetController;

        [Tooltip("Root of the chair. Any collider under it counts as 'the chair'.")]
        [SerializeField] private Transform chairRoot;

        [Header("Values")]
        [Tooltip("Max Step while standing on the chair. 0.05 = the scene's normal value.")]
        [SerializeField] private float maxStepOnChair = 0.05f;

        [Tooltip("How far down from the controller to look for the surface under the player.")]
        [SerializeField] private float probeDistance = 2.5f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        private bool _onChair;
        private float _savedMaxStep;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private void LateUpdate()
        {
            if (targetController == null || chairRoot == null) return;

            bool onChair = IsStandingOnChair();

            if (onChair && !_onChair)
            {
                // Just stepped onto the chair: remember the current value, then lower it.
                _savedMaxStep = targetController.MaxStep;
                targetController.MaxStep = maxStepOnChair;
                _onChair = true;
                Log($"On chair. Max Step {_savedMaxStep:F2} -> {maxStepOnChair:F2}");
            }
            else if (onChair && targetController.MaxStep > maxStepOnChair)
            {
                // Something raised it again while we're on the chair
                // (e.g. MaxStepTrigger re-applying after a grab/release). Keep it low.
                _savedMaxStep = targetController.MaxStep;
                targetController.MaxStep = maxStepOnChair;
                Log($"Re-lowered Max Step to {maxStepOnChair:F2} (was raised while on chair).");
            }
            else if (!onChair && _onChair)
            {
                // Stepped off the chair: restore, unless someone else already changed it.
                if (Mathf.Approximately(targetController.MaxStep, maxStepOnChair))
                    targetController.MaxStep = _savedMaxStep;
                _onChair = false;
                Log($"Off chair. Max Step restored to {targetController.MaxStep:F2}");
            }
        }

        /// <summary>
        /// Casts straight down from the controller and checks whether the first
        /// solid surface below (ignoring the player rig itself) belongs to the chair.
        /// </summary>
        private bool IsStandingOnChair()
        {
            Transform rigRoot = targetController.transform.root;
            Vector3 origin = targetController.transform.position + Vector3.up * 0.1f;

            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, probeDistance,
                ~0, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            Collider nearestCol = null;
            for (int i = 0; i < count; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null || c.transform.IsChildOf(rigRoot)) continue; // skip the player's own colliders
                if (_hits[i].distance < nearest)
                {
                    nearest = _hits[i].distance;
                    nearestCol = c;
                }
            }

            return nearestCol != null && nearestCol.transform.IsChildOf(chairRoot);
        }

        private void OnDisable()
        {
            // Never leave the player stuck at the lowered value.
            if (_onChair && targetController != null &&
                Mathf.Approximately(targetController.MaxStep, maxStepOnChair))
            {
                targetController.MaxStep = _savedMaxStep;
            }
            _onChair = false;
        }

        private void Log(string msg)
        {
            if (enableDebugLogs) Debug.Log($"[{nameof(ChairTopStepLimiter)}] {msg}", this);
        }
    }
}
