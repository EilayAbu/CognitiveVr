using UnityEngine;

namespace VRLocomotion
{
    public class HandMovementAnalyzer : MonoBehaviour
    {
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Camera xrCamera;

        [Header("Movement Detection Parameters")]
        [SerializeField] private float minMovementMagnitude = 0.02f;
        [SerializeField] private float phaseShiftThreshold = 0.3f;
        [SerializeField] private float walkDetectionTime = 0.1f;
        [SerializeField] private float headSmoothingSpeed = 5f;
        [SerializeField] private float naturalSwingThreshold = 0.7f;
        [SerializeField] private float forwardBackwardRatio = 0.6f;

        public bool IsWalking { get; private set; }
        public Vector3 MovementDirection { get; private set; }
        public float MovementIntensity { get; private set; }

        private Vector3 smoothedDirection;
        private float walkTimer;
        private Vector3 lastCameraForward;
        private float currentMovementIntensity;

        private Vector3[] leftHandBuffer = new Vector3[5];
        private Vector3[] rightHandBuffer = new Vector3[5];
        private int bufferIndex = 0;

        private void Start()
        {
            InitializeBuffers();
            if (xrCamera != null)
            {
                lastCameraForward = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up).normalized;
            }
        }

        private void InitializeBuffers()
        {
            for (int i = 0; i < leftHandBuffer.Length; i++)
            {
                leftHandBuffer[i] = leftHand.position;
                rightHandBuffer[i] = rightHand.position;
            }
        }

        private void Update()
        {
            if (leftHand == null || rightHand == null || xrCamera == null) return;

            UpdateHandBuffers();
            UpdateMovementDirection();
            CheckWalking();
        }

        private void UpdateHandBuffers()
        {
            leftHandBuffer[bufferIndex] = leftHand.position;
            rightHandBuffer[bufferIndex] = rightHand.position;
            bufferIndex = (bufferIndex + 1) % leftHandBuffer.Length;
        }

        private void UpdateMovementDirection()
        {
            Vector3 cameraForward = xrCamera.transform.forward;
            Vector3 flatForward = Vector3.ProjectOnPlane(cameraForward, Vector3.up).normalized;

            if (IsWalking)
            {
                lastCameraForward = flatForward;
            }

            smoothedDirection = Vector3.Lerp(smoothedDirection, lastCameraForward, Time.deltaTime * headSmoothingSpeed);
            MovementDirection = smoothedDirection;
        }

        private void CheckWalking()
        {
            Vector3 leftMove = AnalyzeHandMovement(leftHandBuffer);
            Vector3 rightMove = AnalyzeHandMovement(rightHandBuffer);

            Vector3 forwardVector = xrCamera.transform.forward;
            Vector3 rightVector = xrCamera.transform.right;

            Vector3 leftForwardComponent = Vector3.Project(leftMove, forwardVector);
            Vector3 leftSideComponent = Vector3.Project(leftMove, rightVector);
            Vector3 rightForwardComponent = Vector3.Project(rightMove, forwardVector);
            Vector3 rightSideComponent = Vector3.Project(rightMove, rightVector);

            float leftForwardRatio = leftForwardComponent.magnitude / (leftMove.magnitude + 0.001f);
            float rightForwardRatio = rightForwardComponent.magnitude / (rightMove.magnitude + 0.001f);

            bool hasNaturalSwing = leftForwardRatio > forwardBackwardRatio &&
                                 rightForwardRatio > forwardBackwardRatio;

            float dot = Vector3.Dot(leftForwardComponent.normalized, rightForwardComponent.normalized);
            bool antiPhase = dot < -phaseShiftThreshold;

            bool hasMinMovement = leftMove.magnitude > minMovementMagnitude &&
                                rightMove.magnitude > minMovementMagnitude;

            bool naturalMovement = hasNaturalSwing &&
                                 Mathf.Abs(leftSideComponent.magnitude - rightSideComponent.magnitude) < naturalSwingThreshold;

            bool walkingDetected = hasMinMovement && antiPhase && naturalMovement;

            float targetIntensity = walkingDetected ?
                Mathf.Clamp01((leftMove.magnitude + rightMove.magnitude) * 1.5f) : 0f;

            currentMovementIntensity = Mathf.Lerp(currentMovementIntensity, targetIntensity, Time.deltaTime * 3f);
            MovementIntensity = currentMovementIntensity;

            if (walkingDetected)
            {
                walkTimer += Time.deltaTime;
                if (walkTimer >= walkDetectionTime && !IsWalking)
                {
                    IsWalking = true;
                }
            }
            else
            {
                walkTimer = 0f;
                if (IsWalking)
                {
                    IsWalking = false;
                }
            }
        }

        private Vector3 AnalyzeHandMovement(Vector3[] buffer)
        {
            Vector3 avgMovement = Vector3.zero;
            for (int i = 0; i < buffer.Length - 1; i++)
            {
                avgMovement += buffer[(bufferIndex + i + 1) % buffer.Length] -
                              buffer[(bufferIndex + i) % buffer.Length];
            }
            return avgMovement / (buffer.Length - 1);
        }
    }
}