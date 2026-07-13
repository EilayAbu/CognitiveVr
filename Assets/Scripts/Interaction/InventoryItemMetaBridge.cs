using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Attach to each inventory item with a Meta ISDK Grabbable.
    /// Bridges Meta pointer events into item selected/released callbacks for
    /// inventory logic. The item identifies itself by its GameObject name and
    /// owns its stored size: when placed in a backpack slot it shrinks to
    /// <see cref="storedScale"/> of its original world size, and any behaviours
    /// listed in <see cref="disableWhileStored"/> are switched off until it is
    /// taken out again.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class InventoryItemMetaBridge : MonoBehaviour
    {
        [Header("Stored State")]
        [Tooltip("Multiplier applied to the item's original world size while stored in a slot (e.g. 0.2 = 20% of original size).")]
        [SerializeField] private float storedScale = 0.2f;
        [Tooltip("Behaviours on this item that are disabled while it is stored in a slot and re-enabled (to their previous state) when removed.")]
        [SerializeField] private List<Behaviour> disableWhileStored = new List<Behaviour>();

        [Header("Meta ISDK")]
        [SerializeField] private Grabbable grabbable;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        /// <summary>Identity of the item: simply its GameObject name.</summary>
        public string ItemName => gameObject.name;

        public event Action<InventoryItemMetaBridge> WhenItemSelected;
        public event Action<InventoryItemMetaBridge> WhenItemReleased;

        /// <summary>Raised right after the item has been snapped into a slot.</summary>
        public event Action<InventoryItemMetaBridge> WhenStoredInSlot;

        /// <summary>Raised right after the item has been restored from a slot.</summary>
        public event Action<InventoryItemMetaBridge> WhenRemovedFromSlot;

        public bool IsStoredInInventory { get; private set; }
        public float StoredScale => storedScale;

        private Rigidbody _rigidbody;
        private Transform _originalParent;
        private Vector3 _originalLocalScale;
        private bool _originalUseGravity;
        private bool _originalIsKinematic;
        private bool _cachedInitialState;
        private readonly Dictionary<Behaviour, bool> _disabledOriginalEnabled = new Dictionary<Behaviour, bool>();

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

            if (storedScale <= 0f)
            {
                storedScale = 0.2f;
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

        /// <summary>
        /// Snaps the item to the slot center, parents it under the slot and
        /// shrinks it to <see cref="storedScale"/> of its original world size.
        /// </summary>
        public void ApplyStoredState(Transform slotCenter)
        {
            if (slotCenter == null)
            {
                return;
            }

            CacheInitialStateIfNeeded();

            // Compensate for differences in parent lossy scale so that the stored
            // item ends up at (originalWorldSize * storedScale), regardless of
            // how the slot hierarchy itself is scaled.
            Vector3 oldLossy = _originalParent != null ? _originalParent.lossyScale : Vector3.one;
            Vector3 newLossy = slotCenter.lossyScale;
            Vector3 ratio = new Vector3(
                SafeDiv(oldLossy.x, newLossy.x),
                SafeDiv(oldLossy.y, newLossy.y),
                SafeDiv(oldLossy.z, newLossy.z));

            transform.SetParent(slotCenter, true);
            transform.SetPositionAndRotation(slotCenter.position, slotCenter.rotation);
            transform.localScale = Vector3.Scale(_originalLocalScale, ratio) * storedScale;

            if (_rigidbody != null)
            {
                _rigidbody.useGravity = false;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
            }

            DisableStoredBehaviours();
            IsStoredInInventory = true;
            WhenStoredInSlot?.Invoke(this);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(InventoryItemMetaBridge)}] Stored '{ItemName}' in slot '{slotCenter.name}' (scale x{storedScale:F3}).", this);
            }
        }

        private static float SafeDiv(float a, float b)
        {
            return Mathf.Abs(b) > Mathf.Epsilon ? a / b : 1f;
        }

        public void RestoreFromStoredState()
        {
            if (!_cachedInitialState || !IsStoredInInventory)
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

            RestoreStoredBehaviours();
            IsStoredInInventory = false;
            WhenRemovedFromSlot?.Invoke(this);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(InventoryItemMetaBridge)}] Removed '{ItemName}' from slot.", this);
            }
        }

        private void DisableStoredBehaviours()
        {
            _disabledOriginalEnabled.Clear();
            foreach (Behaviour behaviour in disableWhileStored)
            {
                if (behaviour == null || _disabledOriginalEnabled.ContainsKey(behaviour))
                {
                    continue;
                }

                _disabledOriginalEnabled[behaviour] = behaviour.enabled;
                behaviour.enabled = false;
            }
        }

        private void RestoreStoredBehaviours()
        {
            foreach (KeyValuePair<Behaviour, bool> pair in _disabledOriginalEnabled)
            {
                if (pair.Key == null)
                {
                    continue;
                }
                pair.Key.enabled = pair.Value;
            }
            _disabledOriginalEnabled.Clear();
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
