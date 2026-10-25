using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the chair root alongside <see cref="ChairZonePlacement"/>. Tracks
    /// whether the chair is currently being held.
    ///
    /// When a <see cref="Grabbable"/> is present, <see cref="IsHeld"/> reads its
    /// live grab-point count, so a two-handed hold stays held until the last hand
    /// lets go - even when each hand grabs through a different interactable.
    ///
    /// Wire the chair's InteractableUnityEventWrapper events to this:
    ///  - "When Select"   -> OnChairGrabbed
    ///  - "When Unselect" -> OnChairUngrabbed  (call this BEFORE or alongside
    ///                        ChairZonePlacement.OnChairReleased; order between the
    ///                        two doesn't matter since MaxStepTrigger only reads
    ///                        IsHeld on its own trigger enter/exit)
    /// These events only drive <see cref="IsHeld"/> when there is no Grabbable.
    ///
    /// <see cref="MaxStepTrigger"/> reads <see cref="IsHeld"/> so it can skip
    /// applying its override while the chair is in-hand, even if the player's body
    /// is standing inside the trigger zone.
    /// </summary>
    public class ChairGrabState : MonoBehaviour
    {
        [SerializeField] private bool enableDebugLogs;

        [Tooltip("Auto-filled from this GameObject when empty.")]
        [SerializeField] private Grabbable grabbable;

        private bool _heldFlag;

        /// <summary>True while any hand is currently grabbing the chair.</summary>
        public bool IsHeld => grabbable != null ? grabbable.SelectingPointsCount > 0 : _heldFlag;

        /// <summary>Number of hands currently grabbing the chair (0 when no Grabbable is found).</summary>
        public int HandsHolding => grabbable != null ? grabbable.SelectingPointsCount : 0;

        private void Awake()
        {
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }
        }

        /// <summary>Wire to the chair's InteractableUnityEventWrapper "When Select" event.</summary>
        public void OnChairGrabbed()
        {
            _heldFlag = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[ChairGrabState] Chair grabbed. hands={HandsHolding} held={IsHeld}", this);
            }
        }

        /// <summary>Wire to the chair's InteractableUnityEventWrapper "When Unselect" event.</summary>
        public void OnChairUngrabbed()
        {
            _heldFlag = false;

            if (enableDebugLogs)
            {
                Debug.Log($"[ChairGrabState] Chair released by one interactable. hands={HandsHolding} held={IsHeld}", this);
            }
        }
    }
}
