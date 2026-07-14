using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Self-contained storage slot inside the backpack. The slot owns a trigger
    /// BoxCollider that detects inventory items; when an item is released while
    /// inside the trigger, the slot snaps it to its own center and stores it.
    /// While a hand hovers the slot the visual quad turns green so the user
    /// knows they are touching the right spot for inserting/removing an item.
    /// The slot only notifies the owning <see cref="BackpackInventoryZone"/>
    /// (and any other listener) about what entered/exited through the
    /// <see cref="WhenItemStored"/> / <see cref="WhenItemRemoved"/> events.
    /// </summary>
    [DisallowMultipleComponent]
    public class BackpackSlot : MonoBehaviour
    {
        [SerializeField] private BackpackInventoryZone zone;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private BoxCollider boxCollider;
        [SerializeField] private Vector3 colliderSize = Vector3.one;

        [Header("Touch Feedback")]
        [Tooltip("Optional. Renderer used for touch feedback. If empty, a small Quad child is created automatically.")]
        [SerializeField] private Renderer highlightRenderer;
        [Tooltip("Local size (X,Y) of the auto-created highlight quad.")]
        [SerializeField] private Vector2 highlightQuadSize = new Vector2(0.12f, 0.12f);
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private Color touchColor = new Color(0.2f, 1f, 0.2f, 0.8f);

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        /// <summary>Raised after an item has been snapped into this slot.</summary>
        public event Action<BackpackSlot, InventoryItemMetaBridge> WhenItemStored;

        /// <summary>Raised after an item has been released from this slot.</summary>
        public event Action<BackpackSlot, InventoryItemMetaBridge> WhenItemRemoved;

        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        private readonly HashSet<InventoryItemMetaBridge> _itemsInside = new HashSet<InventoryItemMetaBridge>();
        private InventoryItemMetaBridge _storedItem;
        private MaterialPropertyBlock _propertyBlock;
        private bool _warnedMissingGrabbable;
        private bool _isHovering;

        public bool HasItem => _storedItem != null;
        public InventoryItemMetaBridge StoredItem => _storedItem;

        private void Awake()
        {
            EnsureBoxCollider();
            EnsureHighlightRenderer();
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }
            PreventSlotFromBeingGrabbed();
            ApplyHighlightColor(idleColor);
        }

        private void OnEnable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
        }

        private void OnDisable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }

            if (_isHovering && zone != null)
            {
                zone.NotifyHoverExit(this);
            }
            _isHovering = false;
            ApplyHighlightColor(idleColor);

            foreach (InventoryItemMetaBridge item in _itemsInside)
            {
                UnsubscribeFromItem(item);
            }
            _itemsInside.Clear();
        }

        public void Bind(BackpackInventoryZone owningZone, Vector3 desiredColliderSize)
        {
            zone = owningZone;
            colliderSize = desiredColliderSize;
            EnsureBoxCollider();
        }

        // ------------------------------------------------------------------
        // Item detection (trigger volume)
        // ------------------------------------------------------------------

        private void OnTriggerEnter(Collider other)
        {
            InventoryItemMetaBridge item = other.GetComponentInParent<InventoryItemMetaBridge>();
            if (item == null || _itemsInside.Contains(item))
            {
                return;
            }

            _itemsInside.Add(item);
            item.WhenItemReleased += HandleItemReleasedInside;
            RefreshHighlight();

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackSlot)}] '{item.ItemName}' entered slot trigger '{name}'.", this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            InventoryItemMetaBridge item = other.GetComponentInParent<InventoryItemMetaBridge>();
            if (item == null || !_itemsInside.Contains(item))
            {
                return;
            }

            _itemsInside.Remove(item);
            UnsubscribeFromItem(item);
            RefreshHighlight();

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackSlot)}] '{item.ItemName}' exited slot trigger '{name}'.", this);
            }
        }

        private void UnsubscribeFromItem(InventoryItemMetaBridge item)
        {
            if (item == null)
            {
                return;
            }
            item.WhenItemReleased -= HandleItemReleasedInside;
        }

        private void HandleItemReleasedInside(InventoryItemMetaBridge item)
        {
            if (item == null || item.IsStoredInInventory || HasItem)
            {
                return;
            }

            StoreItem(item);
        }

        // ------------------------------------------------------------------
        // Storage
        // ------------------------------------------------------------------

        private void StoreItem(InventoryItemMetaBridge item)
        {
            _storedItem = item;
            item.ApplyStoredState(transform);
            item.WhenItemSelected += HandleStoredItemSelected;
            RefreshHighlight();
            WhenItemStored?.Invoke(this, item);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackSlot)}] Stored '{item.ItemName}' at center of slot '{name}'.", this);
            }
        }

        /// <summary>
        /// Releases the stored item back to the world (restoring its original
        /// scale, parent and physics) and notifies listeners.
        /// </summary>
        public void ReleaseStoredItem()
        {
            if (_storedItem == null)
            {
                return;
            }

            InventoryItemMetaBridge item = _storedItem;
            _storedItem = null;
            item.WhenItemSelected -= HandleStoredItemSelected;
            item.RestoreFromStoredState();
            RefreshHighlight();
            WhenItemRemoved?.Invoke(this, item);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackSlot)}] Released '{item.ItemName}' from slot '{name}'.", this);
            }
        }

        private void HandleStoredItemSelected(InventoryItemMetaBridge item)
        {
            if (item == _storedItem)
            {
                ReleaseStoredItem();
            }
        }

        // ------------------------------------------------------------------
        // Hand hover / grab on the slot itself
        // ------------------------------------------------------------------

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Hover:
                    if (!_isHovering)
                    {
                        _isHovering = true;
                        RefreshHighlight();
                        if (zone != null)
                        {
                            zone.NotifyHoverEnter(this);
                        }
                    }
                    break;

                case PointerEventType.Unhover:
                    if (_isHovering)
                    {
                        _isHovering = false;
                        RefreshHighlight();
                        if (zone != null)
                        {
                            zone.NotifyHoverExit(this);
                        }
                    }
                    break;

                case PointerEventType.Select:
                    ReleaseStoredItem();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Setup helpers
        // ------------------------------------------------------------------

        private void EnsureBoxCollider()
        {
            if (boxCollider == null)
            {
                boxCollider = GetComponent<BoxCollider>();
            }
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }

            // Trigger so OnTriggerEnter/Exit fires for items dropped into the
            // slot. Meta HandGrabInteractable still detects hover/grab against
            // trigger colliders for the hand-touch feedback path.
            boxCollider.isTrigger = true;
            boxCollider.size = colliderSize;
            boxCollider.center = Vector3.zero;
        }

        private void EnsureHighlightRenderer()
        {
            if (highlightRenderer != null)
            {
                return;
            }

            highlightRenderer = GetComponentInChildren<MeshRenderer>();
            if (highlightRenderer != null)
            {
                return;
            }

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SlotHighlight";

            Collider quadCollider = quad.GetComponent<Collider>();
            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }

            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = Vector3.zero;
            // Quad faces +Z by default; rotate to lie flat facing up.
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(highlightQuadSize.x, highlightQuadSize.y, 1f);

            highlightRenderer = quad.GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Recomputes and applies the highlight color from the current
        /// hover/occupancy state: green while a hand hovers the slot OR while
        /// any item's collider overlaps an empty slot's trigger (so dropping
        /// an item in gives immediate visual confirmation even without a
        /// hand hovering), idle otherwise.
        /// </summary>
        private void RefreshHighlight()
        {
            bool shouldHighlight = _isHovering || (!HasItem && _itemsInside.Count > 0);
            ApplyHighlightColor(shouldHighlight ? touchColor : idleColor);
        }

        private void ApplyHighlightColor(Color color)
        {
            if (highlightRenderer == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            highlightRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(ColorPropertyId, color);
            _propertyBlock.SetColor(BaseColorPropertyId, color);
            highlightRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void Reset()
        {
            grabbable = GetComponent<Grabbable>();
            boxCollider = GetComponent<BoxCollider>();
            EnsureBoxCollider();
        }

        private void OnValidate()
        {
            if (colliderSize.x < 0.05f || colliderSize.y < 0.05f || colliderSize.z < 0.05f)
            {
                colliderSize = Vector3.one;
            }

            if (highlightQuadSize.x <= 0f || highlightQuadSize.y <= 0f)
            {
                highlightQuadSize = new Vector2(0.12f, 0.12f);
            }

            PreventSlotFromBeingGrabbed();
        }

        /// <summary>
        /// The slot itself must only ever report hover/select pointer events
        /// (for the touch-feedback highlight and select-to-release) and must
        /// never actually be picked up/dragged by hand. Grabbable clamps how
        /// many active grab points its transformer will honor via
        /// MaxGrabPoints; forcing it to 0 disables all movement while leaving
        /// Hover/Select/Unselect events (and WhenPointerEventRaised) intact.
        /// </summary>
        private void PreventSlotFromBeingGrabbed()
        {
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }

            if (grabbable != null)
            {
                grabbable.MaxGrabPoints = 0;
            }
        }

        private void Start()
        {
            // Defer the warning to Start so the editor setup pass has a chance
            // to add the Grabbable before we complain.
            if (grabbable == null && !_warnedMissingGrabbable)
            {
                _warnedMissingGrabbable = true;
                Debug.LogWarning(
                    $"[{nameof(BackpackSlot)}] No Oculus.Interaction.Grabbable on slot '{name}'. " +
                    "Hand touch feedback and slot-grab release are disabled until you run 'CognitiveVR > Setup Smartphone In Scene'.",
                    this);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = HasItem ? new Color(0.3f, 1f, 0.3f, 0.9f) : new Color(1f, 0.85f, 0.2f, 0.6f);
            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, colliderSize);
            Gizmos.matrix = prev;
        }
#endif
    }
}
