using UnityEngine;

public class BottleFillChecker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ParticleSystem overflowEffect;
    [SerializeField] private string waterZoneTag = "WaterZone";

    [Header("Settings")]
    [SerializeField] private float verticalThreshold = 15f;
    [SerializeField] private float fillDuration = 12f;

    private bool isInWaterZone;
    private bool isBottleOpen;
    private bool isWaterFlowing;
    private float fillTimer;

    private void Update()
    {
        if (CheckAllConditions())
        {
            fillTimer += Time.deltaTime;

            if (fillTimer >= fillDuration && !overflowEffect.isPlaying)
            {
                overflowEffect.Play();
            }
        }
        else
        {
            fillTimer = 0f;
            if (overflowEffect.isPlaying)
            {
                overflowEffect.Stop();
            }
        }
    }

    private bool CheckAllConditions()
    {
        bool isVertical = IsBottleVertical();
        Debug.Log($"Checking conditions: InWater={isInWaterZone}, Open={isBottleOpen}, " +
                  $"Flowing={isWaterFlowing}, Vertical={isVertical}");

        return isInWaterZone &&
               isBottleOpen &&
               isWaterFlowing &&
               isVertical;
    }

    private bool IsBottleVertical()
    {
        float angle = Vector3.Angle(transform.up, Vector3.up);
        return angle <= verticalThreshold;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterZoneTag))
        {
            isInWaterZone = true;
            Debug.Log("Entered water zone");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(waterZoneTag))
        {
            isInWaterZone = false;
            Debug.Log("Exited water zone");
        }
    }

    public void SetBottleOpen(bool open)
    {
        isBottleOpen = open;
        Debug.Log($"Bottle open state changed to: {open}");
    }

    public void SetWaterFlowing(bool flowing)
    {
        isWaterFlowing = flowing;
        Debug.Log($"Water flowing state changed to: {flowing}");
    }
}