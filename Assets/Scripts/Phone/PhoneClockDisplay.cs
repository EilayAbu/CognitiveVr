using CognitiveVR.Core;
using TMPro;
using UnityEngine;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Drives a TextMeshPro label with the wall-clock time published by the
    /// existing SessionTimer (which already starts at 08:52 and progresses in
    /// real time). Format is HH:MM only. Updates once per second to avoid
    /// allocating a new string every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneClockDisplay : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private SessionTimer _sessionTimer;
        [SerializeField] private TMP_Text _label;

        [Header("Behaviour")]
        [Tooltip("If true, also displays seconds (HH:MM:SS).")]
        [SerializeField] private bool _showSeconds;
        [Tooltip("Refresh rate in seconds. Setting > 0 prevents per-frame string allocations.")]
        [SerializeField] private float _refreshIntervalSeconds = 0.5f;

        private float _nextRefreshAt;
        private string _lastWritten;

        public void SetTarget(TMP_Text label)
        {
            _label = label;
        }

        private void OnEnable()
        {
            ResolveSessionTimer();
            _nextRefreshAt = 0f;
            _lastWritten = null;
        }

        private void Update()
        {
            if (_label == null) return;
            if (_sessionTimer == null)
            {
                ResolveSessionTimer();
                if (_sessionTimer == null) return;
            }

            if (Time.unscaledTime < _nextRefreshAt) return;
            _nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, _refreshIntervalSeconds);

            string text = FormatNow();
            if (text == _lastWritten) return;

            _lastWritten = text;
            _label.text = text;
        }

        private string FormatNow()
        {
            var (h, m, s) = _sessionTimer.WallClockTime;
            return _showSeconds
                ? $"{h:D2}:{m:D2}:{s:D2}"
                : $"{h:D2}:{m:D2}";
        }

        private void ResolveSessionTimer()
        {
            if (_sessionTimer != null) return;
#if UNITY_2023_1_OR_NEWER
            _sessionTimer = FindFirstObjectByType<SessionTimer>();
#else
            _sessionTimer = FindObjectOfType<SessionTimer>();
#endif
        }
    }
}
