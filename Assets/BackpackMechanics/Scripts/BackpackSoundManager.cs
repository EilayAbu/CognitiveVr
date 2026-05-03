using UnityEngine;

public class BackpackSoundManager : MonoBehaviour
{
    public AudioClip chickenSound;
    public AudioClip thermosSound;
    public AudioClip smartphoneSound;
    public AudioClip walletSound;

    private AudioSource audioSource;
    private float lastPlayTime;
    private float minTimeBetweenPlays = 1f; // Минимальное время между звуками в секундах

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.loop = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[SNAP_SOUND] OnTriggerEnter с объектом {other.gameObject.name}");

        // Проверяем, прошло ли достаточно времени с последнего проигрывания
        if (Time.time - lastPlayTime < minTimeBetweenPlays)
        {
            Debug.Log("[SNAP_SOUND] Слишком рано для нового звука");
            return;
        }

        if (other.gameObject.name.Contains("[BuildingBlock]"))
        {
            if (other.gameObject.name.Contains("C_Chicken") && chickenSound != null)
            {
                audioSource.PlayOneShot(chickenSound);
                lastPlayTime = Time.time;
                Debug.Log("[SNAP_SOUND] Проигрывание звука для C_Chicken");
            }
            else if (other.gameObject.name.Contains("C_Thermos") && thermosSound != null)
            {
                audioSource.PlayOneShot(thermosSound);
                lastPlayTime = Time.time;
                Debug.Log("[SNAP_SOUND] Проигрывание звука для C_Thermos");
            }
            else if (other.gameObject.name.Contains("C_Smartphone") && smartphoneSound != null)
            {
                audioSource.PlayOneShot(smartphoneSound);
                lastPlayTime = Time.time;
                Debug.Log("[SNAP_SOUND] Проигрывание звука для C_Smartphone");
            }
            else if (other.gameObject.name.Contains("C_Wallet") && walletSound != null)
            {
                audioSource.PlayOneShot(walletSound);
                lastPlayTime = Time.time;
                Debug.Log("[SNAP_SOUND] Проигрывание звука для C_Wallet");
            }
        }
    }
}