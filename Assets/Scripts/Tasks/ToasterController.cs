using System;
using UnityEngine;
using UnityEngine.Events;
using CognitiveVR.Models;

namespace CognitiveVR.Tasks
{
    public enum ToasterState
    {
        Idle,
        Cooking,
        Ready,
        Overcooked,
        Burnt,
        Done
    }

    public enum BurnSeverity
    {
        Perfect = 0,
        Overcooked = 1,
        Burnt = 2
    }

    public class ToasterController : MonoBehaviour
    {
        [Header("Task Integration")]
        [SerializeField] private CognitiveTask _cognitiveTask;

        [Header("Lid (upperTust)")]
        [SerializeField] private Transform _upperTust;
        [Tooltip("Z-rotation angle at or above which the lid counts as closed")]
        [SerializeField] private float _lidClosedAngle = 80f;
        [Tooltip("Minimum allowed lid angle (degrees)")]
        [SerializeField] private float _minLidAngle = 47f;
        [Tooltip("Maximum allowed lid angle (degrees)")]
        [SerializeField] private float _maxLidAngle = 90f;

        [Header("Toast Objects")]
        [SerializeField] private GameObject _freshToast;
        [SerializeField] private GameObject _readyToast;
        [SerializeField] private GameObject _burntToast;

        [Header("Power & Indicator Lights")]
        [Tooltip("Whether the toaster is powered on. Required for Activate() to proceed.")]
        [SerializeField] private bool _isPoweredOn;
        [Tooltip("Indicator GameObject that is enabled while the toaster is powered on.")]
        [SerializeField] private GameObject _powerOnLight;
        [Tooltip("Indicator GameObject that is enabled when the toast is ready.")]
        [SerializeField] private GameObject _readyLight;
        [Tooltip("Indicator GameObject that is enabled when the toast is overcooked or burnt.")]
        [SerializeField] private GameObject _burntLight;

        [Header("Timing")]
        [Tooltip("Seconds from activation until toast is ready")]
        [SerializeField] private float _cookTime = 30f;
        [Tooltip("Seconds from activation until well done")]
        [SerializeField] private float _wellDoneTime = 60f;
        [Tooltip("Seconds from activation until toast is burnt")]
        [SerializeField] private float _burnTime = 90f;

        [Header("Stage Events")]
        [Tooltip("Fired once when toast is ready (elapsed >= _cookTime)")]
        [SerializeField] private UnityEvent _onToastReady;
        [Tooltip("Fired once when toast is well done (elapsed >= _wellDoneTime)")]
        [SerializeField] private UnityEvent _onWellDone;
        [Tooltip("Fired once when toast is burnt (elapsed >= _burnTime)")]
        [SerializeField] private UnityEvent _onToastBurnt;

        [Header("Runtime (Read Only)")]
        [SerializeField] private ToasterState _state = ToasterState.Idle;
        [SerializeField] private float _cookingElapsed;

        private bool _lidWasOpen = true;
        private bool _toastInside;
        private GameObject _toastObject;
        private ToasterMetrics _metrics;

        public ToasterState State => _state;
        public float CookingElapsed => _cookingElapsed;
        public bool IsPoweredOn => _isPoweredOn;

        public event Action<ToasterState> OnStateChanged;
        public event Action<bool> OnPowerChanged;

        private void Awake()
        {
            _metrics = new ToasterMetrics();
        }

        private void Start()
        {
            SetToastVisibility(true, false, false);
            UpdateIndicatorLights();
            _lidWasOpen = IsLidOpen();
        }

        private void Update()
        {
            if (_state == ToasterState.Done)
                return;

            bool lidOpen = IsLidOpen();
            bool lidJustOpened = lidOpen && !_lidWasOpen;
            _lidWasOpen = lidOpen;

            if (_state == ToasterState.Idle)
            {
                if (_toastInside && !lidOpen && _isPoweredOn)
                    Activate();
                return;
            }

            if (_state >= ToasterState.Cooking && _state <= ToasterState.Burnt)
            {
                _cookingElapsed += Time.deltaTime;
                UpdateCookingState();
            }

            if (lidJustOpened && _state >= ToasterState.Cooking)
                HandleLidOpened();
        }

        private void LateUpdate()
        {
            ClampLidRotation();
        }

        public bool IsLidOpen()
        {
            if (_upperTust == null) return true;
            float angle = Mathf.Abs(_upperTust.localEulerAngles.z);
            if (angle > 180f) angle = 360f - angle;
            return angle < _lidClosedAngle;
        }

        #region Toast Trigger Zone

        public void NotifyToastEntered(GameObject toast)
        {
            _toastInside = true;
            _toastObject = toast;
        }

        public void NotifyToastExited()
        {
            _toastInside = false;
            _toastObject = null;

            if (_state >= ToasterState.Ready && _state < ToasterState.Done)
                RemoveToast();
        }

        #endregion

        /// <summary>
        /// Called automatically when toast is inside and lid closes,
        /// or can be called manually from external logic.
        /// </summary>
        public void Activate()
        {
            if (_state != ToasterState.Idle) return;
            if (!_isPoweredOn) return;

            _state = ToasterState.Cooking;
            _cookingElapsed = 0f;

            SetToastVisibility(true, false, false);
            UpdateIndicatorLights();

            float now = GetSessionTime();
            _metrics.RecordActivation(now);

            if (_cognitiveTask != null)
            {
                _cognitiveTask.StartTask("Toaster activated");
                _cognitiveTask.CompleteStep("activate_toaster", "Player activated toaster");
            }

            OnStateChanged?.Invoke(_state);

            if (_cognitiveTask != null)
                _cognitiveTask.ReportProgress("Cooking started");
        }

