using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

namespace CognitiveVR.Data
{
    /// <summary>
    /// Drop-on-and-forget usage reporter for a single scene item.
    ///
    /// Finds every Meta ISDK interactable (IInteractableView) on this object
    /// and its children (HandGrabInteractable, GrabInteractable,
    /// PokeInteractable, RayInteractable...) and reports the AGGREGATED
    /// select / hover transitions to <see cref="ExperimentDataManager"/>.
    /// Aggregation means: an item grabbed by hand tracking AND controller
    /// interactables still logs one clean select/unselect pair, and a
    /// hand-to-hand transfer with overlap stays one continuous hold.
    ///
    /// Works for grabbable props and poke buttons alike. Does not modify any
    /// existing component. Do NOT also wire the same item's
    /// InteractableUnityEventWrapper to the manager, or you'll get duplicates.
    /// </summary>
    public class ItemUsageTracker : MonoBehaviour
    {
        [Tooltip("Name used in the log. Empty = InventoryItemMetaBridge.ItemName if present (keeps names identical to backpack rows), else the GameObject name.")]
        [SerializeField] private string overrideItemName;

        [Tooltip("Also log hover enter/exit (hand approaching the item). Enables the hover_to_select hesitation metric.")]
        [SerializeField] private bool trackHover = true;

        [Tooltip("Tag this item for the bag-collection measurement. Purely descriptive - written to the item's CSV rows and JSON summary, does not change what gets logged.")]
        [SerializeField] private bool bagCollectionMeasurement = false;

        [Tooltip("Tag this item for the initiative measurement. Purely descriptive - written to the item's CSV rows and JSON summary, does not change what gets logged.")]
        [SerializeField] private bool initiativeMeasurement = false;

        [Header("Floor Contact")]
        [Tooltip("Log an item_dropped row when this item hits the floor.")]
        [SerializeField] private bool trackFloorContact = true;

        [Tooltip("Which layer(s) count as the floor. Must be set - an empty mask logs nothing.")]
        [SerializeField] private LayerMask floorLayers;

        [Tooltip("Ignore further floor hits for this long, so one drop with a bounce logs once (seconds).")]
        [SerializeField] private float floorCooldown = 1f;

        [Tooltip("Only count floor hits after the item has been picked up at least once, so the scene settling at startup is not logged as a drop.")]
        [SerializeField] private bool onlyAfterFirstGrab = true;

        [Tooltip("Ignore gentle contacts below this impact speed (m/s). Filters an item being set down carefully.")]
        [SerializeField] private float minImpactSpeed = 0.3f;

        private readonly List<IInteractableView> _views = new List<IInteractableView>();
        private int _selectCount;
        private string _itemName;
        private bool _everGrabbed;
        private float _lastFloorLogTime = -999f;
        private int _dropCount;

        /// <summary>How many times this item has hit the floor this session.</summary>
        public int DropCount => _dropCount;

        /// <summary>
        /// The name this item is logged under. Read by GazeObjectTracker so that
        /// gaze rows use exactly the same key as interaction and backpack rows.
        /// </summary>
        public string ItemName
        {
            get
            {
                if (string.IsNullOrEmpty(_itemName))
                    _itemName = ResolveItemName();

                return _itemName;
            }
        }

        /// <summary>Whether this item is tagged for the bag-collection measurement (Inspector-set, default false).</summary>
        public bool BagCollectionMeasurement => bagCollectionMeasurement;

        /// <summary>Whether this item is tagged for the initiative measurement (Inspector-set, default false).</summary>
        public bool InitiativeMeasurement => initiativeMeasurement;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            _itemName = ResolveItemName();

            foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is IInteractableView view)
                {
                    _views.Add(view);
                }
            }

            if (_views.Count == 0)
            {
                Debug.LogWarning($"[{nameof(ItemUsageTracker)}] No IInteractableView found under '{name}'. Nothing will be logged for this item.", this);
            }

            if (trackFloorContact)
            {
                if (floorLayers.value == 0)
                {
                    Debug.LogWarning($"[{nameof(ItemUsageTracker)}] '{name}': Track Floor Contact is on but Floor Layers is empty - no drops will be logged.", this);
                }

                // Collision callbacks are delivered to the Rigidbody's GameObject.
                // If the body sits on a parent, this component never hears them.
                Rigidbody body = GetComponent<Rigidbody>();
                if (body == null)
                {
                    Rigidbody parentBody = GetComponentInParent<Rigidbody>();
                    Debug.LogWarning($"[{nameof(ItemUsageTracker)}] '{name}': no Rigidbody on this GameObject"
                        + (parentBody != null ? $" (it is on '{parentBody.name}')" : "")
                        + " - floor contacts will not be detected. Move this component onto the Rigidbody object.", this);
                }
            }
        }

        private void OnEnable()
        {
            _selectCount = 0;

            // Registered here rather than Awake: Unity does not guarantee this
            // runs after ExperimentDataManager.Awake (which sets Instance), but
            // it does run all Awakes before any OnEnable for scene objects.
            Manager?.SetItemMeasurements(_itemName, bagCollectionMeasurement, initiativeMeasurement);

            foreach (IInteractableView view in _views)
            {
                view.WhenStateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            foreach (IInteractableView view in _views)
            {
                view.WhenStateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(InteractableStateChangeArgs args)
        {
            bool wasSelect = args.PreviousState == InteractableState.Select;
            bool isSelect = args.NewState == InteractableState.Select;

            if (isSelect && !wasSelect)
            {
                _selectCount++;
                if (_selectCount == 1)
                {
                    _everGrabbed = true;
                    Manager?.LogSelect(_itemName);
                }
            }
            else if (wasSelect && !isSelect)
            {
                _selectCount = Mathf.Max(0, _selectCount - 1);
                if (_selectCount == 0)
                {
                    Manager?.LogUnselect(_itemName);
                }
            }

            if (!trackHover) return;
        }

        /// <summary>
        /// Floor contact. Unity delivers this to the GameObject carrying the
        /// Rigidbody, which is why Awake warns when that is not this object.
        /// Note that a held item is usually kinematic and generates no collision
        /// events at all - what gets logged is the moment after release.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!trackFloorContact) return;
            if (floorLayers.value == 0) return;
            if (onlyAfterFirstGrab && !_everGrabbed) return;
            if (Time.time - _lastFloorLogTime < floorCooldown) return;

            // Is the thing we hit on a floor layer?
            if ((floorLayers.value & (1 << collision.gameObject.layer)) == 0) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < minImpactSpeed) return;

            _lastFloorLogTime = Time.time;
            _dropCount++;

            bool wasHeld = _selectCount > 0;

            Manager?.LogItemDropped(ItemName, impactSpeed,
                $"surface={collision.gameObject.name}"
                + $"|speed_ms={impactSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"
                + $"|still_held={(wasHeld ? 1 : 0)}"
                + $"|drop_number={_dropCount}");
        }

        private string ResolveItemName()
        {
            if (!string.IsNullOrWhiteSpace(overrideItemName))
            {
                return overrideItemName;
            }

            // Keep names identical to the backpack's item_in / item_out rows.
            var bridge = GetComponentInChildren<CognitiveVR.Interaction.InventoryItemMetaBridge>(true);
            if (bridge != null && !string.IsNullOrWhiteSpace(bridge.ItemName))
            {
                return bridge.ItemName;
            }

            return gameObject.name;
        }
    }
}