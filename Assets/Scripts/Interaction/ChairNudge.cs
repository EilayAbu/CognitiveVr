using System.Collections;
using CognitiveVR.Tasks;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Small attention-grabbing wiggle in place: a decaying side-to-side tilt
    /// with tiny hops, after which the object returns to its exact starting pose.
    ///
    /// Place on the chair root (next to <see cref="ChairGrabState"/>,
    /// <see cref="Grabbable"/> and the Rigidbody). Driven by
    /// <see cref="DelayedGazeGlow"/>, or call <see cref="Nudge"/> directly.
    ///
    /// The Rigidbody is made kinematic while wiggling and its original flags are
    /// restored afterwards. Never wiggles while the object is held; if it gets
    /// grabbed mid-wiggle, the wiggle aborts and leaves the pose to the hand.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChairNudge : MonoBehaviour
    {
        [Header("References (auto-filled from this GameObject when empty)")]
        [SerializeField] private Transform _target;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private ChairGrabState _grabState;
        [SerializeField] private Grabbable _grabbable;

        [Header("Wiggle")]
        [SerializeField] private float _durationSeconds = 0.6f;

        [Tooltip("Peak side-to-side tilt, in degrees.")]
        [SerializeField] private float _tiltDegrees = 5f;

        [Tooltip("Peak hop height, in meters.")]
        [SerializeField] private float _hopHeight = 0.02f;

        [Tooltip("Number of left-right swings per wiggle.")]
        [SerializeField] private int _swings = 3;

        [Header("Optional")]
        [SerializeField] private AudioSource _sound;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs;

        private Coroutine _routine;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private bool _originalIsKinematic;
        private bool _rigidbodyOverridden;

        public bool IsNudging => _routine != null;

        private bool IsHeld =>
            (_grabState != null && _grabState.IsHeld) ||
            (_grabbable != null && _grabbable.SelectingPointsCount > 0);

        private void Awake()
        {
            if (_target == null) _target = transform;
            if (_rigidbody == null) _rigidbody = _target.GetComponent<Rigidbody>();
            if (_grabState == null) _grabState = _target.GetComponentInParent<ChairGrabState>();
            if (_grabbable == null) _grabbable = _target.GetComponentInChildren<Grabbable>();
        }

        private void OnValidate()
        {
            if (_durationSeconds < 0.05f) _durationSeconds = 0.05f;
            if (_swings < 1) _swings = 1;
        }

        private void OnDisable()
        {
            Cancel();
        }

        /// <summary>
        /// Starts one wiggle. Returns false (and does nothing) if the object is
        /// held, a wiggle is already running, or this component is inactive.
        /// </summary>
        public bool Nudge()
        {
            if (!isActiveAndEnabled || _target == null || IsNudging || IsHeld)
                return false;

            _routine = StartCoroutine(WiggleRoutine());
            return true;
        }

        /// <summary>Stops a running wiggle, restoring the start pose unless the object is held.</summary>
        public void Cancel()
        {
            if (_routine == null)
                return;

            StopCoroutine(_routine);
            _routine = null;
            Finish(restorePose: !IsHeld);
        }

        private IEnumerator WiggleRoutine()
        {
            _startPosition = _target.position;
            _startRotation = _target.rotation;

            if (_rigidbody != null)
            {
                _originalIsKinematic = _rigidbody.isKinematic;
                _rigidbody.isKinematic = true;
                _rigidbodyOverridden = true;
            }

            if (_sound != null) _sound.Play();

            if (_verboseLogs)
            {
                Debug.Log($"[{nameof(ChairNudge)}] Wiggle on '{_target.name}'.", this);
            }

            float t = 0f;
            while (t < 1f)
            {
                if (IsHeld)
                {
                    _routine = null;
                    Finish(restorePose: false);
                    yield break;
                }

                t += Time.deltaTime / _durationSeconds;
                float k = Mathf.Clamp01(t);
                float envelope = 1f - k;
                float phase = k * _swings * Mathf.PI;

                float tilt = Mathf.Sin(phase * 2f) * _tiltDegrees * envelope;
                float hop = Mathf.Abs(Mathf.Sin(phase)) * _hopHeight * envelope;

                _target.SetPositionAndRotation(
                    _startPosition + Vector3.up * hop,
                    _startRotation * Quaternion.AngleAxis(tilt, Vector3.forward));

                yield return null;
            }

            _routine = null;
            Finish(restorePose: true);
        }

        private void Finish(bool restorePose)
        {
            if (restorePose && _target != null)
            {
                _target.SetPositionAndRotation(_startPosition, _startRotation);
            }

            if (_rigidbodyOverridden && _rigidbody != null)
            {
                // A grab may have taken over the kinematic flag; leave it alone then.
                if (!IsHeld)
                {
                    _rigidbody.isKinematic = _originalIsKinematic;
                    if (!_rigidbody.isKinematic)
                    {
                        _rigidbody.linearVelocity = Vector3.zero;
                        _rigidbody.angularVelocity = Vector3.zero;
                    }
                }
            }

            _rigidbodyOverridden = false;
        }
    }
}
