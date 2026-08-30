using UnityEngine;

/// <summary>Which held object a striking end belongs to, for the logged data.</summary>
public enum StrikerToolType { Umbrella, Mop, Other }

/// <summary>
/// Marker component for a long object's hitting ends (umbrella, mop, ...).
/// Setup: create two small empty children on the object (Tip, HandleEnd),
/// give each a SphereCollider with isTrigger = true (radius ~0.03-0.05),
/// and add this component. The object root's existing Rigidbody (from its
/// grab setup) is enough to make trigger events fire — no other changes needed.
/// </summary>
public class UmbrellaStriker : MonoBehaviour
{
    [Tooltip("Minimum speed (m/s) this end must be moving for a hit to count. 0 = any touch counts.")]
    public float minHitSpeed = 0.5f;

    [Tooltip("Which object this striking end belongs to. Logged so the data shows what the key was hit with.")]
    public StrikerToolType tool = StrikerToolType.Umbrella;

    [Tooltip("Used only when tool = Other. Free text written to the log.")]
    public string customToolName = "";

    /// <summary>Name of the striking object as written to the CSV / JSON.</summary>
    public string ToolName => tool switch
    {
        StrikerToolType.Mop => "mop",
        StrikerToolType.Other => string.IsNullOrWhiteSpace(customToolName) ? "other" : customToolName.Trim(),
        _ => "umbrella",
    };

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
