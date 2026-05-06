using UnityEngine;

public class CapController : MonoBehaviour
{
    public bool IsOpen { get; private set; }

    private float cooldownTime = 3f;  
    private float nextInteractionTime = 0f;

    public void OnCapOpen()
    {
        if (Time.time >= nextInteractionTime)
        {
            IsOpen = true;
            nextInteractionTime = Time.time + cooldownTime;
        }
    }

    public void OnCapClose()
    {
        if (Time.time >= nextInteractionTime)
        {
            IsOpen = false;
            nextInteractionTime = Time.time + cooldownTime;
        }
    }
}