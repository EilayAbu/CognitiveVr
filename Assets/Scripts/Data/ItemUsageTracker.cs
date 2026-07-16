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

        private readonly List<IInteractableView> _views = new List<IInteractableView>();
        private int _selectCount;
        private int _hoverCount;
        private string _itemName;

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
        }

        private void OnEnable()
        {
            _selectCount = 0;
            _hoverCount = 0;

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

            // "Hovering" = the hand is on/near the item, so Select counts too.
            bool wasHovering = wasSelect || args.PreviousState == InteractableState.Hover;
            bool isHovering = isSelect || args.NewState == InteractableState.Hover;

            if (isHovering && !wasHovering)
            {
                _hoverCount++;
                if (_hoverCount == 1)
                {
                    Manager?.LogHoverEnter(_itemName);
                }
            }
            else if (wasHovering && !isHovering)
            {
                _hoverCount = Mathf.Max(0, _hoverCount - 1);
                if (_hoverCount == 0)
                {
                    Manager?.LogHoverExit(_itemName);
                }
            }
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
