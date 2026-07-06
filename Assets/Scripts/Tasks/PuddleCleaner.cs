using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on the puddle (the object with the trigger collider).
/// Each time the sponge enters the trigger, the puddle shrinks by one step
/// (default 4 steps = 25% each) until it's gone, then deactivates.
///
/// Physics requirement: at least one side needs a Rigidbody for trigger
/// events to fire - your grabbable sponge almost certainly has one already.
/// </summary>
public class PuddleCleaner : MonoBehaviour
{
    [Header("What counts as the sponge")]
    [Tooltip("Optional: drag the sponge's collider here for an exact match.")]
    public Collider spongeCollider;
    [Tooltip("Used if no collider is assigned. Leave empty to accept any object.")]
    public string spongeTag = "Sponge";

    [Header("Cleaning")]
    [Tooltip("Number of wipes to fully clean. 4 = 25% per wipe.")]
    [Range(1, 10)] public int wipesToClean = 4;
    [Tooltip("Seconds to animate each shrink. 0 = instant.")]
    public float shrinkDuration = 0.25f;
    [Tooltip("Ignore repeat triggers for this long (prevents double-hits).")]
    public float cooldown = 0.5f;
    [Tooltip("Shrink only X/Z, keeping height (good for flat puddles).")]
    public bool keepHeight = true;

    [Header("Effects")]
    [Tooltip("The audio component that plays the cleaning sound.")]
    public AudioSource cleaningSound;
    [Tooltip("The particle system that plays a small splash effect.")]
    public ParticleSystem splashParticle;

    [Header("Events")]
    public UnityEvent onWipe;      // fired every successful wipe
    public UnityEvent onCleaned;   // fired when the puddle is fully gone

    Vector3 _startScale;
    int _wipes;
    float _lastWipeTime = -999f;
    Coroutine _shrinkRoutine;

    void Awake()
    {
        _startScale = transform.localScale;
        if (!GetComponent<Collider>() || !GetComponent<Collider>().isTrigger)
            Debug.LogWarning($"[PuddleCleaner] {name}: collider missing or not set to Is Trigger.", this);
    }

    /// <summary>Reset the puddle to full size (e.g., for restarting a task).</summary>
    public void ResetPuddle()
    {
        if (_shrinkRoutine != null) StopCoroutine(_shrinkRoutine);
        _wipes = 0;
        _lastWipeTime = -999f;
        transform.localScale = _startScale;
        gameObject.SetActive(true);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsSponge(other)) return;
        if (Time.time - _lastWipeTime < cooldown) return;
        if (_wipes >= wipesToClean) return;

        _lastWipeTime = Time.time;
        _wipes++;
        
        // Play the cleaning sound and splash particles
        PlayEffects();
        
        onWipe?.Invoke();

        Vector3 target = ScaleForWipes(_wipes);
        if (_shrinkRoutine != null) StopCoroutine(_shrinkRoutine);
        _shrinkRoutine = StartCoroutine(ShrinkTo(target, _wipes >= wipesToClean));
    }

    void PlayEffects()
    {
        if (cleaningSound != null)
        {
            cleaningSound.Play();
        }

        if (splashParticle != null)
        {
            // Move the particle system to the splash location if needed, then play
            splashParticle.Play();
        }
    }

    bool IsSponge(Collider other)
    {
        if (spongeCollider != null)
            return other == spongeCollider || other.transform.IsChildOf(spongeCollider.transform);
        if (!string.IsNullOrEmpty(spongeTag))
            return other.CompareTag(spongeTag) || (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag(spongeTag));
        return true; // no filter set - anything triggers it
    }

    Vector3 ScaleForWipes(int wipes)
    {
        float f = Mathf.Clamp01(1f - (float)wipes / wipesToClean); // 0.75, 0.5, 0.25, 0
        return keepHeight
            ? new Vector3(_startScale.x * f, _startScale.y, _startScale.z * f)
            : _startScale * f;
    }

    IEnumerator ShrinkTo(Vector3 target, bool finalWipe)
    {
        if (shrinkDuration > 0f)
        {
            Vector3 from = transform.localScale;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / shrinkDuration;
                transform.localScale = Vector3.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
        }
        transform.localScale = target;

        if (finalWipe)
        {
            onCleaned?.Invoke();
            gameObject.SetActive(false);
        }
    }
}