using UnityEngine;

namespace VRLocomotion
{
    [RequireComponent(typeof(HandMovementAnalyzer))]
    public class VRWalkLocomotion : MonoBehaviour
    {
        [SerializeField] private float baseWalkSpeed = 2f;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private float smoothing = 3f;
        [SerializeField] private float accelerationTime = 0.5f;

        private HandMovementAnalyzer movementAnalyzer;
        private Vector3 currentVelocity;
        private Vector3 targetVelocity;
        private float currentSpeedMultiplier;

        private void Start()
        {
            movementAnalyzer = GetComponent<HandMovementAnalyzer>();
        }

        private void Update()
        {
            if (movementAnalyzer == null || playerTransform == null) return;
            UpdateMovement();
        }

        private void UpdateMovement()
        {
            float targetSpeedMultiplier = 0f;

            if (movementAnalyzer.IsWalking)
            {
                targetSpeedMultiplier = Mathf.Clamp(movementAnalyzer.MovementIntensity, 0.2f, 1f);
            }

            currentSpeedMultiplier = Mathf.Lerp(currentSpeedMultiplier, targetSpeedMultiplier,
                Time.deltaTime / accelerationTime);

            targetVelocity = movementAnalyzer.MovementDirection * baseWalkSpeed * currentSpeedMultiplier;
            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime * smoothing);

            if (currentVelocity.magnitude > 0.001f)
            {
                playerTransform.position += currentVelocity * Time.deltaTime;
            }
        }
    }
}