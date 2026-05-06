
using System.Collections;
using UnityEngine;

public class WaterFillTrigger : MonoBehaviour
{
    public ParticleSystem tapWater;
    public Animator bottleWaterAnimator;
    public CapController capController;
    public AudioSource bottleSound;
    public float fillDelay = 12f;
    private bool isFilling;
    private Coroutine fillCoroutine;
    private Transform currentBottle;

    void Start()
    {
        bottleWaterAnimator.SetBool("IsFilling", false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Bottle"))
        {
            currentBottle = other.transform;
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Bottle") && CheckAllConditions(other.transform))
        {
            if (!isFilling)
            {
                fillCoroutine = StartCoroutine(StartFilling());
                bottleSound.Play();
            }
        }
        else if (bottleSound.isPlaying)
        {
            StopFilling();
        }
    }

    bool CheckAllConditions(Transform bottle)
    {
        return capController.IsOpen &&
               tapWater.isPlaying &&
               IsBottleUpright(bottle);
    }

    bool IsBottleUpright(Transform bottle)
    {
        return Vector3.Dot(bottle.up, Vector3.up) > 0.9f;
    }

    IEnumerator StartFilling()
    {
        isFilling = true;
        yield return new WaitForSeconds(fillDelay);
        if (currentBottle != null && CheckAllConditions(currentBottle))
        {
            bottleWaterAnimator.SetBool("IsFilling", true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Bottle"))
        {
            currentBottle = null;
            StopFilling();
        }
    }

    void StopFilling()
    {
        if (fillCoroutine != null)
        {
            StopCoroutine(fillCoroutine);
            fillCoroutine = null;
        }
        isFilling = false;
        bottleWaterAnimator.SetBool("IsFilling", false);
        bottleSound.Stop();
    }
}