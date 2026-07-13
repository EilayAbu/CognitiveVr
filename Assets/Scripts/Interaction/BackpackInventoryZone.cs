using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Data-collection hub for the backpack. The zone no longer owns a trigger
    /// volume or storage logic — each <see cref="BackpackSlot"/> is independent
    /// and stores/releases items on its own. This component only:
    ///  - generates / manages the 3x3 slot layout,
    ///  - aggregates the per-slot stored/removed notifications into public
    ///    events (<see cref="WhenItemEntered"/> / <see cref="WhenItemExited"/>
    ///    and the Inspector-facing UnityEvents) for data collection,
    ///  - plays in/out audio feedback,
    ///  - suppresses the backpack body grabbables while a hand hovers a slot.
    /// </summary>
    public class BackpackInventoryZone : MonoBehaviour
    {
        [Header("Inventory Layout")]
        [Tooltip("Fallback list of exactly 9 slot transforms (3x3) used as snap points.")]
        [SerializeField] private List<Transform> slotTransforms = new List<Transform>(9);

        [Header("Auto Slot Generation")]
        [Tooltip("If true and no slots are configured, generate a 3x3 grid of child Transforms at runtime.")]
        [SerializeField] private bool autoGenerateSlots = true;
        [Tooltip("Local-space size (X = width, Y = depth) of the grid covered by the auto-generated slots.")]
        [SerializeField] private Vector2 autoSlotsLocalArea = new Vector2(0.7f, 0.7f);
        [Tooltip("Local-space Y position (relative to this transform) for auto-generated slot transforms.")]
        [SerializeField] private float autoSlotsLocalY = 0f;
        [SerializeField] private string autoSlotPrefix = "Slot_";
        [Tooltip("If true, attach a BackpackSlot component (with trigger BoxCollider) to each slot.")]
        [SerializeField] private bool autoConfigureSlotComponents = true;
        [Tooltip("BoxCollider size (local space) added on every slot for item/hand detection.")]
        [SerializeField] private Vector3 slotColliderSize = Vector3.one;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip itemInClip;
        [SerializeField] private AudioClip itemOutClip;

        [Header("Data Collection Events")]
        [Tooltip("Invoked with the item name when an item is stored in any slot.")]
        [SerializeField] private UnityEvent<string> onItemEntered = new UnityEvent<string>();
        [Tooltip("Invoked with the item name when an item is removed from any slot.")]
        [SerializeField] private UnityEvent<string> onItemExited = new UnityEvent<string>();

        [Header("Body Grab Suppression")]
        [Tooltip("Behaviours to disable while a hand is hovering one of the slots (typically the backpack body's HandGrabInteractable / Grabbable). Auto-discovered by CognitiveVR > Setup Smartphone In Scene if left empty.")]
        [SerializeField] private List<Behaviour> backpackBodyGrabbablesToSuppress = new List<Behaviour>();

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        /// <summary>Raised when an item is stored in any slot (item name + slot).</summary>
        public event Action<string, BackpackSlot> WhenItemEntered;

        /// <summary>Raised when an item is removed from any slot (item name + slot).</summary>
        public event Action<string, BackpackSlot> WhenItemExited;

        private readonly List<string> _storedItemNames = new List<string>();
        private readonly List<BackpackSlot> _boundSlots = new List<BackpackSlot>();
        private readonly Dictionary<Behaviour, bool> _suppressedOriginalEnabled = new Dictionary<Behaviour, bool>();
        private int _activeSlotHoverCount;

        /// <summary>Names of all items currently stored in the backpack.</summary>
        public IReadOnlyList<string> StoredItemNames => _storedItemNames;

        private void Awake()
        {
            EnsureSlotsExist();
        }

        private void OnValidate()
        {
            if (slotColliderSize.x < 0.05f || slotColliderSize.y < 0.05f || slotColliderSize.z < 0.05f)
            {
                slotColliderSize = Vector3.one;
            }
        }

        private void OnEnable()
        {
            SubscribeToSlots();
        }

        private void OnDisable()
        {
            UnsubscribeFromSlots();
            RestoreSuppressedBodyGrabbables();
            _activeSlotHoverCount = 0;
        }

        private void SubscribeToSlots()
        {
            UnsubscribeFromSlots();

            foreach (Transform slotTransform in slotTransforms)
            {
                if (slotTransform == null)
                {
                    continue;
                }

                BackpackSlot slot = slotTransform.GetComponent<BackpackSlot>();
                if (slot == null)
                {
                    continue;
                }

                slot.WhenItemStored += HandleSlotItemStored;
                slot.WhenItemRemoved += HandleSlotItemRemoved;
                _boundSlots.Add(slot);
            }
        }

        private void UnsubscribeFromSlots()
        {
            foreach (BackpackSlot slot in _boundSlots)
            {
                if (slot == null)
                {
                    continue;
                }
                slot.WhenItemStored -= HandleSlotItemStored;
                slot.WhenItemRemoved -= HandleSlotItemRemoved;
            }
            _boundSlots.Clear();
        }

        private void HandleSlotItemStored(BackpackSlot slot, InventoryItemMetaBridge item)
        {
            string itemName = item != null ? item.ItemName : "<null>";
            _storedItemNames.Add(itemName);
            PlayOneShot(itemInClip);

            WhenItemEntered?.Invoke(itemName, slot);
            onItemEntered.Invoke(itemName);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackInventoryZone)}] Item entered backpack: '{itemName}' (slot '{slot.name}').", slot);
            }
        }

        private void HandleSlotItemRemoved(BackpackSlot slot, InventoryItemMetaBridge item)
        {
            string itemName = item != null ? item.ItemName : "<null>";
            _storedItemNames.Remove(itemName);
            PlayOneShot(itemOutClip);

            WhenItemExited?.Invoke(itemName, slot);
            onItemExited.Invoke(itemName);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackInventoryZone)}] Item exited backpack: '{itemName}' (slot '{slot.name}').", slot);
            }
        }

        private void EnsureSlotsExist()
        {
            int configuredCount = CountConfiguredSlots();
            if (configuredCount > 0)
            {
                if (autoConfigureSlotComponents)
                {
                    foreach (Transform existingSlot in slotTransforms)
                    {
                        if (existingSlot != null)
                        {
                            AttachSlotComponent(existingSlot.gameObject);
                        }
                    }
                }
                return;
            }

            if (!autoGenerateSlots)
            {
                Debug.LogWarning($"[{nameof(BackpackInventoryZone)}] No slot transforms configured on {name} and auto-generation is disabled.", this);
                return;
            }

            slotTransforms.Clear();

            Vector2 area = autoSlotsLocalArea;
            float halfX = area.x * 0.5f;
            float halfZ = area.y * 0.5f;

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    float tx = (col == 0) ? -halfX : (col == 2 ? halfX : 0f);
                    float tz = (row == 0) ? halfZ : (row == 2 ? -halfZ : 0f);
                    string slotName = $"{autoSlotPrefix}{row}_{col}";

                    Transform existing = transform.Find(slotName);
                    GameObject slotGo;
                    if (existing == null)
                    {
                        // Only brand-new slots get the computed grid placement;
                        // slots already placed in the scene keep their transform.
                        slotGo = new GameObject(slotName);
                        slotGo.transform.SetParent(transform, false);
                        slotGo.transform.localPosition = new Vector3(tx, autoSlotsLocalY, tz);
                        slotGo.transform.localRotation = Quaternion.identity;
                        slotGo.transform.localScale = Vector3.one;
                    }
                    else
                    {
                        slotGo = existing.gameObject;
                    }

                    slotTransforms.Add(slotGo.transform);

                    if (autoConfigureSlotComponents)
                    {
                        AttachSlotComponent(slotGo);
                    }
                }
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackInventoryZone)}] Auto-generated 9 slot transforms under {name}.", this);
            }
        }

        private void AttachSlotComponent(GameObject slotGo)
        {
            if (slotGo == null)
            {
                return;
            }

            BackpackSlot slotComponent = slotGo.GetComponent<BackpackSlot>();
            if (slotComponent == null)
            {
                slotComponent = slotGo.AddComponent<BackpackSlot>();
            }

            slotComponent.Bind(this, slotColliderSize);
        }

        private int CountConfiguredSlots()
        {
            int count = 0;
            if (slotTransforms != null)
            {
                foreach (Transform slot in slotTransforms)
                {
                    if (slot != null) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Called by <see cref="BackpackSlot"/> when a hand starts hovering its
        /// HandGrabInteractable. Disables the configured backpack body
        /// grabbables on the 0->1 transition so the hand reaching INTO the
        /// backpack cannot accidentally grab the backpack itself.
        /// </summary>
        internal void NotifyHoverEnter(BackpackSlot slot)
        {
            _activeSlotHoverCount++;
            if (_activeSlotHoverCount == 1)
            {
                SuppressBodyGrabbables();
            }
        }

        /// <summary>
        /// Mirror of <see cref="NotifyHoverEnter"/>: re-enables the backpack
        /// body grabbables on the 1->0 transition.
        /// </summary>
        internal void NotifyHoverExit(BackpackSlot slot)
        {
            if (_activeSlotHoverCount <= 0)
            {
                return;
            }

            _activeSlotHoverCount--;
            if (_activeSlotHoverCount == 0)
            {
                RestoreSuppressedBodyGrabbables();
            }
        }

        private void SuppressBodyGrabbables()
        {
            if (backpackBodyGrabbablesToSuppress == null)
            {
                return;
            }

            foreach (Behaviour behaviour in backpackBodyGrabbablesToSuppress)
            {
                if (behaviour == null || _suppressedOriginalEnabled.ContainsKey(behaviour))
                {
                    continue;
                }

                _suppressedOriginalEnabled[behaviour] = behaviour.enabled;
                behaviour.enabled = false;

                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(BackpackInventoryZone)}] Suppressed body grabbable '{behaviour.GetType().Name}' on '{behaviour.name}'.", behaviour);
                }
            }
        }

        private void RestoreSuppressedBodyGrabbables()
        {
            if (_suppressedOriginalEnabled.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<Behaviour, bool> pair in _suppressedOriginalEnabled)
            {
                if (pair.Key == null)
                {
                    continue;
                }
                pair.Key.enabled = pair.Value;
            }
            _suppressedOriginalEnabled.Clear();
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (audioSource == null || clip == null)
            {
                return;
            }

            audioSource.PlayOneShot(clip);
        }

#if UNITY_EDITOR
        [ContextMenu("Generate 3x3 Slots Now")]
        private void EditorGenerateSlots()
        {
            slotTransforms.Clear();
            EnsureSlotsExist();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            foreach (Transform slot in slotTransforms)
            {
                if (slot == null) continue;
                Gizmos.DrawWireSphere(slot.position, 0.02f);
            }
        }
#endif
    }
}
