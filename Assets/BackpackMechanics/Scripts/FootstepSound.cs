using UnityEngine;
using VRLocomotion;

public class FootstepSound : MonoBehaviour
{
    private AudioSource audioSource;
    public AudioClip footstepSound;
    private float lastStepTime;
    private float stepInterval = 0.5f;
    private HandMovementAnalyzer movementAnalyzer;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        // »щем HandMovementAnalyzer в сцене
        movementAnalyzer = FindObjectOfType<HandMovementAnalyzer>();
    }

    void Update()
    {
        if (movementAnalyzer != null && movementAnalyzer.IsWalking && Time.time - lastStepTime > stepInterval)
        {
            PlayFootstep();
            lastStepTime = Time.time;

            stepInterval = Mathf.Lerp(0.7f, 0.3f, movementAnalyzer.MovementIntensity);
        }
    }

    void PlayFootstep()
    {
        if (audioSource != null && footstepSound != null)
        {
            audioSource.PlayOneShot(footstepSound);
        }
    }
}