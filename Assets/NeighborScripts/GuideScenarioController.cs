using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

public class GuideScenarioController : MonoBehaviour
{
    [Header("Existing Components")]
    [SerializeField] private AudioSource voice;

    [Header("Voice Clips")]
    [SerializeField] private AudioClip askClip;
    [SerializeField] private AudioClip agreeClip;
    [SerializeField] private AudioClip doorClosedClip;
    [SerializeField] private AudioClip sorryClip;
    [SerializeField] private AudioClip refuseClip;

    [Header("UI")]
    [SerializeField] private GameObject choiceCanvas;

    [Header("Look / Scene Points")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform doorLookTarget;
    [SerializeField] private Transform exitPoint;

    [Header("Body Rotation")]
    [SerializeField] private float turnToPlayerDuration = 0.4f;

    [Header("Timing")]
    [SerializeField] private float delayAfterAgree = 0.8f;
    [SerializeField] private float lookAtDoorTime = 1.3f;
    [SerializeField] private float delayBeforeSorry = 0.4f;
    [SerializeField] private float delayBeforeWalkAway = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool enableKeyboardTest = true;
    [SerializeField] private bool skipWalkAwayForDebug = true;

    private bool scenarioStarted;
    private bool choiceMade;
    private bool agreed;

    private Component eyesComponent;

    private void Reset()
    {
        voice = GetComponent<AudioSource>();
    }

    private void Awake()
    {
        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main.transform;

        eyesComponent = FindEyesComponent();
    }

    private void Update()
    {
        if (enableKeyboardTest &&
            Keyboard.current != null &&
            Keyboard.current.gKey.wasPressedThisFrame)
        {
            StartScenario();
        }
    }

    public void StartScenario()
    {
        if (scenarioStarted)
            return;

        Debug.Log("Guide scenario started.");

        scenarioStarted = true;
        StartCoroutine(ScenarioRoutine());
    }

    private IEnumerator ScenarioRoutine()
    {
        choiceMade = false;
        agreed = false;

        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        yield return FacePlayer();
        SetEyesTarget(playerCamera);

        yield return PlayVoice(askClip);

        if (choiceCanvas != null)
        {
            choiceCanvas.SetActive(true);
            Debug.Log("Choice canvas shown.");
        }
        else
        {
            Debug.LogWarning("GuideScenarioController: Choice Canvas is not assigned.");
        }

        yield return new WaitUntil(() => choiceMade);

        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        if (agreed)
            yield return AgreePath();
        else
            yield return RefusePath();

        yield return new WaitForSeconds(delayBeforeWalkAway);

        if (skipWalkAwayForDebug)
        {
            Debug.Log("WalkAway skipped for debug.");
            yield break;
        }

        yield return WalkAway();
    }

    private IEnumerator AgreePath()
    {
        yield return FacePlayer();
        SetEyesTarget(playerCamera);

        yield return PlayVoice(agreeClip);

        yield return new WaitForSeconds(delayAfterAgree);

        if (doorLookTarget != null)
        {
            SetEyesTarget(doorLookTarget);
            yield return new WaitForSeconds(lookAtDoorTime);
        }
        else
        {
            Debug.LogWarning("GuideScenarioController: Door Look Target is not assigned.");
        }

        yield return PlayVoice(doorClosedClip);

        yield return new WaitForSeconds(delayBeforeSorry);

        SetEyesTarget(playerCamera);
        yield return FacePlayer();

        yield return PlayVoice(sorryClip);
    }

    private IEnumerator RefusePath()
    {
        yield return FacePlayer();
        SetEyesTarget(playerCamera);

        yield return PlayVoice(refuseClip);
    }

    private IEnumerator FacePlayer()
    {
        if (playerCamera == null)
            yield break;

        yield return TurnTowards(playerCamera.position, turnToPlayerDuration);
    }

    private IEnumerator WalkAway()
    {
        if (exitPoint == null)
        {
            Debug.LogWarning("GuideScenarioController: Exit Point is not assigned.");
            yield break;
        }

        while (Vector3.Distance(transform.position, exitPoint.position) > 0.1f)
        {
            Vector3 direction = exitPoint.position - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);

                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    5f * Time.deltaTime
                );
            }

            transform.position = Vector3.MoveTowards(
                transform.position,
                exitPoint.position,
                1.2f * Time.deltaTime
            );

            yield return null;
        }

        Debug.Log("Guide walked away.");
    }

    private IEnumerator PlayVoice(AudioClip clip)
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

    private IEnumerator TurnTowards(Vector3 targetPosition, float duration)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            yield break;

        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.LookRotation(direction);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            transform.rotation = Quaternion.Slerp(
                startRotation,
                targetRotation,
                t
            );

            yield return null;
        }

        transform.rotation = targetRotation;
    }

    private Component FindEyesComponent()
    {
        Component[] components = GetComponents<Component>();

        foreach (Component component in components)
        {
            if (component != null && component.GetType().Name == "Eyes")
            {
                Debug.Log("GuideScenarioController: Eyes component found.");
                return component;
            }
        }

        Debug.LogWarning("GuideScenarioController: Eyes component was not found on Guide.");
        return null;
    }

    private void SetEyesTarget(Transform target)
    {
        if (target == null)
            return;

        if (eyesComponent == null)
        {
            eyesComponent = FindEyesComponent();

            if (eyesComponent == null)
                return;
        }

        System.Type eyesType = eyesComponent.GetType();

        BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        FieldInfo field =
            eyesType.GetField("lookTarget", flags) ??
            eyesType.GetField("LookTarget", flags) ??
            eyesType.GetField("look_target", flags) ??
            eyesType.GetField("target", flags) ??
            eyesType.GetField("Target", flags) ??
            eyesType.GetField("look_target_transform", flags) ??
            eyesType.GetField("lookTargetTransform", flags);

        if (field != null)
        {
            field.SetValue(eyesComponent, target);
            Debug.Log("Eyes target changed to: " + target.name);
            return;
        }

        PropertyInfo property =
            eyesType.GetProperty("lookTarget", flags) ??
            eyesType.GetProperty("LookTarget", flags) ??
            eyesType.GetProperty("Target", flags);

        if (property != null && property.CanWrite)
        {
            property.SetValue(eyesComponent, target);
            Debug.Log("Eyes target changed to: " + target.name);
            return;
        }

        Debug.LogWarning("GuideScenarioController: Could not find Look Target field/property on Eyes component.");
    }

    public void OnAgreeClicked()
    {
        Debug.Log("Agree clicked.");

        agreed = true;
        choiceMade = true;
    }

    public void OnRefuseClicked()
    {
        Debug.Log("Refuse clicked.");

        agreed = false;
        choiceMade = true;
    }

    public void ResetScenario()
    {
        StopAllCoroutines();

        scenarioStarted = false;
        choiceMade = false;
        agreed = false;

        if (voice != null)
            voice.Stop();

        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        SetEyesTarget(playerCamera);

        Debug.Log("Guide scenario reset.");
    }
}