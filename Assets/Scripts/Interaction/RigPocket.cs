using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Self-contained pocket / holster that holds one permanent, dedicated item
    /// (e.g. a phone). Reach in and grab to pull it out; release anywhere and it
    /// snaps back to the pocket automatically.
    ///
    /// KEY FIX vs. the earlier version: the item is restored to full size and to a
    /// world-stable parent on HOVER (before the grab is captured), not on Select.
    /// Meta's OneGrabFreeTransformer captures the grab offset on Select using the
    /// object's current lossy scale; if the scale changes during the grab the held
    /// object ends up offset/skewed/jittery. Priming on hover means the transformer
    /// always captures a clean, full-size pose.
    /// </summary>
    [DisallowMultipleComponent]
    public class RigPocket : MonoBehaviour
    {
        private enum PocketState { Stored, Primed, Out }

        [Header("Permanent Item")]
        [Tooltip("The single item that permanently lives in this pocket. Must carry a Meta ISDK Grabbable.")]
        public Grabbable permanentItem;

        [Header("Snap Target")]
        [Tooltip("Where the stored item snaps to. If empty, this transform is used.")]
        [SerializeField] private Transform holsterAnchor;

        [Header("Stored Appearance")]
        [Tooltip("Multiplier applied to the stored item's original world size while it sits in the pocket. " +
                 "Restored to full size the instant a hand hovers, so the grab pose is never distorted.")]
        [SerializeField] private float storedScale = 1f;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip itemInClip;
        [SerializeField] private AudioClip itemOutClip;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        private Rigidbody _itemRigidbody;
        private Collider[] _itemColliders;
        private bool[] _originalIsTrigger;

        private Transform _originalParent;
        private Vector3 _originalLocalScale;
        private bool _cachedInitialState;

        private PocketState _state = PocketState.Stored;

        // Every pointer (hand) currently selecting the item. Hand-to-hand transfers
        // temporarily hold two entries; we only snap back when this hits zero.
        private readonly HashSet<int> _grabbers = new HashSet<int>();
        private int _hoverCount;

        // Set when the grab count hits zero; the actual store is deferred to
        // LateUpdate so a same-frame re-grab (hand-to-hand transfer / two-hand
        // reorient) can cancel it before the phone ever snaps back.
        private bool _storeQueued;

        private Transform ActiveAnchor => holsterAnchor != null ? holsterAnchor : transform;

        /// <summary>True while the item is currently sitting inside the pocket (stored or primed).</summary>
        public bool HasItem => _state != PocketState.Out;

        private void OnValidate()
        {
            if (storedScale <= 0f)
            {
                storedScale = 1f;
            }
        }

        private void Start()
        {
            if (permanentItem == null)
            {
                Debug.LogError($"[{nameof(RigPocket)}] No permanent item assigned on {name}.", this);
                return;
            }

            CacheItemReferences();
            CacheInitialStateIfNeeded();

            permanentItem.WhenPointerEventRaised += HandlePointerEvent;

            StoreItem();
        }

        private void OnDisable()
        {
            if (permanentItem != null)
            {
                permanentItem.WhenPointerEventRaised -= HandlePointerEvent;
            }

            RestoreOriginalTriggers();
        }

        private void CacheItemReferences()
        {
            _itemRigidbody = permanentItem.GetComponent<Rigidbody>();
            if (_itemRigidbody == null)
            {
                _itemRigidbody = permanentItem.GetComponentInParent<Rigidbody>();
            }

            _itemColliders = permanentItem.GetComponentsInChildren<Collider>(true);
            _originalIsTrigger = new bool[_itemColliders.Length];
            for (int i = 0; i < _itemColliders.Length; i++)
            {
                _originalIsTrigger[i] = _itemColliders[i] != null && _itemColliders[i].isTrigger;
            }
        }

        private void CacheInitialStateIfNeeded()
        {
            if (_cachedInitialState)
            {
                return;
            }

            Transform itemTransform = permanentItem.transform;
            _originalParent = itemTransform.parent;
            _originalLocalScale = itemTransform.localScale;
            _cachedInitialState = true;
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Hover:
                    _hoverCount++;
                    // Restore full size + world-stable parent BEFORE the grab is captured.
                    if (_state == PocketState.Stored)
                    {
                        Prime();
                    }
                    break;

                case PointerEventType.Unhover:
                    _hoverCount = Mathf.Max(0, _hoverCount - 1);
                    // Hand left without grabbing -> tuck it back.
                    if (_state == PocketState.Primed && _hoverCount == 0 && _grabbers.Count == 0)
                    {
                        StoreItem();
                    }
                    break;

                case PointerEventType.Select:
                    // First hand to grab pulls it out. A second hand grabbing during a
                    // transfer/two-hand reorient just gets added to the set; the item is
                    // already out. Any pending store is cancelled by the new grab.
                    bool wasEmpty = _grabbers.Count == 0;
                    _grabbers.Add(pointerEvent.Identifier);
                    _storeQueued = false;
                    if (wasEmpty && _state != PocketState.Out)
                    {
                        PullOutItem();
                    }
                    break;

                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    _grabbers.Remove(pointerEvent.Identifier);
                    // Do NOT store here. During a transfer the releasing hand's Unselect
                    // can arrive before the receiving hand's Select in the same frame,
                    // dipping the count to 0 for an instant. Defer to LateUpdate.
                    if (_grabbers.Count == 0 && _state == PocketState.Out)
                    {
                        _storeQueued = true;
                    }
                    break;
            }
        }

        private void LateUpdate()
        {
            // Deferred store: run only if nothing re-grabbed the item this frame.
            if (_storeQueued && _grabbers.Count == 0 && _state == PocketState.Out)
            {
                _storeQueued = false;
                StoreItem();
            }
        }

        /// <summary>
        /// Full size, re-parented to the world-stable original parent, still frozen
        /// and sitting at the pocket. Nothing here runs during the grab itself, so
        /// Meta's grab transformer captures a clean, undistorted pose on Select.
        /// </summary>
        private void Prime()
        {
            Transform itemTransform = permanentItem.transform;

            itemTransform.SetParent(_originalParent, true); // world pose preserved
            itemTransform.localScale = _originalLocalScale; // full size, no scale swap during grab

            FreezeRigidbody();

            _state = PocketState.Primed;

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Primed {permanentItem.name} (full size).", permanentItem);
            }
        }

        private void PullOutItem()
        {
            // Already full-size + correctly parented from Prime(); just make it solid.
            RestoreOriginalTriggers();

            _storeQueued = false;
            _state = PocketState.Out;

            PlayOneShot(itemOutClip);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Pulled {permanentItem.name} out of pocket.", permanentItem);
            }
        }

        private void StoreItem()
        {
            Transform itemTransform = permanentItem.transform;
            Transform anchor = ActiveAnchor;

            // Compensate for differences in parent lossy scale so the stored item
            // ends up at originalWorldSize * storedScale regardless of anchor scale.
            Vector3 oldLossy = _originalParent != null ? _originalParent.lossyScale : Vector3.one;
            Vector3 newLossy = anchor != null ? anchor.lossyScale : Vector3.one;
            Vector3 ratio = new Vector3(
                SafeDiv(oldLossy.x, newLossy.x),
                SafeDiv(oldLossy.y, newLossy.y),
                SafeDiv(oldLossy.z, newLossy.z));

            itemTransform.SetParent(anchor, true);
            itemTransform.SetPositionAndRotation(anchor.position, anchor.rotation);
            itemTransform.localScale = Vector3.Scale(_originalLocalScale, ratio) * storedScale;

            FreezeRigidbody();

            // Force colliders to triggers so the pocketed item never blocks the
            // player CharacterController, while staying detectable by hand-grab.
            SetStoredAsTrigger();

            _state = PocketState.Stored;

            PlayOneShot(itemInClip);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Stored {permanentItem.name} (scale x{storedScale:F2}).", permanentItem);
            }
        }

        private void FreezeRigidbody()
        {
            if (_itemRigidbody == null)
            {
                return;
            }

            // Stays kinematic while grabbed too: Meta's transformer drives the
            // Transform directly, so a kinematic body follows cleanly with no
            // gravity/physics fighting the grab. It snaps back on release anyway.
            _itemRigidbody.linearVelocity = Vector3.zero;
            _itemRigidbody.angularVelocity = Vector3.zero;
            _itemRigidbody.useGravity = false;
            _itemRigidbody.isKinematic = true;
        }

        private void SetStoredAsTrigger()
        {
            if (_itemColliders == null)
            {
                return;
            }

            for (int i = 0; i < _itemColliders.Length; i++)
            {
                if (_itemColliders[i] != null)
                {
                    _itemColliders[i].isTrigger = true;
                }
            }
        }

        private void RestoreOriginalTriggers()
        {
            if (_itemColliders == null)
            {
                return;
            }

            for (int i = 0; i < _itemColliders.Length; i++)
            {
                if (_itemColliders[i] != null)
                {
                    _itemColliders[i].isTrigger = _originalIsTrigger[i];
                }
            }
        }

        private static float SafeDiv(float a, float b)
        {
            return Mathf.Abs(b) > Mathf.Epsilon ? a / b : 1f;
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
        private void OnDrawGizmosSelected()
        {
            Transform anchor = ActiveAnchor;
            if (anchor != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(anchor.position, 0.03f);
            }
        }
#endif
    }
}