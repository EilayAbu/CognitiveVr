using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the chair root alongside <see cref="ChairZonePlacement"/>. Tracks
    /// whether the chair is currently being held.
    ///
    /// Wire the chair's InteractableUnityEventWrapper events to this:
    ///  - "When Select"   -> OnChairGrabbed
    ///  - "When Unselect" -> OnChairUngrabbed  (call this BEFORE or alongside
    ///                        ChairZonePlacement.OnChairReleased; order between the
    ///                        two doesn't matter since MaxStepTrigger only reads
    ///                        IsHeld on its own trigger enter/exit)
    ///
    /// <see cref="MaxStepTrigger"/> reads <see cref="IsHeld"/> so it can skip
    /// applying its override while the chair is in-hand, even if the player's body
    /// is standing inside the trigger zone.
    /// </summary>
    public class ChairGrabState : MonoBehaviour
    {
        [SerializeField] private bool enableDebugLogs;

        /// <summary>True while any hand is currently grabbing the chair.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>Wire to the chair's InteractableUnityEventWrapper "When Select" event.</summary>
        public void OnChairGrabbed()
        {
            IsHeld = true;

            if (enableDebugLogs)
            {
                Debug.Log("[ChairGrabState] Chair grabbed.", this);
            }
        }

        /// <summary>Wire to the chair's InteractableUnityEventWrapper "When Unselect" event.</summary>
        public void OnChairUngrabbed()
        {
            IsHeld = false;

            if (enableDebugLogs)
            {
                Debug.Log("[ChairGrabState] Chair released.", this);
            }
        }
    }
}
