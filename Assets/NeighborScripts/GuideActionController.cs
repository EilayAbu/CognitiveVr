using System;
using System.Collections;
using System.Collections.Generic;
using CognitiveVR.Core;
using UnityEngine;
using UnityEngine.Events;

public class GuideActionController : MonoBehaviour
{
    // C# events for pure subscribers (e.g. GuideDataBridge). Fired at the
    // existing code points; no logic changes.
    public event Action OnMissionStarted;
    public event Action<int> OnTickPlayed;
    public event Action OnMissionDoorOpenedEvent;
    public event Action OnScenarioStarted;
    public event Action OnChoiceShown;
    public event Action<bool> OnChoiceMade;
    public event Action<bool> OnScenarioEnded;

    [Header("References")]
    [SerializeField] private GuideScenarioController guideAudio;
    [SerializeField] private GameObject choiceCanvas;
    [SerializeField] private GameObject guide;

    [Header("Timing")]
    [SerializeField] private float delayBeforeDisappear = 0.5f;

    [Header("Mission")]
    [SerializeField] private SessionTimer sessionTimer;
    [SerializeField] private string missionTriggerEventId = "neighbor_knock";
    [SerializeField] private float tickInterval = 20f;

    [Header("Visibility")]
    [Tooltip("Hide the neighbor's renderers and colliders until the mission starts. Audio, animator and scripts keep running.")]
    [SerializeField] private bool hideUntilMissionStart = true;

    [Tooltip("Door the neighbor waits behind. If it is already open when the mission starts, the conversation begins right away.")]
    [SerializeField] private DoorStateEvents missionDoor;

    [Header("Events")]
    [SerializeField] private UnityEvent onScenarioEnd;

    private Renderer[] _hiddenRenderers = new Renderer[0];
    private Collider[] _hiddenColliders = new Collider[0];

    private bool scenarioStarted;
    private bool choiceMade;
    private bool agreed;

    private bool missionStarted;
    private bool doorOpened;
    private int tickNumber;

    private void Awake()
    {
        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        if (guideAudio == null)
            guideAudio = GetComponent<GuideScenarioController>();

        if (hideUntilMissionStart && !missionStarted)
        {
            CollectGuideVisuals();
            SetGuideVisible(false);
        }
    }

