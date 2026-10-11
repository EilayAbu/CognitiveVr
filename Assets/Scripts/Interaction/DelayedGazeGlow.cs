using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Delayed gaze relay.
    ///
    /// Watches the player's gaze on a "trigger" object (A). Once the player has
    /// continuously looked at A for <see cref="_requiredGazeSeconds"/>, schedules
    /// a one-shot timer that, after <see cref="_delaySeconds"/> seconds, enables
    /// the <see cref="GazeGlow"/> on a "target" object (B) and on every extra
    /// hint target (e.g. the umbrella).
    ///
    /// If a <see cref="ChairNudge"/> is assigned, after a further
    /// <see cref="_nudgeDelaySeconds"/> it wiggles the chair every
    /// <see cref="_nudgeRepeatSeconds"/> until the hints are stopped.
    ///
    /// When any hinted object is grabbed (Oculus.Interaction <see cref="Grabbable"/>
    /// raises a <see cref="PointerEventType.Select"/>), or <see cref="StopHints"/>
    /// is called (e.g. the task was solved), every glow is disabled, the wiggle
    /// stops and this relay turns itself off.
    /// </summary>
    [DisallowMultipleComponent]
    public class DelayedGazeGlow : MonoBehaviour
    {
        [Serializable]
        public class HintTarget
        {
            [Tooltip("GazeGlow on the hinted object. Disabled on Awake, enabled together with the main target.")]
            public GazeGlow glow;

            [Tooltip("Optional: Grabbable on the hinted object. If empty, auto-discovered on the glow's GameObject (children first, then parents).")]
            public Grabbable grabbable;
        }

        [Header("Detection (Trigger - object A)")]
        [Tooltip("Optional override. Leave empty to auto-use Camera.main transform.")]
        [SerializeField] private Transform _headTransformOverride;

        [Tooltip("Object A: the player has to look at this object to start the relay.")]
        [SerializeField] private Transform _triggerObject;

        [Tooltip("Looking-at angle (degrees from camera forward) to count gaze on A.")]
        [Range(0f, 90f)]
        [SerializeField] private float _enterAngleDegrees = 12f;

        [Tooltip("Looking-away angle (must be >= enter angle) before gaze on A is dropped.")]
        [Range(0f, 90f)]
        [SerializeField] private float _exitAngleDegrees = 18f;

        [SerializeField] private bool _useTriggerDistanceLimit = true;

        [Tooltip("Maximum distance from the camera at which gaze on A counts.")]
        [SerializeField] private float _triggerMaxDistance = 5f;

        [Tooltip("Minimum continuous gaze (in seconds) on A before the delay timer starts.")]
        [SerializeField] private float _requiredGazeSeconds = 1f;

        [Header("Delay")]
        [Tooltip("Seconds to wait after A is registered before enabling B's GazeGlow.")]
        [SerializeField] private float _delaySeconds = 120f;

        [Header("Target (object B)")]
        [Tooltip("GazeGlow on object B. It will be disabled on Awake and enabled after the delay elapses.")]
        [SerializeField] private GazeGlow _targetGazeGlow;

        [Tooltip("Optional: Grabbable on B. If empty, auto-discovered via GetComponentInChildren on the target GazeGlow's GameObject.")]
        [SerializeField] private Grabbable _targetGrabbable;

        [Header("Extra hint targets")]
        [Tooltip("Other objects that glow at the same moment as B (e.g. the umbrella). Grabbing any of them also stops the hints.")]
        [SerializeField] private List<HintTarget> _extraTargets = new List<HintTarget>();

        [Header("Nudge (after the glow)")]
        [Tooltip("Optional: wiggles the chair if the glow hints didn't help.")]
        [SerializeField] private ChairNudge _nudge;

        [Tooltip("Seconds after the glows turn on before the first wiggle.")]
        [SerializeField] private float _nudgeDelaySeconds = 60f;

        [Tooltip("Seconds between wiggles.")]
        [SerializeField] private float _nudgeRepeatSeconds = 20f;

        [Tooltip("Maximum number of wiggles. 0 = unlimited.")]
        [SerializeField] private int _maxNudges;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs;

        /// <summary>The player looked at A long enough. Arg = head-to-A distance (m).</summary>
        public event Action<float> TriggerGazeRegistered;
        /// <summary>The delay elapsed and every hint glow was turned on.</summary>
        public event Action HintGlowsEnabled;
        /// <summary>The chair was wiggled. Arg = wiggle count so far.</summary>
        public event Action<int> ChairNudged;
        /// <summary>The hints were stopped. Arg = reason ("grabbed:&lt;name&gt;", "solved", ...).</summary>
        public event Action<string> HintsStopped;

        private readonly List<(Grabbable grabbable, Action<PointerEvent> handler)> _subscriptions =
            new List<(Grabbable, Action<PointerEvent>)>();

        private Transform _cachedHead;
        private float _continuousGazeTime;
        private float _lastTriggerDistance;
        private bool _gazeRegistered;
        private bool _delayScheduled;
        private bool _glowsEnabled;
        private bool _stopped;
        private int _nudgeCount;
        private Coroutine _hintRoutine;

        public bool IsGazeRegistered => _gazeRegistered;
        public bool IsTargetEnabled => _targetGazeGlow != null && _targetGazeGlow.enabled;
        public bool AreGlowsEnabled => _glowsEnabled;
        public int NudgeCount => _nudgeCount;

        private void Awake()
        {
            if (_targetGazeGlow != null)
            {
                _targetGazeGlow.enabled = false;
            }

            if (_targetGrabbable == null && _targetGazeGlow != null)
            {
                _targetGrabbable = _targetGazeGlow.GetComponentInChildren<Grabbable>();
            }

            foreach (HintTarget target in _extraTargets)
            {
                if (target == null || target.glow == null)
                    continue;

                target.glow.enabled = false;

                if (target.grabbable == null)
                {
                    target.grabbable = target.glow.GetComponentInChildren<Grabbable>();
                    if (target.grabbable == null)
                        target.grabbable = target.glow.GetComponentInParent<Grabbable>();
                }
            }

            if (_targetGazeGlow == null && _verboseLogs)
            {
                Debug.LogWarning(
                    $"[{nameof(DelayedGazeGlow)}] No target GazeGlow assigned on {name}. Relay will be inert.",
                    this);
            }

            ResolveHead();
        }

        private void OnEnable()
        {
            Subscribe(_targetGrabbable);

            foreach (HintTarget target in _extraTargets)
            {
                if (target != null)
                    Subscribe(target.grabbable);
            }
        }

        private void OnDisable()
        {
            foreach (var subscription in _subscriptions)
            {
                if (subscription.grabbable != null)
                    subscription.grabbable.WhenPointerEventRaised -= subscription.handler;
            }

            _subscriptions.Clear();
        }

        private void OnValidate()
        {
            if (_exitAngleDegrees < _enterAngleDegrees)
                _exitAngleDegrees = _enterAngleDegrees;

            if (_requiredGazeSeconds < 0f)
                _requiredGazeSeconds = 0f;

            if (_delaySeconds < 0f)
                _delaySeconds = 0f;

            if (_triggerMaxDistance < 0.05f)
                _triggerMaxDistance = 0.05f;

            if (_nudgeDelaySeconds < 0f)
                _nudgeDelaySeconds = 0f;

            if (_nudgeRepeatSeconds < 0.1f)
                _nudgeRepeatSeconds = 0.1f;

            if (_maxNudges < 0)
                _maxNudges = 0;
        }

        private void Update()
        {
            if (_stopped || _gazeRegistered)
                return;

            UpdateTriggerGaze();
        }

        private void UpdateTriggerGaze()
        {
            Transform head = ResolveHead();
            if (head == null || _triggerObject == null)
            {
                _continuousGazeTime = 0f;
                return;
            }

            Vector3 toObject = _triggerObject.position - head.position;
            float sqr = toObject.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                _continuousGazeTime = 0f;
                return;
            }

            if (_useTriggerDistanceLimit && sqr > _triggerMaxDistance * _triggerMaxDistance)
            {
                _continuousGazeTime = 0f;
                return;
            }

            float angle = Vector3.Angle(head.forward, toObject);

            // Hysteresis identical to GazeGlow: once we've started accumulating
            // gaze, allow a wider exit cone so micro-jitter doesn't reset us.
            float threshold = _continuousGazeTime > 0f ? _exitAngleDegrees : _enterAngleDegrees;
            bool looking = angle <= threshold;

            if (looking)
            {
                _continuousGazeTime += Time.deltaTime;
                _lastTriggerDistance = Mathf.Sqrt(sqr);
                if (_continuousGazeTime >= _requiredGazeSeconds)
                {
                    RegisterTriggerGaze();
                }
            }
            else
            {
                _continuousGazeTime = 0f;
            }
        }

        private void RegisterTriggerGaze()
        {
            if (_gazeRegistered)
                return;

            _gazeRegistered = true;

            Debug.Log(
                $"[{nameof(DelayedGazeGlow)}] First look at '{(_triggerObject != null ? _triggerObject.name : "<null>")}' " +
                $"at {_lastTriggerDistance:0.##}m. Hints in {_delaySeconds:0.##}s.",
                this);

            TriggerGazeRegistered?.Invoke(_lastTriggerDistance);

            if (!_delayScheduled)
            {
                _delayScheduled = true;
                _hintRoutine = StartCoroutine(RunHintSequence());
            }
        }

        private IEnumerator RunHintSequence()
        {
            if (_delaySeconds > 0f)
            {
                yield return new WaitForSeconds(_delaySeconds);
            }

            if (_stopped)
                yield break;

            SetGlows(true);
            _glowsEnabled = true;

            if (_verboseLogs)
            {
                Debug.Log($"[{nameof(DelayedGazeGlow)}] Delay elapsed -> enabled hint glows.", this);
            }

            HintGlowsEnabled?.Invoke();

            if (_nudge == null)
                yield break;

            if (_nudgeDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(_nudgeDelaySeconds);
            }

            while (!_stopped && (_maxNudges <= 0 || _nudgeCount < _maxNudges))
            {
                if (_nudge.Nudge())
                {
                    _nudgeCount++;

                    if (_verboseLogs)
                    {
                        Debug.Log($"[{nameof(DelayedGazeGlow)}] Chair nudge #{_nudgeCount}.", this);
                    }

                    ChairNudged?.Invoke(_nudgeCount);
                }

                yield return new WaitForSeconds(_nudgeRepeatSeconds);
            }
        }

        /// <summary>
        /// Ends the hint sequence: turns off every glow, stops the wiggle and
        /// disables this relay. Safe to call more than once.
        /// </summary>
        public void StopHints(string reason)
        {
            if (_stopped)
                return;

            _stopped = true;

            if (_hintRoutine != null)
            {
                StopCoroutine(_hintRoutine);
                _hintRoutine = null;
            }

            SetGlows(false);

            if (_nudge != null)
            {
                _nudge.Cancel();
            }

            if (_verboseLogs)
            {
                Debug.Log($"[{nameof(DelayedGazeGlow)}] Hints stopped ({reason}).", this);
            }

            HintsStopped?.Invoke(reason);

            enabled = false;
        }

        private void SetGlows(bool on)
        {
            if (_targetGazeGlow != null)
                _targetGazeGlow.enabled = on;

            foreach (HintTarget target in _extraTargets)
            {
                if (target != null && target.glow != null)
                    target.glow.enabled = on;
            }
        }

        private void Subscribe(Grabbable grabbable)
        {
            if (grabbable == null)
                return;

            foreach (var subscription in _subscriptions)
            {
                if (subscription.grabbable == grabbable)
                    return;
            }

            Action<PointerEvent> handler = pointerEvent => HandleTargetPointerEvent(pointerEvent, grabbable);
            grabbable.WhenPointerEventRaised += handler;
            _subscriptions.Add((grabbable, handler));
        }

        private void HandleTargetPointerEvent(PointerEvent pointerEvent, Grabbable grabbable)
        {
            if (pointerEvent.Type != PointerEventType.Select)
                return;

            StopHints($"grabbed:{(grabbable != null ? grabbable.name : "<null>")}");
        }

        private Transform ResolveHead()
        {
            if (_headTransformOverride != null)
            {
                _cachedHead = _headTransformOverride;
                return _cachedHead;
            }

            if (_cachedHead != null)
                return _cachedHead;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _cachedHead = mainCamera.transform;
            }

            return _cachedHead;
        }
    }
}
