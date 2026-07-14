using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Attach to each inventory item with a Meta ISDK Grabbable.
    /// Bridges Meta pointer events into item selected/released callbacks for
    /// inventory logic, and owns the <see cref="SnapInteractor"/> (auto-created
    /// as a child) that lets the item snap into <see cref="BackpackSlot"/>s.
    /// The item identifies itself by its GameObject name and owns its stored
    /// size: when snapped into a backpack slot it shrinks to
    /// <see cref="storedScale"/> of its original size, and any behaviours
    /// listed in <see cref="disableWhileStored"/> are switched off until it is
    /// taken out again.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class InventoryItemMetaBridge : MonoBehaviour
    {
        [Header("Stored State")]
        [Tooltip("Multiplier applied to the item's original size while stored in a slot (e.g. 0.2 = 20% of original size).")]
        [SerializeField] private float storedScale = 0.2f;
        [Tooltip("Behaviours on this item that are disabled while it is stored in a slot and re-enabled (to their previous state) when removed.")]
        [SerializeField] private List<Behaviour> disableWhileStored = new List<Behaviour>();

        [Header("Meta ISDK")]
        [SerializeField] private Grabbable grabbable;
        [Tooltip("Auto-created as a child GameObject when left empty. Snaps this item into backpack slots.")]
        [SerializeField] private SnapInteractor snapInteractor;

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
        public SnapInteractor SnapInteractor => snapInteractor;

        private Rigidbody _rigidbody;
        private Transform _originalParent;
        private Vector3 _originalLocalScale;
        private bool _cachedInitialState;
        private readonly Dictionary<Behaviour, bool> _disabledOriginalEnabled = new Dictionary<Behaviour, bool>();

        private void Reset()
        {
            grabbable = GetComponent<Grabbable>();
            _rigidbody = GetComponent<Rigidbody>();
            ConfigureGrabbable();
        }

        private void Awake()
        {
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }

            _rigidbody = GetComponent<Rigidbody>();
            ConfigureGrabbable();
            EnsureSnapInteractor();

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

            ConfigureGrabbable();
        }

        /// <summary>
        /// Enforces the two Grabbable settings the snap flow depends on:
        /// - KinematicWhileSelected: the snap counts as a selection on the
        ///   Grabbable (the SnapInteractor raises Select pointer events on
        ///   it), so this single flag keeps the item kinematic both while
        ///   held by a hand and while stored in a slot - no manual
        ///   gravity/kinematic juggling anywhere.
        /// - TransferOnSecondSelection: while snapped, the SnapInteractor is
        ///   the current selector; when a hand grabs the stored item the new
        ///   selection must force the snap to release, otherwise both would
        ///   fight over the item. This is exactly how the SDK's own
        ///   SnapExamples configure their items.
        /// </summary>
        private void ConfigureGrabbable()
        {
            if (grabbable == null)
            {
                return;
            }

            grabbable.InjectOptionalKinematicWhileSelected(true);
            grabbable.TransferOnSecondSelection = true;
        }

        /// <summary>
        /// The SnapInteractor is what makes this item snappable into
        /// <see cref="BackpackSlot"/>s (SnapInteractables). It lives on a
        /// child GameObject and is wired to this item's Grabbable + Rigidbody:
        /// while the Grabbable is grabbed the interactor searches for slot
        /// volumes overlapping the item's colliders, and on release it snaps
        /// (eases) the item to the best slot's pose.
        /// </summary>
        private void EnsureSnapInteractor()
        {
            if (snapInteractor == null)
            {
                snapInteractor = GetComponentInChildren<SnapInteractor>(true);
            }

            if (snapInteractor == null)
            {
                // SnapInteractor.Start asserts a PointableElement and a
                // Rigidbody, so only auto-create it when both are available.
                if (grabbable == null || _rigidbody == null)
                {
                    return;
                }

                GameObject interactorGo = new GameObject("SnapInteractor");
                interactorGo.transform.SetParent(transform, false);
                interactorGo.transform.localPosition = Vector3.zero;
                interactorGo.transform.localRotation = Quaternion.identity;
                snapInteractor = interactorGo.AddComponent<SnapInteractor>();
            }

            if (grabbable != null && _rigidbody != null)
            {
                snapInteractor.InjectAllSnapInteractor(grabbable, _rigidbody);
            }
        }

        private void OnEnable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }

            if (snapInteractor != null)
            {
                snapInteractor.WhenStateChanged += HandleSnapInteractorStateChanged;
            }
        }

        private void OnDisable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }

            if (snapInteractor != null)
            {
                snapInteractor.WhenStateChanged -= HandleSnapInteractorStateChanged;
            }
        }

        /// <summary>
        /// The item's own SnapInteractor is the most direct signal that a snap
        /// actually happened: it enters Select exactly when the item snaps
        /// into a slot and leaves Select when the item is taken back out.
        /// Driving the stored state from here keeps the shrink/restore working
        /// even if slot-side event wiring misses the transition (both paths
        /// are idempotent via the IsStoredInInventory guard).
        /// </summary>
        private void HandleSnapInteractorStateChanged(InteractorStateChangeArgs args)
        {
            if (args.NewState == InteractorState.Select)
            {
                ApplyStoredState();
            }
            else if (args.PreviousState == InteractorState.Select)
            {
                RestoreFromStoredState();
            }
        }

        /// <summary>
        /// Called when the item's SnapInteractor selects a slot: parents the
        /// item under the slot, shrinks it to <see cref="storedScale"/> of its
        /// original world size and disables the configured behaviours.
        /// The snap movement keeps easing/holding the item at the slot pose.
        /// </summary>
        public void ApplyStoredState()
        {
            if (IsStoredInInventory)
            {
                return;
            }

            CacheInitialStateIfNeeded();

            Transform slotTransform = snapInteractor != null && snapInteractor.Interactable != null
                ? snapInteractor.Interactable.transform
                : null;
            if (slotTransform != null)
            {
                // Compensate for differences in parent lossy scale so the
                // stored item ends up at (originalWorldSize * storedScale)
                // regardless of how the slot hierarchy is scaled.
                Vector3 oldLossy = _originalParent != null ? _originalParent.lossyScale : Vector3.one;
                Vector3 newLossy = slotTransform.lossyScale;
                Vector3 ratio = new Vector3(
                    SafeDiv(oldLossy.x, newLossy.x),
                    SafeDiv(oldLossy.y, newLossy.y),
                    SafeDiv(oldLossy.z, newLossy.z));

                transform.SetParent(slotTransform, true);
                transform.localScale = Vector3.Scale(_originalLocalScale, ratio) * storedScale;
            }
            else
            {
                transform.localScale = _originalLocalScale * storedScale;
            }

            ReinitializeGrabTransformers();
            DisableStoredBehaviours();
            IsStoredInInventory = true;
            WhenStoredInSlot?.Invoke(this);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(InventoryItemMetaBridge)}] Stored '{ItemName}' (scale x{storedScale:F3}).", this);
            }
        }

        /// <summary>
        /// Called when the snap selection ends (typically because a hand
        /// grabbed the item back out). Restores the original parent, scale and
        /// the stored-disabled behaviours.
        /// </summary>
        public void RestoreFromStoredState()
        {
            if (!_cachedInitialState || !IsStoredInInventory)
            {
                return;
            }

            transform.SetParent(_originalParent, true);
            transform.localScale = _originalLocalScale;

            ReinitializeGrabTransformers();
            RestoreStoredBehaviours();
            IsStoredInInventory = false;
            WhenRemovedFromSlot?.Invoke(this);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(InventoryItemMetaBridge)}] Removed '{ItemName}' from slot.", this);
            }
        }

        private static float SafeDiv(float a, float b)
        {
            return Mathf.Abs(b) > Mathf.Epsilon ? a / b : 1f;
        }

        /// <summary>
        /// GrabFreeTransformer (the SDK's default grab transformer, also
        /// auto-added by Grabbable when no transformer is assigned) locks the
        /// object's localScale to 1x-1x of whatever scale it captured in
        /// Initialize, and re-applies that clamp every frame while the
        /// Grabbable is selected. Since the SnapInteractor keeps the Grabbable
        /// selected for as long as the item sits in a slot, the transformer
        /// would silently undo our shrink each frame. Re-running Initialize
        /// after every scale change re-anchors the clamp at the new scale.
        /// </summary>
        private void ReinitializeGrabTransformers()
        {
            if (grabbable == null)
            {
                return;
            }

            foreach (GrabFreeTransformer transformer in GetComponents<GrabFreeTransformer>())
            {
                transformer.Initialize(grabbable);
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
            _cachedInitialState = true;
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            // Ignore pointer events generated by our own SnapInteractor so
            // WhenItemSelected/WhenItemReleased keep meaning "hand grab" only.
            if (snapInteractor != null && pointerEvent.Identifier == snapInteractor.Identifier)
            {
                return;
            }

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
