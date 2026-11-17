using System.Globalization;
using UnityEngine;
using CognitiveVR.Data;
using CognitiveVR.Interaction;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Pipes LunchBoxController activity into the experiment CSV. Drop it on the
    /// lunch box, next to the LunchBoxController.
    ///
    /// Logged under category "task":
    ///   box_sealed          - toast placed, lid on, box grabbable. Details record
    ///                         whether a ToasterController was wired up, which is the
    ///                         difference between "the toaster task finalized" and
    ///                         "the box sealed but the toaster never heard about it".
    ///                         sealed_by = release | drop_in | external (spatula).
    ///   box_unsealed        - session ended with the box never sealed (logged once on
    ///                         disable), so a missing seal is explicit in the data
    ///                         rather than just an absent row.
    ///   lunchbox_in_bag     - the box was snapped into the backpack. value = 1 if it
    ///                         had the toast in it, 0 if it went in EMPTY.
    ///                         details: has_toast | toast_state (toaster state when
    ///                         the toast was sealed in) | cook_s | in_count.
    ///   lunchbox_out_of_bag - the box was taken back out of the backpack.
    /// </summary>
    [RequireComponent(typeof(LunchBoxController))]
    public class LunchBoxDataBridge : MonoBehaviour
    {
        [Tooltip("Name used in the 'object' column. Match it to the box's gaze name so the rows join in the summary.")]
        [SerializeField] private string logName = "LunchBox";

        [Tooltip("Name the backpack reports for this box (InventoryItemMetaBridge = GameObject name).")]
        [SerializeField] private string bagItemName = "foodBox";

        [Tooltip("Backpack zone. Auto-found if empty.")]
        [SerializeField] private BackpackInventoryZone backpack;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private LunchBoxController _box;
        private bool _sealed;
        private int _bagInCount;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            _box = GetComponent<LunchBoxController>();
            if (backpack == null)
                backpack = FindFirstObjectByType<BackpackInventoryZone>();
        }

        private void OnEnable()
        {
            _box.OnBoxSealed += HandleSealed;

            if (backpack != null)
            {
                backpack.WhenItemEntered += HandleBagEntered;
                backpack.WhenItemExited += HandleBagExited;
            }
        }

        private void OnDisable()
        {
            _box.OnBoxSealed -= HandleSealed;

            if (backpack != null)
            {
                backpack.WhenItemEntered -= HandleBagEntered;
                backpack.WhenItemExited -= HandleBagExited;
            }

            if (!_sealed)
            {
                Manager?.Log("task", "box_unsealed", logName, null,
                    $"toast_tracked={(_box.HasToastTracked ? 1 : 0)}");
            }
        }

        private void HandleSealed(string toastName, bool toasterWired)
        {
            _sealed = true;

            Manager?.Log("task", "box_sealed", logName, null,
                $"toast={toastName}|toaster_wired={(toasterWired ? 1 : 0)}" +
                $"|sealed_by={_box.SealSource}" +
                (string.IsNullOrEmpty(_box.ToastStateAtSeal) ? "" : $"|toast_state={_box.ToastStateAtSeal}") +
                (_box.ToastCookSecondsAtSeal >= 0f ? $"|cook_s={_box.ToastCookSecondsAtSeal.ToString("F1", Inv)}" : ""));
        }

        private void HandleBagEntered(string itemName, BackpackSlot slot)
        {
            if (itemName != bagItemName) return;

            _bagInCount++;
            bool hasToast = _box.IsClosed;

            Manager?.Log("task", "lunchbox_in_bag", logName, hasToast ? 1f : 0f,
                $"has_toast={(hasToast ? 1 : 0)}" +
                (hasToast && !string.IsNullOrEmpty(_box.ToastStateAtSeal) ? $"|toast_state={_box.ToastStateAtSeal}" : "") +
                (hasToast && _box.ToastCookSecondsAtSeal >= 0f ? $"|cook_s={_box.ToastCookSecondsAtSeal.ToString("F1", Inv)}" : "") +
                $"|in_count={_bagInCount}");
        }

        private void HandleBagExited(string itemName, BackpackSlot slot)
        {
            if (itemName != bagItemName) return;

            Manager?.Log("task", "lunchbox_out_of_bag", logName, null,
                $"has_toast={(_box.IsClosed ? 1 : 0)}");
        }
    }
}
