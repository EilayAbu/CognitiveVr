using System.Collections;
using UnityEngine;

public class DisableAfterDelay : MonoBehaviour
{
    [Tooltip("If set, the text hides when this source finishes playing.")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("Extra seconds to keep the text after the audio ends.")]
    [SerializeField] private float extraTime = 0.5f;

    [Tooltip("Used when no AudioSource is assigned, or it has no clip.")]
    [SerializeField] private float delay = 3f;

    private void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(HideRoutine());
    }

    private IEnumerator HideRoutine()
    {
        if (audioSource == null)
        {
            yield return new WaitForSeconds(delay);
        }
        else
        {
            // Button events enable the text before calling AudioSource.Play().
            yield return null;

            if (audioSource.isPlaying)
            {
                while (audioSource.isPlaying) yield return null;
            }
            else
            {
                var clip = audioSource.clip;
                yield return new WaitForSeconds(clip != null ? clip.length : delay);
            }

            yield return new WaitForSeconds(extraTime);
        }

        gameObject.SetActive(false);
    }
}
