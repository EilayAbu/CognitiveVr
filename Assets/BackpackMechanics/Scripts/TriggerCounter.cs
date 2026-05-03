using UnityEngine;

public class DoorDebug : MonoBehaviour
{
    private Animator animator;
    private int triggerCount = 0;
    private bool isOpen = false;

    void Start()
    {
        animator = GetComponent<Animator>();
        Debug.Log("Initial door state: Closed");
    }

    void OnTriggerEnter(Collider other)
    {
        triggerCount++;
        isOpen = !isOpen;
        Debug.Log($"=== Trigger Enter #{triggerCount} ===");
        Debug.Log($"Trying to set door state to: {(isOpen ? "Open" : "Closed")}");

        // Проверяем текущее состояние анимации
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        Debug.Log($"Current animation state hash: {stateInfo.fullPathHash}");
        Debug.Log($"Animation normalized time: {stateInfo.normalizedTime}");

        // Проверяем значение параметра toggle
        bool toggleValue = animator.GetBool("toggle");
        Debug.Log($"Toggle parameter value: {toggleValue}");
    }

    void OnTriggerExit(Collider other)
    {
        Debug.Log($"=== Trigger Exit (after interaction #{triggerCount}) ===");
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        Debug.Log($"State after exit: {stateInfo.fullPathHash}");
        Debug.Log($"Animation normalized time: {stateInfo.normalizedTime}");
    }

    // Отслеживаем изменения в анимации
    void Update()
    {
        if (animator != null)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            // Логируем только когда анимация завершается
            if (stateInfo.normalizedTime >= 1.0f && !stateInfo.loop)
            {
                Debug.Log($"Animation completed. Current state hash: {stateInfo.fullPathHash}");
            }
        }
    }
}