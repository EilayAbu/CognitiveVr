using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on the key sitting on the high shelf.
/// When an UmbrellaStriker trigger touches it (fast enough), the key flies
/// in a fixed parabolic arc to landingPoint and becomes grabbable again on touchdown.
/// Deterministic landing — no physics randomness.
/// </summary>
public class KnockableKey : MonoBehaviour
{
    [Header("Landing")]
    [Tooltip("Empty GameObject marking exactly where the key should land.")]
    public Transform landingPoint;

    [Header("Flight")]
    public float flightDuration = 0.8f;
    [Tooltip("Extra height of the arc above the straight start->end line, in meters.")]
    public float arcHeight = 0.6f;
    [Tooltip("Cosmetic tumble while flying, degrees/sec per axis.")]
    public Vector3 tumbleSpeed = new Vector3(240f, 90f, 0f);

    [Header("On landing")]
    [Tooltip("Turn OFF if your ISDK setup keeps the key kinematic until grabbed.")]
    public bool enablePhysicsOnLand = true;

    [Header("Optional")]
    public AudioSource hitSound;

    Rigidbody _rb;
    Collider[] _cols;
    bool _launched;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _cols = GetComponentsInChildren<Collider>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (_launched) return;

        var striker = other.GetComponentInParent<UmbrellaStriker>();
        if (striker == null || !striker.IsFastEnough) return;

        _launched = true;
        StartCoroutine(FlyToLanding());
    }

    /// <summary>
    /// Wire this to the key's PointableUnityEventWrapper -> OnSelect so that
    /// grabbing it directly (stool route) permanently disarms the knock-off.
    /// </summary>
    public void Disarm() => _launched = true;

    IEnumerator FlyToLanding()
    {
        if (hitSound != null) hitSound.Play();

        // Freeze physics + collisions so nothing deflects the arc mid-flight,
        // and so the key can't be grabbed while airborne.
        if (_rb != null) _rb.isKinematic = true;
        foreach (var c in _cols) c.enabled = false;

        Vector3 start = transform.position;
        Vector3 end = landingPoint.position;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / flightDuration;
            float k = Mathf.Clamp01(t);

            Vector3 pos = Vector3.Lerp(start, end, k);
            pos.y += arcHeight * 4f * k * (1f - k); // simple parabola
            transform.position = pos;
            transform.Rotate(tumbleSpeed * Time.deltaTime, Space.Self);

            yield return null;
        }

        transform.SetPositionAndRotation(end, landingPoint.rotation);

        foreach (var c in _cols) c.enabled = true;

        if (_rb != null)
        {
            if (enablePhysicsOnLand)
            {
                _rb.isKinematic = false;
                _rb.linearVelocity = Vector3.zero;   // Unity 6 API; use .velocity on older versions
                _rb.angularVelocity = Vector3.zero;
            }
            // else: stays kinematic at the landing point, ready for ISDK grab
        }
    }
}
