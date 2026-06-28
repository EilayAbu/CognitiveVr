using UnityEngine;
using UnityEngine.Events;

public class DoorStateEvents : MonoBehaviour
{
    [Header("Door")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private bool useLocalRotation = true;

    [Header("Thresholds (Y angle in degrees)")]
    [SerializeField] private float openAngleThreshold = 30f;
    [SerializeField] private float closedAngleThreshold = 10f;

    [Header("Events")]
    [SerializeField] private UnityEvent onDoorOpened;
    [SerializeField] private UnityEvent onDoorClosed;

    // Code-facing events so other systems (e.g. WindowTaskManager) can subscribe in C#.
    public event System.Action DoorOpened;
    public event System.Action DoorClosed;

    public bool IsOpen => isOpen;

    private bool isOpen;

    private void Start()
    {
        if (doorPivot == null)
            doorPivot = transform;

        // Инициализация состояния без вызова события при старте сцены.
        isOpen = GetYAngle() > openAngleThreshold;
    }

    private void Update()
    {
        float angle = GetYAngle();

        if (!isOpen && angle > openAngleThreshold)
        {
            isOpen = true;
            Debug.Log("Door opened.");
            onDoorOpened.Invoke();
            DoorOpened?.Invoke();
        }
        else if (isOpen && angle < closedAngleThreshold)
        {
            isOpen = false;
            Debug.Log("Door closed.");
            onDoorClosed.Invoke();
            DoorClosed?.Invoke();
        }
    }

    private float GetYAngle()
    {
        float y = useLocalRotation
            ? doorPivot.localEulerAngles.y
            : doorPivot.eulerAngles.y;

        // Берём модуль угла, чтобы открытие в обе стороны (например -60°) считалось как 60°.
        return Mathf.Abs(NormalizeAngle(y));
    }

    // eulerAngles возвращает 0..360; приводим к -180..180 для стабильного сравнения с порогом.
    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;

        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;

        return angle;
    }
}
