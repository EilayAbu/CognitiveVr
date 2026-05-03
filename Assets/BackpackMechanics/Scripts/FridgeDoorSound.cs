using UnityEngine;

public class FridgeDoorSound : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    private Animator animator;
    private bool isDoorOpen = false;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        bool isInOpenState = animator.GetCurrentAnimatorStateInfo(0).IsName("IdleOpened");

        if (isInOpenState != isDoorOpen)
        {
            isDoorOpen = isInOpenState;

            if (isDoorOpen && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
            else if (!isDoorOpen && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }
    }
}