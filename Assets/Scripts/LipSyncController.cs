using UnityEngine;

public class LipSyncController : MonoBehaviour
{
    public AudioSource audioSource;
    public Transform jawBone;
    public Transform lipUpperLeft;
    public Transform lipUpperRight;
    public Transform lipUpperMiddle;
    public Transform lipCornerLeft;
    public Transform lipCornerRight;

    public Transform eyelidUpperLeft;
    public Transform eyelidUpperRight;
    public Transform browInnerLeft;
    public Transform browInnerRight;
    public Transform browOuterLeft;
    public Transform browOuterRight;

    public float sensitivity = 15f;
    public float smoothing = 40f;
    public float jawAngle = 8f;
    public float lipUpperAngle = 60f;
    public float lipUpperMiddleAngle = 20f;
    public float lipCornerAngle = 5f;
    public float blinkAngle = 40f;
    public float browAngle = 10f;

    private Quaternion jawClosed, ulLClosed, ulRClosed, ulMClosed, lcLClosed, lcRClosed;
    private Quaternion elLClosed, elRClosed, biLClosed, biRClosed, boLClosed, boRClosed;
    private float currentLevel = 0f;
    private float[] samples = new float[256];

    // Blink
    private float blinkTimer = 0f;
    private float nextBlink = 3f;
    private float blinkDuration = 0.15f;
    private float blinkTime = 0f;
    private bool isBlinking = false;

    // Brow
    private float browTimer = 0f;
    private float nextBrowMove = 2f;
    private float browTarget = 0f;
    private float browCurrent = 0f;

    void Start()
    {
        if (jawBone) jawClosed = jawBone.localRotation;
        if (lipUpperLeft) ulLClosed = lipUpperLeft.localRotation;
        if (lipUpperRight) ulRClosed = lipUpperRight.localRotation;
        if (lipUpperMiddle) ulMClosed = lipUpperMiddle.localRotation;
        if (lipCornerLeft) lcLClosed = lipCornerLeft.localRotation;
        if (lipCornerRight) lcRClosed = lipCornerRight.localRotation;
        if (eyelidUpperLeft) elLClosed = eyelidUpperLeft.localRotation;
        if (eyelidUpperRight) elRClosed = eyelidUpperRight.localRotation;
        if (browInnerLeft) biLClosed = browInnerLeft.localRotation;
        if (browInnerRight) biRClosed = browInnerRight.localRotation;
        if (browOuterLeft) boLClosed = browOuterLeft.localRotation;
        if (browOuterRight) boRClosed = browOuterRight.localRotation;

        nextBlink = Random.Range(2f, 5f);
        nextBrowMove = Random.Range(1f, 3f);
    }

    void Update()
    {
        // Lipsync
        if (audioSource != null)
        {
            audioSource.GetOutputData(samples, 0);
            float level = 0f;
            foreach (var s in samples) level += Mathf.Abs(s);
            level /= samples.Length;
            float target = Mathf.Clamp(level * sensitivity, 0f, 1f);
            currentLevel = Mathf.Lerp(currentLevel, target, Time.deltaTime * smoothing);

            if (jawBone)
                jawBone.localRotation = jawClosed * Quaternion.Euler(currentLevel * jawAngle, 0f, 0f);
            if (lipUpperLeft)
                lipUpperLeft.localRotation = ulLClosed * Quaternion.Euler(0f, 0f, currentLevel * lipUpperAngle);
            if (lipUpperRight)
                lipUpperRight.localRotation = ulRClosed * Quaternion.Euler(0f, 0f, currentLevel * lipUpperAngle * -1f);
            if (lipUpperMiddle)
                lipUpperMiddle.localRotation = ulMClosed * Quaternion.Euler(0f, 0f, currentLevel * lipUpperMiddleAngle);
            if (lipCornerLeft)
                lipCornerLeft.localRotation = lcLClosed * Quaternion.Euler(0f, 0f, currentLevel * lipCornerAngle * -1f);
            if (lipCornerRight)
                lipCornerRight.localRotation = lcRClosed * Quaternion.Euler(0f, 0f, currentLevel * lipCornerAngle);
        }

        // Blink
        blinkTimer += Time.deltaTime;
        if (!isBlinking && blinkTimer >= nextBlink)
        {
            isBlinking = true;
            blinkTime = 0f;
            blinkTimer = 0f;
            nextBlink = Random.Range(2f, 6f);
        }
        if (isBlinking)
        {
            blinkTime += Time.deltaTime;
            float t = blinkTime / blinkDuration;
            float blinkWeight = t < 0.5f ? t * 2f : (1f - t) * 2f;
            if (eyelidUpperLeft) eyelidUpperLeft.localRotation = elLClosed * Quaternion.Euler(blinkWeight * blinkAngle, 0f, 0f);
            if (eyelidUpperRight) eyelidUpperRight.localRotation = elRClosed * Quaternion.Euler(blinkWeight * blinkAngle, 0f, 0f);
            if (blinkTime >= blinkDuration) isBlinking = false;
        }
        else
        {
            if (eyelidUpperLeft) eyelidUpperLeft.localRotation = elLClosed;
            if (eyelidUpperRight) eyelidUpperRight.localRotation = elRClosed;
        }

        // Brow
        browTimer += Time.deltaTime;
        if (browTimer >= nextBrowMove)
        {
            browTimer = 0f;
            nextBrowMove = Random.Range(1.5f, 4f);
            browTarget = Random.Range(-1f, 1f);
        }
        browCurrent = Mathf.Lerp(browCurrent, browTarget, Time.deltaTime * 3f);
        if (browInnerLeft) browInnerLeft.localRotation = biLClosed * Quaternion.Euler(browCurrent * browAngle, 0f, 0f);
        if (browInnerRight) browInnerRight.localRotation = biRClosed * Quaternion.Euler(browCurrent * browAngle, 0f, 0f);
        if (browOuterLeft) browOuterLeft.localRotation = boLClosed * Quaternion.Euler(browCurrent * browAngle, 0f, 0f);
        if (browOuterRight) browOuterRight.localRotation = boRClosed * Quaternion.Euler(browCurrent * browAngle, 0f, 0f);
    }
}