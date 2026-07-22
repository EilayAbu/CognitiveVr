using System;
using System.Collections;
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

    [Header("Events")]
    [SerializeField] private UnityEvent onScenarioEnd;

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

    public void StartMission()
    {
        if (missionStarted)
            return;

        missionStarted = true;
        doorOpened = false;
        tickNumber = 0;

        Debug.Log("Mission started - tick.");

        OnMissionStarted?.Invoke();

        if (guideAudio != null)
            guideAudio.PlayTick();

        tickNumber++;
        OnTickPlayed?.Invoke(tickNumber);

        StartCoroutine(TickReminderLoop());
    }

    // Wire this into DoorStateEvents.onDoorOpened in the inspector.
    public void OnMissionDoorOpened()
    {
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
