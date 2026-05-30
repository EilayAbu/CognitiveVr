using System;
using UnityEngine;
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

        [Header("Smoke Particle System")]
        [SerializeField] private ParticleSystem _smokeParticles;

        [Header("Timing")]
        [Tooltip("Seconds from activation until toast is ready")]
        [SerializeField] private float _cookTime = 30f;
        [Tooltip("Seconds from activation until smoke stage 2 (medium)")]
        [SerializeField] private float _smokeStage2Time = 60f;
        [Tooltip("Seconds from activation until smoke stage 3 (heavy) and toast is burnt")]
        [SerializeField] private float _smokeStage3Time = 90f;

        [Header("Smoke Settings - Stage 1 (Light)")]
        [SerializeField] private float _stage1Emission = 5f;
        [SerializeField] private float _stage1StartSize = 0.03f;
        [SerializeField] private float _stage1Lifetime = 2f;
        [SerializeField] private Color _stage1Color = new Color(0.75f, 0.75f, 0.75f, 0.3f);

        [Header("Smoke Settings - Stage 2 (Medium)")]
        [SerializeField] private float _stage2Emission = 15f;
        [SerializeField] private float _stage2StartSize = 0.06f;
        [SerializeField] private float _stage2Lifetime = 3f;
        [SerializeField] private Color _stage2Color = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        [Header("Smoke Settings - Stage 3 (Heavy)")]
        [SerializeField] private float _stage3Emission = 30f;
        [SerializeField] private float _stage3StartSize = 0.1f;
        [SerializeField] private float _stage3Lifetime = 4f;
        [SerializeField] private Color _stage3Color = new Color(0.25f, 0.25f, 0.25f, 0.7f);

        [Header("Runtime (Read Only)")]
        [SerializeField] private ToasterState _state = ToasterState.Idle;
        [SerializeField] private float _cookingElapsed;
        [SerializeField] private int _currentSmokeStage;

        private bool _lidWasOpen = true;
        private bool _toastInside;
        private GameObject _toastObject;
        private ToasterMetrics _metrics;

        public ToasterState State => _state;
        public float CookingElapsed => _cookingElapsed;
        public int CurrentSmokeStage => _currentSmokeStage;
        public bool IsPoweredOn => _isPoweredOn;

        public event Action<ToasterState> OnStateChanged;
        public event Action<int> OnSmokeStageChanged;
        public event Action<bool> OnPowerChanged;

        private void Awake()
        {
            _metrics = new ToasterMetrics();
        }

        private void Start()
        {
            SetToastVisibility(true, false, false);
            StopSmoke();
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
                UpdateSmokeStage();
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
            _currentSmokeStage = 0;

            SetToastVisibility(true, false, false);
            StopSmoke();
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
        /// cooking, the toaster is reset to Idle and smoke is stopped.
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
                StopSmoke();
                _state = ToasterState.Idle;
                _cookingElapsed = 0f;
                _currentSmokeStage = 0;
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

            StopSmoke();

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

        /// <summary>
        /// Assign the smoke particle system at runtime (used by ToasterSmokeSetup).
        /// </summary>
        public void SetSmokeParticles(ParticleSystem ps)
        {
            _smokeParticles = ps;
        }

        public void ResetToaster()
        {
            _state = ToasterState.Idle;
            _cookingElapsed = 0f;
            _currentSmokeStage = 0;
            _lidWasOpen = IsLidOpen();
            _toastInside = false;
            _toastObject = null;
            _metrics = new ToasterMetrics();

            SetToastVisibility(true, false, false);
            StopSmoke();
            UpdateIndicatorLights();

            if (_cognitiveTask != null)
                _cognitiveTask.ResetTask();

            OnStateChanged?.Invoke(_state);
        }

        #region Cooking State Machine

        private void UpdateCookingState()
        {
            if (_state == ToasterState.Done) return;

            if (_cookingElapsed >= _smokeStage3Time && _state != ToasterState.Burnt)
            {
                _state = ToasterState.Burnt;
                SetToastVisibility(false, false, true);
                UpdateIndicatorLights();
                OnStateChanged?.Invoke(_state);
            }
            else if (_cookingElapsed >= _smokeStage2Time && _state < ToasterState.Overcooked)
            {
                _state = ToasterState.Overcooked;
                UpdateIndicatorLights();
                OnStateChanged?.Invoke(_state);
            }
            else if (_cookingElapsed >= _cookTime && _state < ToasterState.Ready)
            {
                _state = ToasterState.Ready;
                SetToastVisibility(false, true, false);
                UpdateIndicatorLights();

                float now = GetSessionTime();
                _metrics.RecordToastReady(now);

                OnStateChanged?.Invoke(_state);
            }
        }

        #endregion

        #region Smoke Control

        private void UpdateSmokeStage()
        {
            int newStage = 0;

            if (_cookingElapsed >= _smokeStage3Time)
                newStage = 3;
            else if (_cookingElapsed >= _smokeStage2Time)
                newStage = 2;
            else if (_cookingElapsed >= _cookTime)
                newStage = 1;

            if (newStage != _currentSmokeStage)
            {
                _currentSmokeStage = newStage;
                ApplySmokeStage(newStage);

                if (newStage == 1)
                {
                    float now = GetSessionTime();
                    _metrics.RecordSmokeStarted(now);
                }

                OnSmokeStageChanged?.Invoke(newStage);
            }
        }

        private void ApplySmokeStage(int stage)
        {
            if (_smokeParticles == null) return;

            if (stage <= 0)
            {
                StopSmoke();
                return;
            }

            float emission, startSize, lifetime;
            Color color;

            switch (stage)
            {
                case 1:
                    emission = _stage1Emission;
                    startSize = _stage1StartSize;
                    lifetime = _stage1Lifetime;
                    color = _stage1Color;
                    break;
                case 2:
                    emission = _stage2Emission;
                    startSize = _stage2StartSize;
                    lifetime = _stage2Lifetime;
                    color = _stage2Color;
                    break;
                default:
                    emission = _stage3Emission;
                    startSize = _stage3StartSize;
                    lifetime = _stage3Lifetime;
                    color = _stage3Color;
                    break;
            }

            var emissionModule = _smokeParticles.emission;
            emissionModule.rateOverTime = emission;

            var mainModule = _smokeParticles.main;
            mainModule.startSize = startSize;
            mainModule.startLifetime = lifetime;
            mainModule.startColor = color;

            if (!_smokeParticles.isPlaying)
                _smokeParticles.Play();
        }

        private void StopSmoke()
        {
            if (_smokeParticles == null) return;

            _smokeParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _currentSmokeStage = 0;
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
            if (_cookingElapsed >= _smokeStage3Time)
                return BurnSeverity.Burnt;
            if (_cookingElapsed >= _smokeStage2Time)
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
