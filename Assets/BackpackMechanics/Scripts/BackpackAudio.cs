using UnityEngine;
using Oculus.Interaction;

public class BackpackAudio : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip chickenSound;
    public AudioClip thermosSound;
    public AudioClip smartphoneSound;
    public AudioClip walletSound;

    
    public void PlayChickenSound()
    {
        if (chickenSound != null)
            audioSource.PlayOneShot(chickenSound);
    }

    public void PlayThermosSound()
    {
        if (thermosSound != null)
            audioSource.PlayOneShot(thermosSound);
    }

    public void PlaySmartphoneSound()
    {
        if (smartphoneSound != null)
            audioSource.PlayOneShot(smartphoneSound);
    }

    public void PlayWalletSound()
    {
        if (walletSound != null)
            audioSource.PlayOneShot(walletSound);
    }
}