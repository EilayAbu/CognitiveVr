using System.Collections;
using UnityEngine;

public class PhoneRinger : MonoBehaviour
{
    [Header("Angle Settings")]
    [Tooltip("The resting position on the X axis in degrees.")]
    [SerializeField] private float baseXAngle = 90f;

    [Tooltip("How many degrees off the base angle to swing (1 degree swings between 89 and 91).")]
    [SerializeField] private float swingAmount = 1f;

    [Header("Timing Settings")]
    [Tooltip("How long the ringing lasts (in seconds).")]
    [SerializeField] private float defaultDuration = 1.5f;

    [Tooltip("How fast it vibrates back and forth.")]
    [SerializeField] private float defaultSpeed = 50f;

    private Coroutine _ringCoroutine;
    private Vector3 _originalEuler;

    private void Awake()
    {
        // Store current Y and Z rotators so we don't accidentally overwrite them
        _originalEuler = transform.localEulerAngles;
    }

    /// <summary>
    /// Call this function to trigger a ring with default settings (89° to 91°).
    /// </summary>
    public void Ring()
    {
        Ring(baseXAngle, swingAmount, defaultDuration, defaultSpeed);
    }

    /// <summary>
    /// Call this from code to customize the base angle, swing range, duration, or speed.
    /// </summary>
    public void Ring(float centerAngle, float angleSwing, float duration, float speed)
    {
        if (_ringCoroutine != null)
        {
            StopCoroutine(_ringCoroutine);
        }

        _ringCoroutine = StartCoroutine(RingRoutine(centerAngle, angleSwing, duration, speed));
    }

    private IEnumerator RingRoutine(float centerAngle, float angleSwing, float duration, float speed)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // Oscillates between (centerAngle - angleSwing) and (centerAngle + angleSwing)
            // e.g. 90 + (-1 to +1) * 1 = 89 to 91 degrees
            float currentX = centerAngle + Mathf.Sin(elapsed * speed) * angleSwing;

            // Apply rotation around X while preserving original Y and Z rotations
            transform.localRotation = Quaternion.Euler(currentX, _originalEuler.y, _originalEuler.z);

            yield return null;
        }

        // Return to exact resting base angle when complete
        transform.localRotation = Quaternion.Euler(centerAngle, _originalEuler.y, _originalEuler.z);
        _ringCoroutine = null;
    }
}