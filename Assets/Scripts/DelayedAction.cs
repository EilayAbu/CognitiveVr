using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Invokes a UnityEvent after a configurable delay.
/// Attach to any GameObject, wire the event in the Inspector,
/// and trigger via Play() or automatically on enable.
/// </summary>
public class DelayedAction : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds to wait before invoking the action.")]
    [SerializeField] private float delay = 1f;

    [Tooltip("Use unscaled time (ignores Time.timeScale, e.g. for pause menus).")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Trigger")]
    [Tooltip("Automatically run when the component becomes enabled.")]
    [SerializeField] private bool playOnEnable = false;

    [Tooltip("Repeat the action on a loop instead of once.")]
    [SerializeField] private bool loop = false;

    [Header("Event")]
    [SerializeField] private UnityEvent onComplete;

    private Coroutine routine;

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    private void OnDisable()
    {
        Cancel();
    }

    /// <summary>Start the delay using the Inspector value.</summary>
    public void Play()
    {
        Cancel();
        routine = StartCoroutine(Run(delay));
    }

    /// <summary>Start the delay with a custom duration (overrides Inspector value).</summary>
    public void Play(float customDelay)
    {
        Cancel();
        routine = StartCoroutine(Run(customDelay));
    }

    /// <summary>Stop a pending action before it fires.</summary>
    public void Cancel()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    private IEnumerator Run(float wait)
    {
        do
        {
            if (useUnscaledTime)
                yield return new WaitForSecondsRealtime(wait);
            else
                yield return new WaitForSeconds(wait);

            onComplete?.Invoke();
        }
        while (loop && isActiveAndEnabled);

        routine = null;
    }
}