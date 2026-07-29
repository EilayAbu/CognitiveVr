using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NextSceneOnEvent : MonoBehaviour
{
    [SerializeField, Min(0f)] private float delayBeforeNextScene = 10f;

    // Hook this up to any UnityEvent (button, trigger, etc.)
    public void GoToNextScene()
    {
        StartCoroutine(LoadNextSceneAfterDelay());
    }

    private IEnumerator LoadNextSceneAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforeNextScene);

        int next = SceneManager.GetActiveScene().buildIndex + 1;
        if (next < SceneManager.sceneCountInBuildSettings)
            SceneManager.LoadScene(next);
        else
            Debug.LogWarning("No next scene in Build Settings.");
    }
}