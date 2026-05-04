using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Place on PackLP (or the backpack grid plane parent) to manage 3x3 storage slots.
    /// Items are stored when released inside the backpack trigger volume.
    /// The component is self-healing: if no slots are configured it will generate
    /// a 3x3 grid of child Transforms at runtime, and it will force the attached
    /// collider into trigger mode so OnTriggerEnter actually fires.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BackpackInventoryZone : MonoBehaviour
    {
        [Header("Inventory Layout")]
        [Tooltip("Optional parent for stored items. If empty, this object is used.")]
        [SerializeField] private Transform inventoryParent;
        [Tooltip("Assign 9 Meta Snap Zone components (for example SnapInteractable) when available.")]
        [SerializeField] private List<MonoBehaviour> metaSnapZones = new List<MonoBehaviour>(9);
        [Tooltip("Fallback list of exactly 9 slot transforms (3x3) used as snap points.")]
        [SerializeField] private List<Transform> slotTransforms = new List<Transform>(9);
        [Tooltip("Multiplier applied to a stored item's original world size (e.g. 0.2 stores items at 20% of their original size).")]
        [SerializeField] private float inventoryScale = 0.2f;

        [Header("Auto Slot Generation")]
        [Tooltip("If true and no slots are configured, generate a 3x3 grid of child Transforms at runtime.")]
        [SerializeField] private bool autoGenerateSlots = true;
        [Tooltip("Local-space size (X = width, Y = depth) of the grid covered by the auto-generated slots.")]
        [SerializeField] private Vector2 autoSlotsLocalArea = new Vector2(0.7f, 0.7f);
        [Tooltip("Local-space Y position (relative to this transform) for auto-generated slot transforms.")]
        [SerializeField] private float autoSlotsLocalY = 0f;
        [SerializeField] private string autoSlotPrefix = "Slot_";
        [Tooltip("If true, attach a BackpackSlot component (with BoxCollider) to each slot so the hand can grab items per-slot.")]
        [SerializeField] private bool autoConfigureSlotComponents = true;
        [Tooltip("BoxCollider size (local space) added on every slot for hand-grab detection.")]
        [SerializeField] private Vector3 slotColliderSize = Vector3.one;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip itemInClip;
        [SerializeField] private AudioClip itemOutClip;

        [Header("Body Grab Suppression")]
        [Tooltip("Behaviours to disable while a hand is hovering one of the slots (typically the backpack body's HandGrabInteractable / Grabbable). Auto-discovered by CognitiveVR > Setup Smartphone In Scene if left empty.")]
        [SerializeField] private List<Behaviour> backpackBodyGrabbablesToSuppress = new List<Behaviour>();

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        private readonly Dictionary<Transform, InventoryItemMetaBridge> _slotOccupancy = new Dictionary<Transform, InventoryItemMetaBridge>();
        private readonly HashSet<InventoryItemMetaBridge> _itemsInsideVolume = new HashSet<InventoryItemMetaBridge>();
        private readonly Dictionary<Behaviour, bool> _suppressedOriginalEnabled = new Dictionary<Behaviour, bool>();
        private int _activeSlotHoverCount;

        private Transform ActiveInventoryParent => inventoryParent != null ? inventoryParent : transform;

        private void Awake()
        {
            ForceTriggerCollider();
            EnsureSlotsExist();
        }

        private void OnValidate()
        {
            if (inventoryScale <= 0f)
            {
                inventoryScale = 0.2f;
            }

            if (slotColliderSize.x < 0.05f || slotColliderSize.y < 0.05f || slotColliderSize.z < 0.05f)
            {
                slotColliderSize = Vector3.one;
            }
        }

        private void OnEnable()
        {
            RegisterKnownItemsInScene();
        }

        private void OnDisable()
        {
            foreach (InventoryItemMetaBridge item in FindObjectsByType<InventoryItemMetaBridge>(FindObjectsSortMode.None))
            {
                item.WhenItemSelected -= HandleItemSelected;
                item.WhenItemReleased -= HandleItemReleased;
            }

            RestoreSuppressedBodyGrabbables();
            _activeSlotHoverCount = 0;
            _itemsInsideVolume.Clear();
            _slotOccupancy.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            InventoryItemMetaBridge item = other.GetComponentInParent<InventoryItemMetaBridge>();
            if (item == null)
            {
                return;
            }

            RegisterItem(item);
            _itemsInsideVolume.Add(item);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackInventoryZone)}] Item entered backpack volume: {item.name}", item);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            InventoryItemMetaBridge item = other.GetComponentInParent<InventoryItemMetaBridge>();
            if (item == null)
            {
                return;
            }

            _itemsInsideVolume.Remove(item);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackInventoryZone)}] Item exited backpack volume: {item.name}", item);
            }
        }

        private void ForceTriggerCollider()
        {
            Collider volumeCollider = GetComponent<Collider>();
            if (volumeCollider == null)
            {
                Debug.LogError($"[{nameof(BackpackInventoryZone)}] Missing collider on {name}.", this);
                return;
            }

            if (!volumeCollider.isTrigger)
            {
                volumeCollider.isTrigger = true;
                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(BackpackInventoryZone)}] Forced collider to trigger on {name}.", this);
                }
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
                Debug.LogWarning($"[{nameof(BackpackInventoryZone)}] No snap zones / slot transforms configured on {name} and auto-generation is disabled.", this);
                return;
            }

            slotTransforms.Clear();

            Vector2 area = autoSlotsLocalArea;
            // Auto-fit slot grid to ~80% of the BoxCollider XZ footprint when
            // the user left the default value untouched, so slots are spaced
            // sensibly regardless of how small/large the parent's lossy scale is.
            if (Mathf.Approximately(area.x, 0.7f) && Mathf.Approximately(area.y, 0.7f))
            {
                if (GetComponent<Collider>() is BoxCollider box)
                {
                    area = new Vector2(box.size.x * 0.8f, box.size.z * 0.8f);
                }
            }

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
                        slotGo = new GameObject(slotName);
                        slotGo.transform.SetParent(transform, false);
                    }
                    else
                    {
                        slotGo = existing.gameObject;
                    }

                    slotGo.transform.localPosition = new Vector3(tx, autoSlotsLocalY, tz);
                    slotGo.transform.localRotation = Quaternion.identity;
                    slotGo.transform.localScale = Vector3.one;
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
            if (metaSnapZones != null)
            {
                foreach (MonoBehaviour zone in metaSnapZones)
                {
                    if (zone != null) count++;
                }
            }
            if (count > 0) return count;

            if (slotTransforms != null)
            {
                foreach (Transform slot in slotTransforms)
                {
                    if (slot != null) count++;
                }
            }
            return count;
        }

        private void RegisterKnownItemsInScene()
        {
            foreach (InventoryItemMetaBridge item in FindObjectsByType<InventoryItemMetaBridge>(FindObjectsSortMode.None))
            {
                RegisterItem(item);
            }
        }

        private void RegisterItem(InventoryItemMetaBridge item)
        {
            item.WhenItemSelected -= HandleItemSelected;
            item.WhenItemReleased -= HandleItemReleased;
            item.WhenItemSelected += HandleItemSelected;
            item.WhenItemReleased += HandleItemReleased;
        }

        private void HandleItemSelected(InventoryItemMetaBridge item)
        {
            if (item.IsStoredInInventory)
            {
                ReleaseItemFromInventory(item, true);
            }
        }

        private void HandleItemReleased(InventoryItemMetaBridge item)
        {
            if (!_itemsInsideVolume.Contains(item) || item.IsStoredInInventory)
            {
                return;
            }

            Transform nearestSlot = GetNearestFreeSlot(item.transform.position);
            if (nearestSlot == null)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(BackpackInventoryZone)}] No free slot available for {item.name}.", item);
                }
                return;
            }

            StoreItemInInventory(item, nearestSlot);
        }

        private void StoreItemInInventory(InventoryItemMetaBridge item, Transform slot)
        {
            if (_slotOccupancy.ContainsKey(slot))
            {
                return;
            }

            item.ApplyStoredState(ActiveInventoryParent, slot, inventoryScale);
            _slotOccupancy[slot] = item;
            BackpackSlot slotComponent = slot.GetComponent<BackpackSlot>();
            if (slotComponent != null)
            {
                slotComponent.SetStoredItem(item);
            }
            PlayOneShot(itemInClip);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackInventoryZone)}] Stored {item.name} in slot {slot.name} (scale x{inventoryScale:F3}).", item);
            }
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

        /// <summary>
        /// Called by <see cref="BackpackSlot"/> when the hand performs a grab
        /// gesture on a slot collider. Releases the item bound to that slot.
        /// </summary>
        internal void ReleaseItemFromSlot(BackpackSlot slot)
        {
            if (slot == null)
            {
                return;
            }

            if (!_slotOccupancy.TryGetValue(slot.transform, out InventoryItemMetaBridge item) || item == null)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(BackpackInventoryZone)}] Slot '{slot.name}' grabbed but holds no item.", slot);
                }
                return;
            }

            ReleaseItemFromInventory(item, true);
        }

        private void ReleaseItemFromInventory(InventoryItemMetaBridge item, bool playAudio)
        {
            Transform occupiedSlot = null;
            foreach (KeyValuePair<Transform, InventoryItemMetaBridge> pair in _slotOccupancy)
            {
                if (pair.Value == item)
                {
                    occupiedSlot = pair.Key;
                    break;
                }
            }

            if (occupiedSlot != null)
            {
                _slotOccupancy.Remove(occupiedSlot);
                BackpackSlot slotComponent = occupiedSlot.GetComponent<BackpackSlot>();
                if (slotComponent != null)
                {
                    slotComponent.ClearStoredItem();
                }
            }

            item.RestoreFromStoredState();

            if (playAudio)
            {
                PlayOneShot(itemOutClip);
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackInventoryZone)}] Released {item.name} from inventory.", item);
            }
        }

        private Transform GetNearestFreeSlot(Vector3 worldPosition)
        {
            Transform nearest = null;
            float nearestDistanceSqr = float.MaxValue;

            foreach (Transform slot in GetConfiguredSlots())
            {
                if (slot == null || _slotOccupancy.ContainsKey(slot))
                {
                    continue;
                }

                float distanceSqr = (slot.position - worldPosition).sqrMagnitude;
                if (distanceSqr < nearestDistanceSqr)
                {
                    nearestDistanceSqr = distanceSqr;
                    nearest = slot;
                }
            }

            return nearest;
        }

        private IEnumerable<Transform> GetConfiguredSlots()
        {
            bool hasMetaZones = false;
            if (metaSnapZones != null && metaSnapZones.Count > 0)
            {
                foreach (MonoBehaviour zone in metaSnapZones)
                {
                    if (zone != null)
                    {
                        hasMetaZones = true;
                        yield return zone.transform;
                    }
                }
            }

            if (hasMetaZones)
            {
                yield break;
            }

            foreach (Transform slot in slotTransforms)
            {
                if (slot != null)
                {
                    yield return slot;
                }
            }
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
            Collider c = GetComponent<Collider>();
            if (c is BoxCollider box)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
                Matrix4x4 prev = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = prev;
            }

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
