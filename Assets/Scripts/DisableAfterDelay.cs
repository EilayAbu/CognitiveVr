using UnityEngine;

public class DisableAfterDelay : MonoBehaviour
{
    [SerializeField] private float delay = 3f;

    private void OnEnable()
    {
        CancelInvoke();
        Invoke(nameof(Hide), delay);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }
}