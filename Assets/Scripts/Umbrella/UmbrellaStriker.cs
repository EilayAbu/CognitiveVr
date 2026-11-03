using System.Collections.Generic;
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

    static readonly HashSet<UmbrellaStriker> s_active = new HashSet<UmbrellaStriker>();
    static readonly List<Collider> s_noPhysicsColliders = new List<Collider>();

    Vector3 _lastPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_active.Clear();
        s_noPhysicsColliders.Clear();
    }

    /// <summary>
    /// Every tool's colliders stop physically colliding with these colliders, so a
    /// tool can never push them — scripted motion only. Re-applied whenever a tool
    /// is re-enabled, because Unity resets IgnoreCollision on deactivation.
    /// The caller must call this again after re-enabling its own colliders.
    /// </summary>
    public static void IgnorePhysicsWith(Collider[] colliders)
    {
        if (colliders == null) return;

        foreach (var c in colliders)
            if (c != null && !s_noPhysicsColliders.Contains(c))
                s_noPhysicsColliders.Add(c);

        foreach (var striker in s_active)
            striker.ApplyIgnore(colliders);
    }

    void OnEnable()
    {
        _lastPos = transform.position;

        s_active.Add(this);
        s_noPhysicsColliders.RemoveAll(c => c == null);
        ApplyIgnore(s_noPhysicsColliders);
    }

    void OnDisable()
    {
        s_active.Remove(this);
    }

    void FixedUpdate()
    {
        Velocity = (transform.position - _lastPos) / Time.fixedDeltaTime;
        _lastPos = transform.position;
    }

    public bool IsFastEnough => minHitSpeed <= 0f || Velocity.magnitude >= minHitSpeed;

    void ApplyIgnore(IReadOnlyList<Collider> others)
    {
        if (others.Count == 0) return;

        // Never fall back to transform.root: it could be a scene container whose
        // colliders include the shelf/table, which would make the key fall through.
        var rb = GetComponentInParent<Rigidbody>();
        Transform toolRoot = rb != null ? rb.transform : (transform.parent != null ? transform.parent : transform);

        foreach (var mine in toolRoot.GetComponentsInChildren<Collider>())
        {
            if (!mine.enabled) continue;

            foreach (var other in others)
            {
                if (other == null || other == mine) continue;
                if (!other.enabled || !other.gameObject.activeInHierarchy) continue;
                Physics.IgnoreCollision(mine, other, true);
            }
        }
    }
}
