using UnityEngine;
using UnityEngine.Events;

public class MultiPressButtonEvents : MonoBehaviour
{
    [Header("Events per press")]
    public UnityEvent OnInstructions; // הוראות – לחיצה ראשונה
    public UnityEvent OnStart;        // התחלה  – לחיצה שנייה
    public UnityEvent OnHelp;         // עזרה   – לחיצה שלישית והלאה

    [SerializeField] private bool debugLogs = false;

    private int _pressCount;

    // Hook this to Button.onClick or InteractableUnityEventWrapper.WhenSelect
    public void OnButtonPressed()
    {
        _pressCount++;
        if (debugLogs) Debug.Log($"[MultiPressButtonEvents] press #{_pressCount}");

        if (_pressCount == 1)      OnInstructions?.Invoke();
        else                       OnHelp?.Invoke();
    }
    public void OnButtonStarted()
    {
        _pressCount++;
        OnStart?.Invoke();
    }

    public void ResetCounter() => _pressCount = 0;
}
