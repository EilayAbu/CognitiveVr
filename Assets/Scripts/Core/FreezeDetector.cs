using System;
using UnityEngine;

namespace CognitiveVR.Core
{
    public class FreezeDetector : MonoBehaviour
    {
        [Header("Tracking Targets")]
        [Tooltip("XR Camera / Head transform")]
        public Transform HeadTransform;
        [Tooltip("Left hand controller transform")]
        public Transform LeftHand;
        [Tooltip("Right hand controller transform")]
        public Transform RightHand;

        [Header("Detection Settings")]
        [Tooltip("Seconds without meaningful movement to count as frozen")]
        public float FreezeThreshold = 3f;
        [Tooltip("Minimum position delta per frame to count as movement")]
        public float MovementEpsilon = 0.01f;
        [Tooltip("Minimum rotation delta (degrees) per frame to count as movement")]
        public float RotationEpsilon = 1f;

        [Header("Runtime State")]
        [SerializeField] private bool _isFrozen;
        [SerializeField] private float _currentIdleTime;
        [SerializeField] private float _totalFreezeTime;
        [SerializeField] private int _freezeCount;

        public bool IsFrozen => _isFrozen;
        public float CurrentIdleTime => _currentIdleTime;
        public float TotalFreezeTime => _totalFreezeTime;
        public int FreezeCount => _freezeCount;

        /// <summary>
        /// Fired when a freeze is detected (idle time exceeded threshold).
        /// Parameters: freeze duration so far, player position at freeze start.
        /// </summary>
        public event Action<float, Vector3> OnFreezeStarted;

        /// <summary>
        /// Fired when the player resumes movement after a freeze.
        /// Parameters: total freeze duration, position where freeze occurred.
        /// </summary>
        public event Action<float, Vector3> OnFreezeEnded;

        private Vector3 _lastHeadPos;
        private Quaternion _lastHeadRot;
        private Vector3 _lastLeftPos;
        private Vector3 _lastRightPos;
        private Vector3 _freezeStartPosition;
        private bool _wasTrackingLastFrame;

        private void LateUpdate()
        {
            if (HeadTransform == null) return;

            bool hasMoved = CheckForMovement();

            if (hasMoved)
            {
                if (_isFrozen)
                {
                    OnFreezeEnded?.Invoke(_currentIdleTime, _freezeStartPosition);
                }
                _currentIdleTime = 0f;
                _isFrozen = false;
            }
            else
            {
                _currentIdleTime += Time.deltaTime;

                if (!_isFrozen && _currentIdleTime >= FreezeThreshold)
                {
                    _isFrozen = true;
                    _freezeCount++;
                    _freezeStartPosition = HeadTransform.position;
                    OnFreezeStarted?.Invoke(_currentIdleTime, _freezeStartPosition);
                }

                if (_isFrozen)
                {
                    _totalFreezeTime += Time.deltaTime;
                }
            }

            StoreCurrentPositions();
        }

        private bool CheckForMovement()
        {
            if (!_wasTrackingLastFrame)
            {
                _wasTrackingLastFrame = true;
                StoreCurrentPositions();
                return true;
            }

            float headPosDelta = Vector3.Distance(HeadTransform.position, _lastHeadPos);
            float headRotDelta = Quaternion.Angle(HeadTransform.rotation, _lastHeadRot);

            if (headPosDelta > MovementEpsilon || headRotDelta > RotationEpsilon)
                return true;

            if (LeftHand != null)
            {
                float leftDelta = Vector3.Distance(LeftHand.position, _lastLeftPos);
                if (leftDelta > MovementEpsilon) return true;
            }

            if (RightHand != null)
            {
                float rightDelta = Vector3.Distance(RightHand.position, _lastRightPos);
                if (rightDelta > MovementEpsilon) return true;
            }

            return false;
        }

        private void StoreCurrentPositions()
        {
            if (HeadTransform != null)
            {
                _lastHeadPos = HeadTransform.position;
                _lastHeadRot = HeadTransform.rotation;
            }
            if (LeftHand != null) _lastLeftPos = LeftHand.position;
            if (RightHand != null) _lastRightPos = RightHand.position;
        }

        public void ResetTracking()
        {
            _isFrozen = false;
            _currentIdleTime = 0f;
            _totalFreezeTime = 0f;
            _freezeCount = 0;
            _wasTrackingLastFrame = false;
        }
    }
}
