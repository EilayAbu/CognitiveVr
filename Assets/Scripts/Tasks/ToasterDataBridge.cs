using System.Globalization;
using UnityEngine;
using CognitiveVR.Data;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Pipes ToasterController activity into the experiment CSV. Purely a
    /// subscriber - it never drives the toaster, only reads it. Drop it on the
    /// same GameObject as the ToasterController; no Inspector wiring needed.
    ///
    /// Logged under category "task":
    ///   toaster_state  - every state transition, value = cooking elapsed.
    ///                    The Done row carries the final burn severity.
    ///   toaster_power  - power on/off
    ///   toaster_lid    - lid opened / closed
    ///   toast_inserted - toast placed inside the toaster
    ///   toast_removed  - toast taken out (temporary removal, not finalization)
    ///
    /// Lid and toast presence are polled rather than event-driven, because the
    /// controller exposes them as UnityEvents only. Both are simple bool reads.
    /// </summary>
    [RequireComponent(typeof(ToasterController))]
    public class ToasterDataBridge : MonoBehaviour
    {
        [Tooltip("Name used in the 'object' column. Keep it identical to the toaster's gaze name so the rows join in the summary.")]
        [SerializeField] private string logName = "Toaster";

        [Tooltip("Log lid open/close transitions.")]
        [SerializeField] private bool trackLid = true;

        [Tooltip("Log toast inserted/removed transitions.")]
        [SerializeField] private bool trackToastPresence = true;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private ToasterController _toaster;

        private bool _lidWasOpen;
        private bool _toastWasInside;
        private bool _initialized;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            _toaster = GetComponent<ToasterController>();
        }

        private void OnEnable()
        {
            _toaster.OnStateChanged += HandleStateChanged;
            _toaster.OnPowerChanged += HandlePowerChanged;
        }

        private void OnDisable()
        {
            _toaster.OnStateChanged -= HandleStateChanged;
            _toaster.OnPowerChanged -= HandlePowerChanged;
        }

        private void Update()
        {
            bool lidOpen = _toaster.IsLidOpen();
            bool toastInside = _toaster.ToastInside;

            // First frame just captures the baseline - no phantom rows at startup.
            if (!_initialized)
            {
                _lidWasOpen = lidOpen;
                _toastWasInside = toastInside;
                _initialized = true;
                return;
            }

            if (trackLid && lidOpen != _lidWasOpen)
            {
                _lidWasOpen = lidOpen;
                Manager?.Log("task", "toaster_lid", logName, _toaster.CookingElapsed,
                    lidOpen ? "lid=open" : "lid=closed");
            }

            if (trackToastPresence && toastInside != _toastWasInside)
            {
                _toastWasInside = toastInside;
                Manager?.Log("task", toastInside ? "toast_inserted" : "toast_removed",
                    logName, _toaster.CookingElapsed,
                    $"state={_toaster.State}|lid={(lidOpen ? "open" : "closed")}");
            }
        }

        private void HandleStateChanged(ToasterState state)
        {
            string details = $"state={state}|powered={(_toaster.IsPoweredOn ? 1 : 0)}";

            // The finalizing transition carries the outcome measure.
            if (state == ToasterState.Done)
            {
                details += $"|severity={_toaster.CurrentBurnSeverity}" +
                           $"|cook_s={_toaster.CookingElapsed.ToString("F2", Inv)}";
            }

            Manager?.Log("task", "toaster_state", logName, _toaster.CookingElapsed, details);
        }

        private void HandlePowerChanged(bool on)
        {
            Manager?.Log("task", "toaster_power", logName, _toaster.CookingElapsed,
                on ? "power=on" : "power=off");
        }
    }
}