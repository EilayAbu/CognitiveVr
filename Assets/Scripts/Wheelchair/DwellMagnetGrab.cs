using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// <summary>
/// Grab without closing the hand.
///   - Keep the hand near an object for grabDwellTime (default 2s) -> the object
///     is pulled to the hand like a magnet and held.
///   - Touch the held object with your OTHER hand -> it is released.
/// Put one on each hand/controller anchor. Grabbable objects only need a
/// Collider + Rigidbody (optionally filtered by layer or tag).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class DwellMagnetGrab : MonoBehaviour
{
    [Header("What can be grabbed")]
    public LayerMask grabbableLayers = ~0;
    [Tooltip("Leave empty to allow any Rigidbody on the layers above. The tag must exist in the Tag Manager.")]
    public string requiredTag = "";

    [Header("Detection")]
    [Tooltip("Radius of the magnet zone around the hand (m, local to this transform). Applied in Awake.")]
    public float detectionRadius = 0.12f;

    [Header("Timing")]
    public float grabDwellTime = 2f;
    [Tooltip("How long the other hand must touch the held object to release it. Small value avoids accidental bumps.")]
    public float releaseTouchTime = 0.2f;
    [Tooltip("After release, the object can't be grabbed again for this long (so it doesn't jump right back).")]
    public float regrabCooldown = 1.5f;
    [Tooltip("If the hand drifts out briefly (tremor), progress is kept for this long.")]
    public float dwellGraceTime = 0.3f;

    [Header("Magnet / hold")]
    [Tooltip("Where the object is held. Defaults to this transform.")]
    public Transform holdPoint;
    public float magnetDuration = 0.25f;
    [Tooltip("Keep the object's rotation relative to the hand at grab time.")]
    public bool keepRelativeRotation = true;

    [Header("Release")]
    [Tooltip("ON: object falls after release. OFF: object stays floating where it was released.")]
    public bool dropWithGravity = true;

    [Header("Feedback")]
    [Tooltip("Optional object (e.g. a sphere or ring) that grows from 0 to full size while dwelling.")]
    public Transform progressIndicator;
    public UnityEvent<float> onDwellProgress;
    public UnityEvent<GameObject> onGrabbed;
    public UnityEvent<GameObject> onReleased;

    [Header("Debug")]
    [Tooltip("Logs every enter/exit/ignore/dwell/grab/release to the Console (and logcat on Quest).")]
    public bool debugLogs = true;
    [Tooltip("Shows the real detection zone as a see-through sphere. " +
             "Grey = nothing, cyan = something in range but not grabbable now, yellow->green = dwelling, green = holding.")]
    public bool debugVisual = true;
    [Range(0.05f, 1f)] public float debugVisualAlpha = 0.25f;
    [Tooltip("Read-only: current state, visible in the Inspector during Play.")]
    [SerializeField] string debugStatus;

    public Rigidbody Held => _held;
    public bool IsHolding => _isHolding;

    // Shared between all hands
    static readonly Dictionary<Rigidbody, DwellMagnetGrab> s_holders = new Dictionary<Rigidbody, DwellMagnetGrab>();
    static readonly Dictionary<Rigidbody, float> s_cooldownUntil = new Dictionary<Rigidbody, float>();

    readonly Dictionary<Rigidbody, int> _overlaps = new Dictionary<Rigidbody, int>();
    readonly Dictionary<Rigidbody, float> _releaseTouch = new Dictionary<Rigidbody, float>();
    readonly List<Rigidbody> _tmp = new List<Rigidbody>();
    readonly List<Rigidbody> _stale = new List<Rigidbody>();

    Rigidbody _selfBody;
    Rigidbody _target;
    float _dwell, _lostTimer, _progress;

    Rigidbody _held;
    bool _isHolding;
    bool _heldWasKinematic, _heldUsedGravity;
    RigidbodyInterpolation _heldInterpolation;
    Vector3 _grabStartPos;
    Quaternion _grabStartRot, _rotOffset;
    float _magnetT;

    Vector3 _indicatorBaseScale = Vector3.one;
    Renderer _debugRenderer;

    void Awake()
    {
        if (!holdPoint) holdPoint = transform;

        var col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = detectionRadius;

        // Trigger events need a Rigidbody on at least one side.
        _selfBody = GetComponent<Rigidbody>();
        if (!_selfBody) _selfBody = gameObject.AddComponent<Rigidbody>();
        _selfBody.isKinematic = true;
        _selfBody.useGravity = false;

        if (progressIndicator)
        {
            _indicatorBaseScale = progressIndicator.localScale;
            progressIndicator.gameObject.SetActive(false);
        }

        if (debugVisual) CreateDebugVisual(col);

        Vector3 s = transform.lossyScale;
        float worldRadius = col.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        Log($"Ready on '{name}'. World radius = {worldRadius:F3} m (local {col.radius:F3}, scale {s}), " +
            $"layer '{LayerMask.LayerToName(gameObject.layer)}', grabbableLayers mask = {grabbableLayers.value}");
        if (Mathf.Abs(worldRadius - detectionRadius) > 0.01f)
            LogWarn($"Hand is scaled: real zone is {worldRadius:F3} m, not {detectionRadius:F3} m.");
    }

    void Start()
    {
        // Warn if the physics Layer Collision Matrix blocks the hand from seeing grabbable layers.
        int myLayer = gameObject.layer;
        for (int l = 0; l < 32; l++)
        {
            if ((grabbableLayers.value & (1 << l)) == 0) continue;
            string ln = LayerMask.LayerToName(l);
            if (string.IsNullOrEmpty(ln)) continue;
            if (Physics.GetIgnoreLayerCollision(myLayer, l))
                LogWarn($"Layer Collision Matrix: '{LayerMask.LayerToName(myLayer)}' x '{ln}' is OFF -> objects on '{ln}' will never be detected.");
        }
    }

    void OnDisable()
    {
        if (_isHolding) Release();
        _overlaps.Clear();
        _releaseTouch.Clear();
        ResetDwell();
    }

    // ---------------- Trigger tracking ----------------

    void OnTriggerEnter(Collider other)
    {
        string why = WhyNotGrabbable(other);
        if (why != null)
        {
            Log($"Ignored '{other.name}': {why}");
            return;
        }
        var rb = other.attachedRigidbody;
        _overlaps.TryGetValue(rb, out int n);
        _overlaps[rb] = n + 1;
        if (n == 0) Log($"In range: '{rb.name}'");
    }

    void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (!rb || !_overlaps.TryGetValue(rb, out int n)) return;
        if (n <= 1)
        {
            _overlaps.Remove(rb);
            Log($"Out of range: '{rb.name}'");
        }
        else _overlaps[rb] = n - 1;
    }

    /// <summary>Returns null if grabbable, otherwise the reason it isn't.</summary>
    string WhyNotGrabbable(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (!rb) return "no Rigidbody on it or its parents (add one)";
        if (rb == _selfBody) return "own hand";
        // FIX: the other hand also has a kinematic Rigidbody + trigger -> never treat a hand as grabbable.
        if (rb.GetComponent<DwellMagnetGrab>()) return "it's a hand with DwellMagnetGrab";
        if ((grabbableLayers.value & (1 << rb.gameObject.layer)) == 0)
            return $"layer '{LayerMask.LayerToName(rb.gameObject.layer)}' not in grabbableLayers";
        if (!string.IsNullOrEmpty(requiredTag) && !rb.CompareTag(requiredTag))
            return $"tag '{rb.tag}' != required '{requiredTag}'";
        return null;
    }

    void CleanOverlaps()
    {
        _tmp.Clear();
        foreach (var kv in _overlaps)
            if (!kv.Key || !kv.Key.gameObject.activeInHierarchy) _tmp.Add(kv.Key);
        foreach (var rb in _tmp) _overlaps.Remove(rb);
    }

    // ---------------- Main loop ----------------

    void Update()
    {
        float dt = Time.deltaTime;
        CleanOverlaps();

        // Held object was destroyed
        if (_isHolding && !_held)
        {
            Log("Held object was destroyed.");
            s_holders.Remove(_held);
            _isHolding = false;
            _held = null;
        }

        HandleReleaseByTouch(dt);

        if (!_isHolding) HandleDwell(dt);

        UpdateDebug();
    }

    void HandleReleaseByTouch(float dt)
    {
        // This hand touching an object that ANOTHER hand is holding -> release it.
        _tmp.Clear();
        foreach (var kv in _overlaps)
        {
            var rb = kv.Key;
            if (s_holders.TryGetValue(rb, out var holder) && holder != this) _tmp.Add(rb);
        }

        // Drop timers for objects we are no longer touching
        _stale.Clear();
        foreach (var rb in _releaseTouch.Keys)
            if (!_tmp.Contains(rb)) _stale.Add(rb);
        foreach (var rb in _stale) _releaseTouch.Remove(rb);

        foreach (var rb in _tmp)
        {
            _releaseTouch.TryGetValue(rb, out float t);
            if (t == 0f) Log($"Touching '{rb.name}' held by other hand - releasing in {releaseTouchTime:F2}s");
            t += dt;
            if (t >= releaseTouchTime)
            {
                _releaseTouch.Remove(rb);
                if (s_holders.TryGetValue(rb, out var holder))
                {
                    Log($"Touch-release: '{rb.name}' (was held by '{holder.name}')");
                    holder.Release();
                }
            }
            else
            {
                _releaseTouch[rb] = t;
            }
        }
    }

    void HandleDwell(float dt)
    {
        // Keep the current target while it's still in range (prevents flicker between two nearby objects)
        bool targetStillValid = _target && _overlaps.ContainsKey(_target) && CanBeGrabbedNow(_target);

        if (targetStillValid)
        {
            _lostTimer = 0f;
            _dwell += dt;
        }
        else
        {
            Rigidbody best = FindBestCandidate();

            if (best && best != _target)
            {
                _target = best;
                _dwell = 0f;
                _lostTimer = 0f;
                Log($"Dwell started on '{best.name}' ({grabDwellTime:F1}s to grab)");
            }
            else if (_target)
            {
                // Grace period for tremor / brief drift
                _lostTimer += dt;
                if (_lostTimer > dwellGraceTime)
                {
                    Log($"Dwell lost on '{_target.name}' at {_dwell:F2}s");
                    ResetDwell();
                }
            }
        }

        float progress = _target ? Mathf.Clamp01(_dwell / grabDwellTime) : 0f;
        UpdateFeedback(progress);

        if (_target && _dwell >= grabDwellTime)
            Grab(_target);
    }

    Rigidbody FindBestCandidate()
    {
        Rigidbody best = null;
        float bestDist = float.MaxValue;
        Vector3 p = holdPoint.position;

        foreach (var kv in _overlaps)
        {
            var rb = kv.Key;
            if (!CanBeGrabbedNow(rb)) continue;
            float d = (rb.worldCenterOfMass - p).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = rb; }
        }
        return best;
    }

    bool CanBeGrabbedNow(Rigidbody rb)
    {
        if (!rb) return false;
        if (s_holders.ContainsKey(rb)) return false;
        if (s_cooldownUntil.TryGetValue(rb, out float until) && Time.time < until) return false;
        return true;
    }

    void ResetDwell()
    {
        _target = null;
        _dwell = 0f;
        _lostTimer = 0f;
        UpdateFeedback(0f);
    }

    void UpdateFeedback(float progress)
    {
        _progress = progress;
        if (progressIndicator)
        {
            bool show = progress > 0f;
            if (progressIndicator.gameObject.activeSelf != show)
                progressIndicator.gameObject.SetActive(show);
            progressIndicator.localScale = _indicatorBaseScale * progress;
        }
        onDwellProgress?.Invoke(progress);
    }

    // ---------------- Grab / release ----------------

    void Grab(Rigidbody rb)
    {
        ResetDwell();

        _held = rb;
        _isHolding = true;
        s_holders[rb] = this;

        _heldWasKinematic = rb.isKinematic;
        _heldUsedGravity = rb.useGravity;
        _heldInterpolation = rb.interpolation;

        if (!rb.isKinematic) SetVelocity(rb, Vector3.zero);
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.None; // we drive the transform directly

        _grabStartPos = rb.transform.position;
        _grabStartRot = rb.transform.rotation;
        _rotOffset = Quaternion.Inverse(holdPoint.rotation) * rb.transform.rotation;
        _magnetT = 0f;

        Log($"GRABBED '{rb.name}'");
        onGrabbed?.Invoke(rb.gameObject);
    }

    /// <summary>Releases the held object. Can also be called from UI, voice, etc.</summary>
    public void Release()
    {
        if (!_isHolding) return;
        var rb = _held;

        _isHolding = false;
        _held = null;
        s_holders.Remove(rb);

        if (rb)
        {
            s_cooldownUntil[rb] = Time.time + regrabCooldown;
            rb.interpolation = _heldInterpolation;

            if (dropWithGravity)
            {
                rb.isKinematic = _heldWasKinematic;
                rb.useGravity = _heldUsedGravity;
                if (!rb.isKinematic)
                {
                    SetVelocity(rb, Vector3.zero);
                    rb.angularVelocity = Vector3.zero;
                }
            }
            // else: stays kinematic and floats where it was released

            Log($"RELEASED '{rb.name}' (cooldown {regrabCooldown:F1}s)");
            onReleased?.Invoke(rb.gameObject);
        }
    }

    void LateUpdate()
    {
        // LateUpdate so we follow the hand after tracking has updated this frame
        if (!_isHolding || !_held) return;

        Transform t = _held.transform;
        Vector3 targetPos = holdPoint.position;
        Quaternion targetRot = keepRelativeRotation ? holdPoint.rotation * _rotOffset : t.rotation;

        if (_magnetT < 1f)
        {
            _magnetT += magnetDuration > 0f ? Time.deltaTime / magnetDuration : 1f;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_magnetT));
            t.SetPositionAndRotation(
                Vector3.Lerp(_grabStartPos, targetPos, e),
                Quaternion.Slerp(_grabStartRot, targetRot, e));
        }
        else
        {
            t.SetPositionAndRotation(targetPos, targetRot);
        }
    }

    static void SetVelocity(Rigidbody rb, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }

    // ---------------- Debug ----------------

    void Log(string msg)
    {
        if (debugLogs) Debug.Log($"[DwellGrab:{name}] {msg}", this);
    }

    void LogWarn(string msg)
    {
        Debug.LogWarning($"[DwellGrab:{name}] {msg}", this);
    }

    void CreateDebugVisual(SphereCollider col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        // Remove the primitive's collider BEFORE parenting, otherwise it would join this hand's Rigidbody.
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = "DwellGrab_DebugZone";
        go.transform.SetParent(transform, false);
        go.transform.localPosition = col.center;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * col.radius * 2f; // inherits hand scale = exact trigger size

        _debugRenderer = go.GetComponent<Renderer>();
        _debugRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _debugRenderer.receiveShadows = false;
        // Sprites/Default is in "Always Included Shaders" by default, supports transparency, renders in URP.
        var sh = Shader.Find("Sprites/Default");
        if (sh) _debugRenderer.material = new Material(sh);
        else LogWarn("Sprites/Default shader not found - debug sphere will be opaque.");
    }

    void UpdateDebug()
    {
        Color c;
        if (_isHolding)
        {
            c = Color.green;
            debugStatus = $"HOLDING {(_held ? _held.name : "?")}";
        }
        else if (_target)
        {
            c = Color.Lerp(Color.yellow, Color.green, _progress);
            debugStatus = $"Dwelling {_target.name} {_dwell:F1}/{grabDwellTime:F1}s";
        }
        else if (_overlaps.Count > 0)
        {
            c = Color.cyan; // in range but held by other hand / cooling down
            debugStatus = $"{_overlaps.Count} in range, none grabbable now";
        }
        else
        {
            c = Color.grey;
            debugStatus = "Idle";
        }

        if (_debugRenderer)
        {
            _debugRenderer.enabled = debugVisual;
            c.a = _isHolding ? Mathf.Min(1f, debugVisualAlpha * 1.5f) : debugVisualAlpha;
            _debugRenderer.material.color = c;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 s = transform.lossyScale;
        float r = detectionRadius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, r);
    }
#endif
}