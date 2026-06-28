using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Self-contained pocket / holster that holds one permanent, dedicated item
    /// (e.g. a phone). The assigned item lives inside the pocket at all times:
    /// reach in and grab it to pull it out, and the moment you let go it snaps
    /// back to the pocket automatically, wherever it was dropped.
    ///
    /// While stored, the item is frozen and (optionally) rescaled, and all of its
    /// colliders are forced to triggers. Trigger colliders are ignored by
    /// <see cref="CharacterController.Move"/>, so the pocketed item never blocks
    /// the player's movement, height changes, jumping or falling. The colliders
    /// stay enabled (just as triggers) so the item remains detectable by Meta
    /// hand-grab; on pull-out each collider's original trigger state is restored.
    /// </summary>
    [DisallowMultipleComponent]
    public class RigPocket : MonoBehaviour
    {
        [Header("Permanent Item")]
        [Tooltip("The single item that permanently lives in this pocket. Must carry a Meta ISDK Grabbable.")]
        [SerializeField] private Grabbable permanentItem;

        [Header("Snap Target")]
        [Tooltip("Where the stored item snaps to. If empty, this transform is used.")]
        [SerializeField] private Transform holsterAnchor;

        [Header("Stored Appearance")]
        [Tooltip("Multiplier applied to the stored item's original world size (1 = keep size, 0.2 = shrink to 20%).")]
        [SerializeField] private float storedScale = 1f;

        [Tooltip("Freeze the stored item's rigidbody (kinematic + gravity off) so it stays put inside the pocket.")]
        [SerializeField] private bool freezeRigidbody = true;

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
        private bool _originalUseGravity;
        private bool _originalIsKinematic;
        private bool _cachedInitialState;

        private bool _isOut;

        private Transform ActiveAnchor => holsterAnchor != null ? holsterAnchor : transform;

        /// <summary>True while the item is currently sitting inside the pocket.</summary>
        public bool HasItem => !_isOut;

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

            // Begin with the item tucked inside the pocket.
            StoreItem();
        }

        private void OnDisable()
        {
            if (permanentItem != null)
            {
                permanentItem.WhenPointerEventRaised -= HandlePointerEvent;
            }

            // Never leave the item's colliders stuck as triggers if the pocket
            // is disabled while the item is stored.
            SetStoredAsTrigger(false);
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

            if (_itemRigidbody != null)
            {
                _originalUseGravity = _itemRigidbody.useGravity;
                _originalIsKinematic = _itemRigidbody.isKinematic;
            }

            _cachedInitialState = true;
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Select:
                    // Grabbed: pull the item out of the pocket.
                    if (!_isOut)
                    {
                        PullOutItem();
                    }
                    break;

                case PointerEventType.Unselect:
                    // Released anywhere: snap it back into the pocket.
                    if (_isOut)
                    {
                        StoreItem();
                    }
                    break;
            }
        }

        private void StoreItem()
        {
            Transform itemTransform = permanentItem.transform;
            Transform anchor = ActiveAnchor;

            // Compensate for differences in parent lossy scale so the stored
            // item ends up at originalWorldSize * storedScale regardless of how
            // the anchor itself is scaled.
            Vector3 oldLossy = _originalParent != null ? _originalParent.lossyScale : Vector3.one;
            Vector3 newLossy = anchor != null ? anchor.lossyScale : Vector3.one;
            Vector3 ratio = new Vector3(
                SafeDiv(oldLossy.x, newLossy.x),
                SafeDiv(oldLossy.y, newLossy.y),
                SafeDiv(oldLossy.z, newLossy.z));

            itemTransform.SetParent(anchor, true);
            itemTransform.SetPositionAndRotation(anchor.position, anchor.rotation);
            itemTransform.localScale = Vector3.Scale(_originalLocalScale, ratio) * storedScale;

            if (_itemRigidbody != null && freezeRigidbody)
            {
                _itemRigidbody.linearVelocity = Vector3.zero;
                _itemRigidbody.angularVelocity = Vector3.zero;
                _itemRigidbody.useGravity = false;
                _itemRigidbody.isKinematic = true;
            }

            // Force colliders to triggers so the pocketed item never blocks the
            // player CharacterController, while staying grabbable.
            SetStoredAsTrigger(true);

            _isOut = false;

            PlayOneShot(itemInClip);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Stored {permanentItem.name} (scale x{storedScale:F2}).", permanentItem);
            }
        }

        private void PullOutItem()
        {
            Transform itemTransform = permanentItem.transform;

            itemTransform.SetParent(_originalParent, true);
            itemTransform.localScale = _originalLocalScale;

            if (_itemRigidbody != null && freezeRigidbody)
            {
                _itemRigidbody.useGravity = _originalUseGravity;
                _itemRigidbody.isKinematic = _originalIsKinematic;
            }

            // Restore the original collider trigger states now that the item is
            // back in play.
            SetStoredAsTrigger(false);

            _isOut = true;

            PlayOneShot(itemOutClip);

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(RigPocket)}] Pulled {permanentItem.name} out of pocket.", permanentItem);
            }
        }

        private void SetStoredAsTrigger(bool stored)
        {
            if (_itemColliders == null)
            {
                return;
            }

            for (int i = 0; i < _itemColliders.Length; i++)
            {
                Collider c = _itemColliders[i];
                if (c == null)
                {
                    continue;
                }

                c.isTrigger = stored ? true : _originalIsTrigger[i];
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
