using UnityEngine;

/// <summary>
/// Marker component for the umbrella's hitting ends.
/// Setup: create two small empty children on the umbrella (Tip, HandleEnd),
/// give each a SphereCollider with isTrigger = true (radius ~0.03-0.05),
/// and add this component. The umbrella root's existing Rigidbody (from its
/// grab setup) is enough to make trigger events fire — no other changes needed.
/// </summary>
public class UmbrellaStriker : MonoBehaviour
{
    [Tooltip("Minimum speed (m/s) this end must be moving for a hit to count. 0 = any touch counts.")]
    public float minHitSpeed = 0.5f;

    public Vector3 Velocity { get; private set; }

    Vector3 _lastPos;

    void OnEnable()
    {
        _lastPos = transform.position;
    }

    void FixedUpdate()
    {
        Velocity = (transform.position - _lastPos) / Time.fixedDeltaTime;
        _lastPos = transform.position;
    }

    public bool IsFastEnough => minHitSpeed <= 0f || Velocity.magnitude >= minHitSpeed;
}
