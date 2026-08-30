using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on a trigger-collider GameObject placed AROUND the key on the shelf.
/// Two ways to clear the key, both consume the zone (it destroys itself afterward):
///   1) Tool route: an UmbrellaStriker end (umbrella, mop, ...) enters the zone
///      fast enough -> the key flies in a fixed parabolic arc to landingPoint,
///      then the zone is gone. The tool that hit is reported with the event.
///   2) Stool route: the player climbs and grabs the key directly; the key leaves
///      the zone -> the zone is gone (no flight).
/// The key itself needs no script — just assign it in the Inspector.
/// </summary>
[RequireComponent(typeof(Collider))]
public class KeyKnockZone : MonoBehaviour
{
    /// <summary>
    /// Tool hit fast enough - the key is flying down.
    /// Args = hit speed (m/s), tool name ("umbrella", "mop", ...).
    /// </summary>
    public event System.Action<float, string> KeyKnockedByUmbrella;
    /// <summary>
    /// A tool end touched the zone but was too slow to count.
    /// Args = hit speed (m/s), tool name ("umbrella", "mop", ...).
    /// </summary>
    public event System.Action<float, string> UmbrellaHitTooSlow;
    /// <summary>The key was taken directly by hand (stool route), no tool involved.</summary>
    public event System.Action KeyTakenByHand;

    /// <summary>Tool name of the last end that touched the zone, slow or not. Empty until then.</summary>
    public string LastHitToolName { get; private set; } = "";

    [Header("Refs")]
    [Tooltip("The key sitting on the shelf, inside this zone.")]
    public Transform key;
    [Tooltip("Empty GameObject marking exactly where the key should land (the table).")]
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

    Rigidbody _keyRb;
    Collider[] _keyCols;
    bool _consumed;

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;

        if (key != null)
        {
            _keyRb = key.GetComponent<Rigidbody>();
            _keyCols = key.GetComponentsInChildren<Collider>();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_consumed) return;

        // Tool route (umbrella, mop, ...)
        var striker = other.GetComponentInParent<UmbrellaStriker>();
        if (striker != null)
        {
            float speed = striker.Velocity.magnitude;
            string tool = striker.ToolName;
            LastHitToolName = tool;

            if (striker.IsFastEnough)
            {
                _consumed = true;
                KeyKnockedByUmbrella?.Invoke(speed, tool);
                StartCoroutine(FlyThenDestroy());
            }
            else
            {
                UmbrellaHitTooSlow?.Invoke(speed, tool);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (_consumed) return;
        if (key == null) return;

        // Stool route: the key itself has left the zone (grabbed & lifted out).
        if (other.transform == key || other.transform.IsChildOf(key))
        {
            _consumed = true;
            KeyTakenByHand?.Invoke();
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Optional backup for the stool route: wire the key's
    /// PointableUnityEventWrapper -> OnSelect to this, in case your grab setup
    /// doesn't physically move the key out of the zone on grab.
    /// </summary>
    public void DisarmAndDestroy()
    {
        if (_consumed) return;
        _consumed = true;
        KeyTakenByHand?.Invoke();
        Destroy(gameObject);
    }

    IEnumerator FlyThenDestroy()
    {
        if (hitSound != null) hitSound.Play();

        // Freeze physics + collisions so nothing deflects the arc mid-flight,
        // and so the key can't be grabbed while airborne.
        // (Disabling the key's colliders fires our own OnTriggerExit for the key,
        //  but _consumed is already true, so it's safely ignored.)
        if (_keyRb != null) _keyRb.isKinematic = true;
        if (_keyCols != null) foreach (var c in _keyCols) c.enabled = false;

        Vector3 start = key.position;
        Vector3 end = landingPoint.position;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, flightDuration);
            float k = Mathf.Clamp01(t);

            Vector3 pos = Vector3.Lerp(start, end, k);
            pos.y += arcHeight * 4f * k * (1f - k); // simple parabola
            key.position = pos;
            key.Rotate(tumbleSpeed * Time.deltaTime, Space.Self);

            yield return null;
        }

        key.SetPositionAndRotation(end, landingPoint.rotation);

        if (_keyCols != null) foreach (var c in _keyCols) c.enabled = true;

        if (_keyRb != null)
        {
            if (enablePhysicsOnLand)
            {
                _keyRb.isKinematic = false;
                _keyRb.linearVelocity = Vector3.zero;   // Unity 6 API; use .velocity on older versions
                _keyRb.angularVelocity = Vector3.zero;
            }
            // else: stays kinematic at the landing point, ready for ISDK grab
        }

        Destroy(gameObject);
    }
}