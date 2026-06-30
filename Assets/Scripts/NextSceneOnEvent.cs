using UnityEngine;
using UnityEngine.SceneManagement;

public class NextSceneOnEvent : MonoBehaviour
{
    // Hook this up to any UnityEvent (button, trigger, etc.)
    public void GoToNextScene()
    {
        int next = SceneManager.GetActiveScene().buildIndex + 1;
        if (next < SceneManager.sceneCountInBuildSettings)
            SceneManager.LoadScene(next);
        else
            Debug.LogWarning("No next scene in Build Settings.");
    }
}