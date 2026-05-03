using UnityEngine;

public class SimpleDoorController : MonoBehaviour
{
    private Animator animator;
    private bool isOpen = false;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void OnTriggerEnter(Collider other)
    {
        // Проверяем что столкновение произошло с рукой
        if (other.gameObject.name.Contains("Hand"))
        {
            isOpen = !isOpen;
            animator.SetBool("Open", isOpen);
            Debug.Log("Hand touched the door!"); // Для отладки
        }
    }
}