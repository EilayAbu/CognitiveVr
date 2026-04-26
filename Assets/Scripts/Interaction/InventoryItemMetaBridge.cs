using System;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Identifies the semantic role of an inventory item independently of its
    /// scene name. Consumers (e.g. SmsSwapTracker) match against these enum
    /// values rather than fragile string comparisons.
    /// </summary>
    public enum ItemId
    {
        Unknown = 0,
        Wallet = 1,
        Laptop = 2,
        Tablet = 3,
        WaterBottle = 4,
        Keys = 5,
        Medicine = 6,
        Umbrella = 7
    }

    /// <summary>
    /// Attach to each inventory item with a Meta ISDK Grabbable.
    /// Bridges Meta pointer events into item selected/released callbacks for inventory logic.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class InventoryItemMetaBridge : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Semantic identifier for this inventory item. Used by trackers (e.g. SmsSwapTracker) to match laptop / tablet / etc.")]
        [SerializeField] private ItemId itemId = ItemId.Unknown;

        [Header("Meta ISDK")]
        [SerializeField] private Grabbable grabbable;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        public ItemId ItemId => itemId;

        public event Action<InventoryItemMetaBridge> WhenItemSelected;
        public event Action<InventoryItemMetaBridge> WhenItemReleased;

        public bool IsStoredInInventory { get; private set; }

        private Rigidbody _rigidbody;
        private Transform _originalParent;
        private Vector3 _originalLocalScale;
        private bool _originalUseGravity;
        private bool _originalIsKinematic;
        private bool _cachedInitialState;

        private void Reset()
        {
            grabbable = GetComponent<Grabbable>();
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }

            _rigidbody = GetComponent<Rigidbody>();

            if (grabbable == null)
            {
                Debug.LogWarning($"[{nameof(InventoryItemMetaBridge)}] Missing Meta Grabbable on {name}.", this);
            }
        }

        private void OnValidate()
        {
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }
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
        }

        public void ApplyStoredState(Transform inventoryParent, Transform slotTransform, float inventoryScaleMultiplier)
        {
            CacheInitialStateIfNeeded();

            // Compensate for differences in parent lossy scale so that the stored
            // item ends up at (originalWorldSize * inventoryScaleMultiplier),
            // regardless of how the inventory parent itself is scaled.
            Vector3 oldLossy = _originalParent != null ? _originalParent.lossyScale : Vector3.one;
            Vector3 newLossy = inventoryParent != null ? inventoryParent.lossyScale : Vector3.one;
            Vector3 ratio = new Vector3(
                SafeDiv(oldLossy.x, newLossy.x),
                SafeDiv(oldLossy.y, newLossy.y),
                SafeDiv(oldLossy.z, newLossy.z));

            transform.SetParent(inventoryParent, true);
            transform.SetPositionAndRotation(slotTransform.position, slotTransform.rotation);
            transform.localScale = Vector3.Scale(_originalLocalScale, ratio) * inventoryScaleMultiplier;

            if (_rigidbody != null)
            {
                _rigidbody.useGravity = false;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
            }

            IsStoredInInventory = true;
        }

        private static float SafeDiv(float a, float b)
        {
            return Mathf.Abs(b) > Mathf.Epsilon ? a / b : 1f;
        }

        public void RestoreFromStoredState()
        {
            if (!_cachedInitialState)
            {
                return;
            }

            transform.SetParent(_originalParent, true);
            transform.localScale = _originalLocalScale;

            if (_rigidbody != null)
            {
                _rigidbody.useGravity = _originalUseGravity;
                _rigidbody.isKinematic = _originalIsKinematic;
            }

            IsStoredInInventory = false;
        }

        private void CacheInitialStateIfNeeded()
        {
            if (_cachedInitialState)
            {
                return;
            }

            _originalParent = transform.parent;
            _originalLocalScale = transform.localScale;

            if (_rigidbody != null)
            {
                _originalUseGravity = _rigidbody.useGravity;
                _originalIsKinematic = _rigidbody.isKinematic;
            }

            _cachedInitialState = true;
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Select:
                    WhenItemSelected?.Invoke(this);
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[{nameof(InventoryItemMetaBridge)}] Select: {name}", this);
                    }
                    break;

                case PointerEventType.Unselect:
                    WhenItemReleased?.Invoke(this);
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[{nameof(InventoryItemMetaBridge)}] Unselect: {name}", this);
                    }
                    break;
            }
        }
    }
}
