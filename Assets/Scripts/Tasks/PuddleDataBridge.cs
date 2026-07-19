using System.Globalization;
using UnityEngine;
using CognitiveVR.Data;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Pipes PuddleCleaner activity into the experiment CSV. Purely a
    /// subscriber: it attaches runtime listeners to the puddle's existing
    /// UnityEvents, so PuddleCleaner is not modified and any listeners you
    /// already wired in the Inspector keep working untouched.
    ///
    /// Drop it on the same GameObject as the PuddleCleaner.
    ///
    /// Logged under category "task":
    ///   puddle_wipe    - each successful wipe. value = wipe number,
    ///                    details carry percent cleaned and time since first wipe.
    ///   puddle_cleaned - puddle fully gone. value = seconds from first wipe to last.
    ///   puddle_reset   - ResetPuddle() brought a finished puddle back.
    /// </summary>
    [RequireComponent(typeof(PuddleCleaner))]
    public class PuddleDataBridge : MonoBehaviour
    {
        [Tooltip("Name used in the 'object' column. Keep it identical to the puddle's gaze name so the rows join in the summary.")]
        [SerializeField] private string logName = "Puddle";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private PuddleCleaner _puddle;
        private int _wipes;
        private float _firstWipeTime = -1f;
        private bool _completed;

        private static ExperimentDataManager Manager => ExperimentDataManager.Instance;

        private void Awake()
        {
            _puddle = GetComponent<PuddleCleaner>();
        }

        private void OnEnable()
        {
            // The puddle deactivates itself when cleaned, so coming back enabled
            // means ResetPuddle() ran.
            if (_completed)
            {
                Manager?.Log("task", "puddle_reset", logName, null, null);
                _wipes = 0;
                _firstWipeTime = -1f;
                _completed = false;
            }

            _puddle.onWipe.AddListener(HandleWipe);
            _puddle.onCleaned.AddListener(HandleCleaned);
        }

        private void OnDisable()
        {
            _puddle.onWipe.RemoveListener(HandleWipe);
            _puddle.onCleaned.RemoveListener(HandleCleaned);
        }

        private void HandleWipe()
        {
            _wipes++;

            if (_firstWipeTime < 0f)
                _firstWipeTime = Time.time;

            int total = Mathf.Max(1, _puddle.wipesToClean);
            float percent = Mathf.Clamp01((float)_wipes / total) * 100f;
            float sinceFirst = Time.time - _firstWipeTime;

            Manager?.Log("task", "puddle_wipe", logName, _wipes,
                $"cleaned_pct={percent.ToString("F0", Inv)}" +
                $"|of={total}" +
                $"|since_first_wipe_s={sinceFirst.ToString("F2", Inv)}");
        }

        private void HandleCleaned()
        {
            _completed = true;

            float duration = _firstWipeTime >= 0f ? Time.time - _firstWipeTime : 0f;

            Manager?.Log("task", "puddle_cleaned", logName, duration,
                $"wipes={_wipes}|duration_s={duration.ToString("F2", Inv)}");
        }
    }
}
