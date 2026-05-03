using UnityEngine;

public class DoorAudioDebug : MonoBehaviour
{
    private AudioSource audioSource;
    private bool isOpen = false;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        Debug.Log($"Object name: {gameObject.name}");
        Debug.Log($"Collider type: {GetComponent<Collider>().GetType()}");
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"=== Trigger Enter with object: {other.name} (parent: {other.transform.parent?.name}) ===");

        if (!isOpen)
        {
            isOpen = true;
            if (audioSource != null)
            {
                audioSource.Play();
                Debug.Log($"Started playing sound, triggered by {other.name}");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        Debug.Log($"=== Trigger Exit with object: {other.name} (parent: {other.transform.parent?.name}) ===");

        isOpen = false;
        if (audioSource != null)
        {
            audioSource.Stop();
            Debug.Log($"Stopped playing sound, triggered by {other.name}");
        }
    }
}