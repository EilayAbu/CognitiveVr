using System.Collections;
using UnityEngine;

public class GuideScenarioController : MonoBehaviour
{
    [Header("Existing Components")]
    [SerializeField] private AudioSource voice;

    [Header("Voice Clips")]
    [SerializeField] private AudioClip askClip;
    [SerializeField] private AudioClip agreeClip;
    [SerializeField] private AudioClip refuseClip;

    [Header("Optional Clips")]
    [SerializeField] private AudioClip doorClosedClip;
    [SerializeField] private AudioClip sorryClip;

    [Header("Mission Audio")]
    [SerializeField] private AudioClip tickClip;

    private void Reset()
    {
        voice = GetComponent<AudioSource>();
    }

    public IEnumerator PlayAsk() => PlayVoice(askClip);
    public IEnumerator PlayAgree() => PlayVoice(agreeClip);
    public IEnumerator PlayRefuse() => PlayVoice(refuseClip);
    public IEnumerator PlayDoorClosed() => PlayVoice(doorClosedClip);
    public IEnumerator PlaySorry() => PlayVoice(sorryClip);

    public IEnumerator PlayVoice(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogWarning("GuideScenarioController: Missing AudioClip.");
            yield break;
        }

        if (voice == null)
        {
            Debug.LogWarning("GuideScenarioController: Missing AudioSource.");
            yield return new WaitForSeconds(clip.length);
            yield break;
        }

        voice.Stop();
        voice.clip = clip;
        voice.Play();

        yield return new WaitForSeconds(clip.length);
    }

    public void PlayTick()
    {
        if (tickClip == null)
        {
            Debug.LogWarning("GuideScenarioController: Missing tickClip.");
            return;
        }

        if (voice == null)
        {
            Debug.LogWarning("GuideScenarioController: Missing AudioSource.");
            return;
        }

        voice.PlayOneShot(tickClip);
    }

    public void StopVoice()
    {
        if (voice != null)
            voice.Stop();
    }
}
