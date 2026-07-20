using System.Collections;
using CognitiveVR.Core;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Plays a door-knock sound when a scheduled <see cref="SessionTimer"/> event
    /// fires, then keeps replaying it every few seconds until the player opens the
    /// door. Door open/closed state is read from <see cref="DoorStateEvents"/>.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DoorKnockController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Reports the physical open/closed state of the door.")]
        [SerializeField] private DoorStateEvents doorStateEvents;

        [Tooltip("Session timer whose scheduled event starts the knocking.")]
        [SerializeField] private SessionTimer sessionTimer;

        [SerializeField] private AudioSource audioSource;

        [Tooltip("Knock clip played at the door (e.g. door-knock-bedroom-1).")]
        [SerializeField] private AudioClip knockClip;

        [Header("Trigger")]
        [Tooltip("Id of the SessionTimer scheduled event that starts the knocking.")]
        [SerializeField] private string triggerEventId = "neighbor_knock";

        [Header("Timing")]
        [Tooltip("Extra delay in seconds after the trigger fires before the first knock.")]
        [SerializeField] private float initialDelaySeconds = 0f;

        [Tooltip("Seconds between knocks while the door is still closed.")]
        [SerializeField] private float repeatIntervalSeconds = 10f;

        [SerializeField] private bool enableDebugLogs = true;

        private Coroutine _knockRoutine;
        private bool isOpen = false;

        private void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (doorStateEvents != null)
                doorStateEvents.DoorOpened += HandleDoorOpened;

            if (sessionTimer != null)
                sessionTimer.OnScheduledEventTriggered += HandleScheduledEvent;
        }

        private void OnDisable()
        {
            if (doorStateEvents != null)
                doorStateEvents.DoorOpened -= HandleDoorOpened;

            if (sessionTimer != null)
                sessionTimer.OnScheduledEventTriggered -= HandleScheduledEvent;

            StopKnocking();
        }

        private void HandleScheduledEvent(SessionTimer.ScheduledEvent evt)
        {
            if (evt == null || evt.Id != triggerEventId)
                return;

            if (enableDebugLogs)
                Debug.Log($"[DoorKnockController] Scheduled event '{evt.Id}' triggered, starting knocks.", this);

            StartKnocking();
        }

        /// <summary>
        /// Begins the knock sequence: waits the initial delay, then knocks
        /// repeatedly until the door is opened. Safe to call from a UnityEvent.
        /// </summary>
        public void StartKnocking()
        {
            if (_knockRoutine != null)
                return;

            if (doorStateEvents != null && doorStateEvents.IsOpen)
                return;

            _knockRoutine = StartCoroutine(KnockLoop());
        }

        /// <summary>
        /// Stops the knock sequence. Safe to call from a UnityEvent.
        /// </summary>
        public void StopKnocking()
        {
            isOpen = true;

            if (enableDebugLogs)
                Debug.Log("[DoorKnockController] StopKnocking called.", this);

            if (audioSource != null)
                audioSource.Stop();

            if (_knockRoutine == null)
                return;

            StopCoroutine(_knockRoutine);
            _knockRoutine = null;
        }

        private IEnumerator KnockLoop()
        {
            yield return new WaitForSeconds(initialDelaySeconds);

            while ((doorStateEvents == null || !doorStateEvents.IsOpen) && !isOpen)
            {
                PlayKnock();
                yield return new WaitForSeconds(repeatIntervalSeconds);
            }

            _knockRoutine = null;
        }

        private void PlayKnock()
        {
            if ((audioSource == null || knockClip == null) && !isOpen)
            {
                if (enableDebugLogs)
                    Debug.LogWarning("[DoorKnockController] Missing AudioSource or knock clip.", this);
                return;
            }
            if (isOpen) return;

            audioSource.PlayOneShot(knockClip);

            if (enableDebugLogs)
                Debug.Log("[DoorKnockController] Knock played.", this);
        }

        private void HandleDoorOpened()
        {
            if (enableDebugLogs)
                Debug.Log("[DoorKnockController] Door opened, stopping knocks.", this);

            StopKnocking();
        }
    }
}
