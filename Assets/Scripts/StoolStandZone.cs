using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Companion to <see cref="MaxStepTrigger"/> for hand-tracking users.
    ///
    /// MaxStep only gates joystick/locomotion movement. A hand-tracking player
    /// has no stick, so they move by physically walking (room-scale). The ISDK
    /// character capsule follows the head and re-grounds onto whatever collider
    /// is beneath it — that path ignores MaxStep, so the stool becomes climbable
    /// everywhere.
    ///
    /// This makes the stool a valid standing surface ONLY while the player is
    /// inside this trigger, by disabling / re-layering its collider(s). Outside
    /// the zone the stool is removed from ground detection, so physical walking
    /// keeps the player on the real floor.
    ///
    /// Put this on the SAME zone GameObject as your MaxStepTrigger (both share
    /// one trigger collider): MaxStep handles controller locomotion, this handles
    /// physical / hand-tracking movement. Does not modify either existing script.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StoolStandZone : MonoBehaviour
    {
        /// <summary>Player entered the stand zone (walked up to the stool spot).</summary>
        public event System.Action PlayerEnteredZone;
        /// <summary>Player left the stand zone.</summary>
        public event System.Action PlayerExitedZone;
        /// <summary>
        /// The stool became a valid standing surface (true) or stopped being one
        /// (false). True means the player is in the zone and not holding the stool
        /// - i.e. positioned to climb it.
        /// </summary>
        public event System.Action<bool> StandableChanged;

        public enum GateMode
        {
            /// <summary>Simple. Stool is non-solid outside the zone (hands pass through it).</summary>
            ToggleColliders,
            /// <summary>Stool stays solid; it's just excluded from ground detection via its layer.</summary>
            SwapLayer
        }

        [Header("Target Stool")]
        [Tooltip("The collider(s) that make the stool standable — usually the seat/top collider.")]
        [SerializeField] private List<Collider> standColliders = new List<Collider>();

        [Header("Gate Mode")]
        [SerializeField] private GateMode mode = GateMode.ToggleColliders;
        [Tooltip("SwapLayer only: layer used while standable. Must be INCLUDED in the CharacterController's ground mask.")]
        [SerializeField] private string inZoneLayer = "Default";
        [Tooltip("SwapLayer only: layer used while NOT standable. Must be EXCLUDED from the CharacterController's ground mask.")]
        [SerializeField] private string outOfZoneLayer = "Ignore Raycast";

        [Header("Filter (optional)")]
        [Tooltip("If set, only colliders with this tag toggle the zone. Match your MaxStepTrigger's tag.")]
        [SerializeField] private string requiredTag = "Player";

        [Header("Held Gate (optional)")]
        [Tooltip("If assigned, standing is suppressed while this reports IsHeld = true " +
                 "(e.g. the player is holding the stool). Re-checked every frame while in the zone.")]
        [SerializeField] private CognitiveVR.Tasks.ChairGrabState heldGate;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        private bool _playerInZone;
        private bool _standable;
        private readonly Dictionary<Collider, bool> _originalEnabled = new Dictionary<Collider, bool>();
        private readonly Dictionary<Collider, int> _originalLayer = new Dictionary<Collider, int>();

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnValidate()
        {
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                Debug.LogWarning($"[{nameof(StoolStandZone)}] Collider on {name} is not 'Is Trigger'. " +
                                 "This component relies on trigger events.", this);
            }
        }

        private void Awake()
        {
            if (standColliders == null || standColliders.Count == 0)
            {
                Debug.LogWarning($"[{nameof(StoolStandZone)}] No stand colliders assigned on {name}. " +
                                 "Nothing will be gated.", this);
            }

            CacheOriginals();
            SetStandable(false, force: true); // out-of-zone by default
        }

        private void CacheOriginals()
        {
            foreach (Collider c in standColliders)
            {
                if (c == null) continue;
                _originalEnabled[c] = c.enabled;
                _originalLayer[c] = c.gameObject.layer;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!PassesFilter(other)) return;
            bool wasInZone = _playerInZone;
            _playerInZone = true;
            if (!wasInZone) PlayerEnteredZone?.Invoke();
            Evaluate();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!PassesFilter(other)) return;
            bool wasInZone = _playerInZone;
            _playerInZone = false;
            if (wasInZone) PlayerExitedZone?.Invoke();
            Evaluate();
        }

        private void Update()
        {
            // Re-check the held state every frame while in the zone, so grabbing /
            // releasing the stool reacts immediately (matches MaxStepTrigger).
            if (_playerInZone) Evaluate();
        }

        private void Evaluate()
        {
            bool held = heldGate != null && heldGate.IsHeld;
            SetStandable(_playerInZone && !held);
        }

        private bool PassesFilter(Collider other)
        {
            return string.IsNullOrEmpty(requiredTag) || other.CompareTag(requiredTag);
        }

        private void SetStandable(bool standable, bool force = false)
        {
            if (standable == _standable && !force) return;
            bool changed = standable != _standable;
            _standable = standable;
            if (changed) StandableChanged?.Invoke(standable);

            foreach (Collider c in standColliders)
            {
                if (c == null) continue;

                if (mode == GateMode.ToggleColliders)
                {
                    c.enabled = standable;
                }
                else // SwapLayer
                {
                    int layer = LayerMask.NameToLayer(standable ? inZoneLayer : outOfZoneLayer);
                    if (layer >= 0) c.gameObject.layer = layer;
                    else Debug.LogWarning($"[{nameof(StoolStandZone)}] Layer not found; check inZoneLayer/outOfZoneLayer.", this);
                }
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(StoolStandZone)}] Standable = {standable} (inZone={_playerInZone}).", this);
            }
        }

        private void RestoreOriginals()
        {
            foreach (Collider c in standColliders)
            {
                if (c == null) continue;
                if (_originalEnabled.TryGetValue(c, out bool e)) c.enabled = e;
                if (_originalLayer.TryGetValue(c, out int l)) c.gameObject.layer = l;
            }
        }

        private void OnDisable()
        {
            // Don't leave the stool in a gated state if this component is torn down.
            RestoreOriginals();
            _playerInZone = false;
            _standable = false;
        }
    }
}
