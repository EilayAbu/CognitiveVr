using System;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Self-contained storage slot inside the backpack, built on Meta ISDK's
    /// snap system. The slot owns a <see cref="SnapInteractable"/> (plus the
    /// kinematic Rigidbody and trigger BoxCollider it requires): while an item
    /// is held over the slot the SnapInteractable reports Hover, and when the
    /// item is released its <see cref="SnapInteractor"/> selects this slot and
    /// eases the item to the slot center. Grabbing the stored item transfers
    /// it back to the hand (via Grabbable.TransferOnSecondSelection),
    /// which unselects the snap and restores the item.
    /// While a hand or a held item hovers the slot the visual quad turns green
    /// so the user knows they are touching the right spot.
    /// The slot notifies the owning <see cref="BackpackInventoryZone"/> (and
    /// any other listener) through <see cref="WhenItemStored"/> /
    /// <see cref="WhenItemRemoved"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class BackpackSlot : MonoBehaviour
    {
        [SerializeField] private BackpackInventoryZone zone;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private HandGrabInteractable handGrabInteractable;
        [SerializeField] private SnapInteractable snapInteractable;
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

        private InventoryItemMetaBridge _storedItem;
        private MaterialPropertyBlock _propertyBlock;
        private bool _warnedMissingGrabbable;
        private bool _isHandHovering;
        private bool _isItemHovering;

        public bool HasItem => _storedItem != null;
        public InventoryItemMetaBridge StoredItem => _storedItem;

        private void Awake()
        {
            EnsureBoxCollider();
            EnsureSnapInteractable();
            EnsureHighlightRenderer();
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }
            if (handGrabInteractable == null)
            {
                handGrabInteractable = GetComponent<HandGrabInteractable>();
            }
            PreventSlotFromBeingGrabbed();
            RefreshHandGrabAvailability();
            ApplyHighlightColor(idleColor);
        }

        private void OnEnable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }

            if (snapInteractable != null)
            {
                snapInteractable.WhenStateChanged += HandleSnapStateChanged;
                snapInteractable.WhenSelectingInteractorViewAdded += HandleSnapSelected;
                snapInteractable.WhenSelectingInteractorViewRemoved += HandleSnapUnselected;
            }
        }

        private void OnDisable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }

            if (snapInteractable != null)
            {
                snapInteractable.WhenStateChanged -= HandleSnapStateChanged;
                snapInteractable.WhenSelectingInteractorViewAdded -= HandleSnapSelected;
                snapInteractable.WhenSelectingInteractorViewRemoved -= HandleSnapUnselected;
            }

            if (_isHandHovering && zone != null)
            {
                zone.NotifyHoverExit(this);
            }
            _isHandHovering = false;
            _isItemHovering = false;
            ApplyHighlightColor(idleColor);
        }

        public void Bind(BackpackInventoryZone owningZone, Vector3 desiredColliderSize)
        {
            zone = owningZone;
            colliderSize = desiredColliderSize;
            EnsureBoxCollider();
        }

        // ------------------------------------------------------------------
        // Snap interactable (item store / remove)
        // ------------------------------------------------------------------

        private void HandleSnapSelected(IInteractorView interactorView)
        {
            InventoryItemMetaBridge item = ResolveItem(interactorView);
            if (item == null)
            {
                return;
            }

            _storedItem = item;
            item.ApplyStoredState();
            RefreshHandGrabAvailability();
            RefreshHighlight();
            WhenItemStored?.Invoke(this, item);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackSlot)}] Stored '{item.ItemName}' in slot '{name}'.", this);
            }
        }

        private void HandleSnapUnselected(IInteractorView interactorView)
        {
            InventoryItemMetaBridge item = ResolveItem(interactorView);
            if (item == null || item != _storedItem)
            {
                return;
            }

            _storedItem = null;
            item.RestoreFromStoredState();
            RefreshHandGrabAvailability();
            RefreshHighlight();
            WhenItemRemoved?.Invoke(this, item);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(BackpackSlot)}] Released '{item.ItemName}' from slot '{name}'.", this);
            }
        }

        private void HandleSnapStateChanged(InteractableStateChangeArgs args)
        {
            bool itemHovering = args.NewState == InteractableState.Hover;
            if (itemHovering == _isItemHovering)
            {
                return;
            }

            _isItemHovering = itemHovering;
            RefreshHighlight();
        }

        private static InventoryItemMetaBridge ResolveItem(IInteractorView interactorView)
        {
            if (!(interactorView is SnapInteractor snapInteractor) || snapInteractor.Rigidbody == null)
            {
                return null;
            }

            return snapInteractor.Rigidbody.GetComponentInParent<InventoryItemMetaBridge>();
        }

        // ------------------------------------------------------------------
        // Hand hover on the slot itself
        // ------------------------------------------------------------------

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Hover:
                    if (!_isHandHovering)
                    {
                        _isHandHovering = true;
                        RefreshHighlight();
                        if (zone != null)
                        {
                            zone.NotifyHoverEnter(this);
                        }
                    }
                    break;

                case PointerEventType.Unhover:
                    if (_isHandHovering)
                    {
                        _isHandHovering = false;
                        RefreshHighlight();
                        if (zone != null)
                        {
                            zone.NotifyHoverExit(this);
                        }
                    }
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

            // Trigger so the snap registry's InteractableTriggerBroadcaster
            // detects held items overlapping the slot. Meta HandGrabInteractable
            // still detects hover against trigger colliders for the
            // hand-touch feedback path.
            boxCollider.isTrigger = true;
            boxCollider.size = colliderSize;
            boxCollider.center = Vector3.zero;
        }

        /// <summary>
        /// The snap system requires a (kinematic) Rigidbody on the slot: the
        /// SDK's CollisionInteractionRegistry adds a trigger broadcaster to
        /// the interactable's Rigidbody GameObject to find snap candidates by
        /// collider overlap. One item per slot is enforced through
        /// MaxInteractors / MaxSelectingInteractors.
        /// </summary>
        private void EnsureSnapInteractable()
        {
            Rigidbody slotRigidbody = GetComponent<Rigidbody>();
            if (slotRigidbody == null)
            {
                slotRigidbody = gameObject.AddComponent<Rigidbody>();
            }
            slotRigidbody.isKinematic = true;
            slotRigidbody.useGravity = false;

            if (snapInteractable == null)
            {
                snapInteractable = GetComponent<SnapInteractable>();
            }
            if (snapInteractable == null)
            {
                snapInteractable = gameObject.AddComponent<SnapInteractable>();
            }

            if (snapInteractable.Rigidbody == null)
            {
                snapInteractable.InjectRigidbody(slotRigidbody);
            }

            snapInteractable.MaxInteractors = 1;
            snapInteractable.MaxSelectingInteractors = 1;
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
        /// Recomputes and applies the highlight color from the current hover
        /// state: green while a hand touches the slot OR while a held item
        /// hovers the slot's snap volume, idle otherwise.
        /// </summary>
        private void RefreshHighlight()
        {
            bool shouldHighlight = _isHandHovering || _isItemHovering;
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
            handGrabInteractable = GetComponent<HandGrabInteractable>();
            snapInteractable = GetComponent<SnapInteractable>();
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
        /// The slot itself must only ever report hover pointer events (for the
        /// touch-feedback highlight) and must never actually be picked
        /// up/dragged by hand. Grabbable clamps how many active grab points
        /// its transformer will honor via MaxGrabPoints; forcing it to 0
        /// disables all movement while leaving Hover/Unhover events (and
        /// WhenPointerEventRaised) intact.
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

        /// <summary>
        /// While a slot holds an item, its own HandGrabInteractable occupies
        /// the same space as the (shrunk) stored item's HandGrabInteractable,
        /// so a hand reaching for the tiny item can get scored onto the slot
        /// instead. Disabling the slot's interactable while occupied removes
        /// that competing target; Interactable.Disable() cleanly cancels any
        /// active hover/select on the slot itself, so hover bookkeeping
        /// (_isHandHovering / RefreshHighlight) never gets stuck. Re-enabled
        /// the moment the slot goes empty again so hover feedback still works
        /// for dropping a new item in.
        /// </summary>
        private void RefreshHandGrabAvailability()
        {
            if (handGrabInteractable == null)
            {
                return;
            }

            if (HasItem)
            {
                handGrabInteractable.Disable();
            }
            else
            {
                handGrabInteractable.Enable();
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
                    "Hand touch feedback is disabled until you run 'CognitiveVR > Setup Smartphone In Scene'.",
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
