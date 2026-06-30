using UnityEngine;

// Put on a NON-kinematic Rigidbody (visual + collider).
// Assign 'target' = the transform the Meta Grabbable moves.
// Body chases target through physics, so it collides with the world.
[RequireComponent(typeof(Rigidbody))]
public class PhysicsFollow : MonoBehaviour
{
    public Transform target;
    public float positionStrength = 30f;
    public float rotationStrength = 30f;
    public float maxSpeed = 8f;

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false;
    }

    void FixedUpdate()
    {
        if (!target) return;

        Vector3 vel = (target.position - rb.position) * positionStrength;
        if (vel.magnitude > maxSpeed) vel = vel.normalized * maxSpeed;
        rb.linearVelocity = vel;

        Quaternion d = target.rotation * Quaternion.Inverse(rb.rotation);
        d.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (Mathf.Abs(angle) > 0.01f && axis.sqrMagnitude > 0.001f)
            rb.angularVelocity = axis.normalized * (angle * Mathf.Deg2Rad) * rotationStrength;
        else
            rb.angularVelocity = Vector3.zero;
    }
}