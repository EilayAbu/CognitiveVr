using UnityEngine;
using CognitiveVR.Data;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Pipes LunchBoxController activity into the experiment CSV. Drop it on the
    /// lunch box, next to the LunchBoxController.
    ///
    /// Logged under category "task":
    ///   box_sealed   - toast placed, lid on, box grabbable. Details record
    ///                  whether a ToasterController was wired up, which is the
    ///                  difference between "the toaster task finalized" and
    ///                  "the box sealed but the toaster never heard about it".
    ///   box_unsealed - session ended with the box never sealed (logged once on
    ///                  disable), so a missing seal is explicit in the data
    ///                  rather than just an absent row.
    /// </summary>
    [RequireComponent(typeof(LunchBoxController))]
    public class LunchBoxDataBridge : MonoBehaviour
    {
        [Tooltip("Name used in the 'object' column. Match it to the box's gaze name so the rows join in the summary.")]
        [SerializeField] private string logName = "LunchBox";

        private LunchBoxController _box;
        private bool _sealed;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            _box = GetComponent<LunchBoxController>();
        }

        private void OnEnable()
        {
            _box.OnBoxSealed += HandleSealed;
        }

        private void OnDisable()
        {
            _box.OnBoxSealed -= HandleSealed;

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
                $"toast={toastName}|toaster_wired={(toasterWired ? 1 : 0)}");
        }
    }
}
