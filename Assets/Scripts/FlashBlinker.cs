using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Blinks a target GameObject on and off a configurable number of times
/// spread over a configurable duration. Drag the object to flash into
/// Target and call StartFlashing() from a UnityEvent, or enable Play On Start.
/// </summary>
public class FlashBlinker : MonoBehaviour
{
    public enum FinishState
    {
        Off,
        On,
        RestoreOriginal
    }

    [Header("Target")]
    [Tooltip("The object to turn on and off. Drag it here from the hierarchy.")]
    [SerializeField] private GameObject target;

    [Header("Flashing")]
    [Tooltip("How many times to flash.")]
    [SerializeField] private int flashCount = 5;

    [Tooltip("Total seconds the whole flashing sequence takes.")]
    [SerializeField] private float duration = 2f;

    [Tooltip("Portion of each cycle the object stays on. 0.5 means equal on and off time.")]
    [Range(0.05f, 0.95f)]
    [SerializeField] private float onRatio = 0.5f;

    [Header("Options")]
    [Tooltip("Start flashing automatically when the scene starts.")]
    [SerializeField] private bool playOnStart = false;

    [Tooltip("Use unscaled time (ignores Time.timeScale, e.g. while paused).")]
    [SerializeField] private bool useUnscaledTime = false;

    [Tooltip("State the target is left in once the sequence ends or is stopped.")]
    [SerializeField] private FinishState stateWhenFinished = FinishState.Off;

    [Header("Event")]
    [SerializeField] private UnityEvent onFinished;

    private Coroutine routine;
    private bool originalActive;

    /// <summary>True while a flashing sequence is running.</summary>
    public bool IsFlashing => routine != null;

    private void Awake()
    {
        if (target != null)
        {
            originalActive = target.activeSelf;

            if (target == gameObject)
            {
                Debug.LogWarning(
                    $"{nameof(FlashBlinker)} on '{name}' targets its own GameObject. " +
                    "Deactivating it stops the flashing coroutine, so put this component " +
                    "on a parent or a separate manager object instead.", this);
            }
        }
    }

    private void Start()
    {
        if (playOnStart)
            StartFlashing();
    }

    private void OnDisable()
    {
        StopFlashing();
    }

    /// <summary>Flash using the Inspector values.</summary>
    public void StartFlashing()
    {
        StartFlashing(flashCount, duration);
    }

    /// <summary>Flash with custom values instead of the Inspector ones.</summary>
    public void StartFlashing(int count, float seconds)
    {
        if (target == null)
        {
            Debug.LogWarning($"{nameof(FlashBlinker)} on '{name}' has no Target assigned.", this);
            return;
        }

        if (count <= 0 || seconds <= 0f)
        {
            Debug.LogWarning(
                $"{nameof(FlashBlinker)} on '{name}' needs a positive flash count and duration " +
                $"(got count={count}, duration={seconds}).", this);
            return;
        }

        StopFlashing();
        routine = StartCoroutine(Run(count, seconds));
    }

    /// <summary>Stop immediately and apply the finish state.</summary>
    public void StopFlashing()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        ApplyFinishState();
    }

    private IEnumerator Run(int count, float seconds)
    {
        float cycle = seconds / count;
        float onTime = cycle * onRatio;
        float offTime = cycle - onTime;

        for (int i = 0; i < count; i++)
        {
            target.SetActive(true);
            yield return Wait(onTime);

            target.SetActive(false);
            yield return Wait(offTime);
        }

        routine = null;
        ApplyFinishState();
        onFinished?.Invoke();
    }

    private object Wait(float seconds)
    {
        return useUnscaledTime
            ? (object)new WaitForSecondsRealtime(seconds)
            : new WaitForSeconds(seconds);
    }

    private void ApplyFinishState()
    {
        if (target == null)
            return;

        switch (stateWhenFinished)
        {
            case FinishState.On:
                target.SetActive(true);
                break;
            case FinishState.RestoreOriginal:
                target.SetActive(originalActive);
                break;
            default:
                target.SetActive(false);
                break;
        }
    }
}