        /// <summary>
        /// Sets the toaster power state. Hook this to the button's WhenSelect UnityEvent
        /// (with a boolean argument) for separate on/off buttons. If turned off while
        /// cooking, the toaster is reset to Idle.
        /// </summary>
        public void SetPower(bool on)
        {
            if (_isPoweredOn == on)
            {
                UpdateIndicatorLights();
                return;
            }

            _isPoweredOn = on;

            if (!on && _state >= ToasterState.Cooking && _state < ToasterState.Done)
            {
                _state = ToasterState.Idle;
                _cookingElapsed = 0f;
                SetToastVisibility(true, false, false);
                OnStateChanged?.Invoke(_state);
            }

            UpdateIndicatorLights();
            OnPowerChanged?.Invoke(_isPoweredOn);
        }

        /// <summary>
        /// Toggles the toaster power on/off. Hook this to the button's WhenSelect
        /// UnityEvent for a single physical toggle button.
        /// </summary>
        public void TogglePower()
        {
            SetPower(!_isPoweredOn);
        }

        /// <summary>
        /// Call when the player grabs/removes the toast from the toaster.
        /// </summary>
        public void RemoveToast()
        {
            if (_state < ToasterState.Ready || _state == ToasterState.Done)
                return;

            float now = GetSessionTime();
            BurnSeverity severity = GetCurrentBurnSeverity();

            _metrics.RecordToastRemoved(now, _cookingElapsed, _cookTime, severity);

            _state = ToasterState.Done;
            UpdateIndicatorLights();

            if (_cognitiveTask != null)
            {
                _cognitiveTask.CompleteStep("remove_toast",
                    $"Toast removed at {_cookingElapsed:F1}s, severity={severity}");
                _cognitiveTask.CompleteTask($"Burn severity: {severity}");
            }

            OnStateChanged?.Invoke(_state);
        }

        public ToasterMetrics GetMetrics() => _metrics;

        public void ResetToaster()
        {
            _state = ToasterState.Idle;
            _cookingElapsed = 0f;
            _lidWasOpen = IsLidOpen();
            _toastInside = false;
            _toastObject = null;
            _metrics = new ToasterMetrics();

            SetToastVisibility(true, false, false);
            UpdateIndicatorLights();

            if (_cognitiveTask != null)
                _cognitiveTask.ResetTask();

            OnStateChanged?.Invoke(_state);
        }

        #region Cooking State Machine

        private void UpdateCookingState()
        {
            if (_state == ToasterState.Done) return;

            if (_cookingElapsed >= _burnTime && _state != ToasterState.Burnt)
            {
                _state = ToasterState.Burnt;
                SetToastVisibility(false, false, true);
                UpdateIndicatorLights();
                _onToastBurnt?.Invoke();
                OnStateChanged?.Invoke(_state);
            }
            else if (_cookingElapsed >= _wellDoneTime && _state < ToasterState.Overcooked)
            {
                _state = ToasterState.Overcooked;
                UpdateIndicatorLights();
                _onWellDone?.Invoke();
                OnStateChanged?.Invoke(_state);
            }
            else if (_cookingElapsed >= _cookTime && _state < ToasterState.Ready)
            {
                _state = ToasterState.Ready;
                SetToastVisibility(false, true, false);
                UpdateIndicatorLights();

                float now = GetSessionTime();
                _metrics.RecordToastReady(now);

                _onToastReady?.Invoke();
                OnStateChanged?.Invoke(_state);
            }
        }

        #endregion

        #region Lid Events

        private void HandleLidOpened()
        {
            float now = GetSessionTime();
            bool isFirstCheck = !_metrics.HasChecked;
            _metrics.RecordLidOpened(now);

            if (_cognitiveTask != null)
            {
                if (isFirstCheck)
                    _cognitiveTask.CompleteStep("check_toaster", "First toaster check");
                else
                    _cognitiveTask.ReportProgress($"Toaster check #{_metrics.LidOpenCount}");
            }
        }

        #endregion

        #region Toast Visibility

        private void SetToastVisibility(bool fresh, bool ready, bool burnt)
        {
            if (_freshToast != null) _freshToast.SetActive(fresh);
            if (_readyToast != null) _readyToast.SetActive(ready);
            if (_burntToast != null) _burntToast.SetActive(burnt);
        }

        private void UpdateIndicatorLights()
        {
            if (_powerOnLight != null)
                _powerOnLight.SetActive(_isPoweredOn);

            if (_readyLight != null)
                _readyLight.SetActive(_state == ToasterState.Ready);

            if (_burntLight != null)
                _burntLight.SetActive(_state == ToasterState.Overcooked
                                      || _state == ToasterState.Burnt);
        }

        #endregion

        #region Helpers

        private void ClampLidRotation()
        {
            if (_upperTust == null) return;

            Vector3 euler = _upperTust.localEulerAngles;
            float z = euler.z;
            if (z > 180f) z -= 360f;

            float clamped = Mathf.Clamp(z, _minLidAngle, _maxLidAngle);
            if (!Mathf.Approximately(z, clamped))
            {
                euler.z = clamped;
                _upperTust.localEulerAngles = euler;
            }
        }

        private BurnSeverity GetCurrentBurnSeverity()
        {
            if (_cookingElapsed >= _burnTime)
                return BurnSeverity.Burnt;
            if (_cookingElapsed >= _wellDoneTime)
                return BurnSeverity.Overcooked;
            return BurnSeverity.Perfect;
        }

        private float GetSessionTime()
        {
            return TaskController.Instance.GetSessionTime();
        }

        #endregion
    }
}