    // Collects the renderers and colliders that make the neighbor visible and
    // physical. The GameObject itself stays active, because it also carries the
    // tick AudioSource, the mission logic and the data bridge.
    private void CollectGuideVisuals()
    {
        GameObject root = guide != null ? guide : gameObject;
        Transform canvasRoot = choiceCanvas != null ? choiceCanvas.transform : null;

        var renderers = new List<Renderer>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.enabled && !IsUnder(r.transform, canvasRoot))
                renderers.Add(r);
        }

        var colliders = new List<Collider>();
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c.enabled && !IsUnder(c.transform, canvasRoot))
                colliders.Add(c);
        }

        _hiddenRenderers = renderers.ToArray();
        _hiddenColliders = colliders.ToArray();
    }

    private static bool IsUnder(Transform t, Transform ancestor)
    {
        return ancestor != null && (t == ancestor || t.IsChildOf(ancestor));
    }

    private void SetGuideVisible(bool visible)
    {
        foreach (var r in _hiddenRenderers)
            if (r != null) r.enabled = visible;

        foreach (var c in _hiddenColliders)
            if (c != null) c.enabled = visible;
    }

    private void OnEnable()
    {
        ResolveSessionTimer();

        if (sessionTimer != null)
            sessionTimer.OnScheduledEventTriggered += HandleScheduledEvent;
    }

    private void OnDisable()
    {
        if (sessionTimer != null)
            sessionTimer.OnScheduledEventTriggered -= HandleScheduledEvent;
    }

    private void ResolveSessionTimer()
    {
        if (sessionTimer != null)
            return;

#if UNITY_2023_1_OR_NEWER
        sessionTimer = FindFirstObjectByType<SessionTimer>();
#else
        sessionTimer = FindObjectOfType<SessionTimer>();
#endif
    }

    private void HandleScheduledEvent(SessionTimer.ScheduledEvent evt)
    {
        if (evt != null && evt.Id == missionTriggerEventId)
            StartMission();
    }

    [ContextMenu("Start Mission (test)")]
    public void StartMission()
    {
        if (missionStarted)
            return;

        missionStarted = true;
        doorOpened = false;
        tickNumber = 0;

        SetGuideVisible(true);

        Debug.Log("Mission started - tick.");

        OnMissionStarted?.Invoke();

        if (guideAudio != null)
            guideAudio.PlayTick();

        tickNumber++;
        OnTickPlayed?.Invoke(tickNumber);

        StartCoroutine(TickReminderLoop());

        // DoorStateEvents only fires on a closed->open transition. If the door
        // was opened before the mission (and ignored), start the scenario now.
        if (missionDoor != null && missionDoor.IsOpen)
        {
            Debug.Log("Mission door already open at mission start - starting scenario.");
            OnMissionDoorOpened();
            OnDoorOpened();
        }
    }

    // Wire this into DoorStateEvents.onDoorOpened in the inspector.
    public void OnMissionDoorOpened()
    {
        if (!missionStarted)
        {
            Debug.Log("Mission door opened before mission start - ignored.");
            return;
        }

        doorOpened = true;
        Debug.Log("Mission door opened.");

        OnMissionDoorOpenedEvent?.Invoke();
    }

    private IEnumerator TickReminderLoop()
    {
        while (!doorOpened)
        {
            yield return new WaitForSeconds(tickInterval);

            if (doorOpened)
                yield break;

            if (guideAudio != null)
                guideAudio.PlayTick();

            tickNumber++;
            OnTickPlayed?.Invoke(tickNumber);
        }
    }

    public void OnDoorOpened()
    {
        if (!missionStarted)
        {
            Debug.Log("Door opened before mission start - guide scenario ignored.");
            return;
        }

        if (scenarioStarted)
            return;

        Debug.Log("Guide scenario started.");

        scenarioStarted = true;
        OnScenarioStarted?.Invoke();
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        choiceMade = false;
        agreed = false;

        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        if (guideAudio != null)
            yield return guideAudio.PlayAsk();
        else
            Debug.LogWarning("GuideActionController: Guide Audio is not assigned.");

        if (choiceCanvas != null)
        {
            choiceCanvas.SetActive(true);
            Debug.Log("Choice canvas shown.");
        }
        else
        {
            Debug.LogWarning("GuideActionController: Choice Canvas is not assigned.");
        }

        OnChoiceShown?.Invoke();

        yield return new WaitUntil(() => choiceMade);

        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        if (guideAudio != null)
        {
            if (agreed)
                yield return guideAudio.PlayAgree();
            else
                yield return guideAudio.PlayRefuse();
        }

        yield return new WaitForSeconds(delayBeforeDisappear);

        onScenarioEnd?.Invoke();
        OnScenarioEnded?.Invoke(agreed);

        if (guide != null)
        {
            //guide.SetActive(false);
            Debug.Log("Guide disappeared.");
        }
    }

    public void OnAgreeClicked()
    {
        Debug.Log("הבחור הסכים לעזור");

        agreed = true;
        choiceMade = true;

        OnChoiceMade?.Invoke(true);
    }

    public void OnRefuseClicked()
    {
        Debug.Log("הבחור לא הסכים לעזור");

        agreed = false;
        choiceMade = true;

        OnChoiceMade?.Invoke(false);
    }

    public void ResetScenario()
    {
        StopAllCoroutines();

        scenarioStarted = false;
        choiceMade = false;
        agreed = false;

        if (guideAudio != null)
            guideAudio.StopVoice();

        if (choiceCanvas != null)
            choiceCanvas.SetActive(false);

        if (guide != null)
            guide.SetActive(true);

        Debug.Log("Guide scenario reset.");
    }
}
