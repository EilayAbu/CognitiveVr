using System.Collections;
using System.Globalization;
using CognitiveVR.Data;
using UnityEngine;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Rings the phone when a message arrives and keeps re-ringing every
    /// <see cref="_repeatInterval"/> seconds, up to <see cref="_repeatCount"/>
    /// extra times, until the participant opens that message. Each message
    /// (boss SMS, weather alert) has its own independent ring loop. Every ring
    /// swings the phone via <see cref="PhoneRinger"/>; the ring sounds play on
    /// the repeats only, unless <see cref="_playSoundOnArrival"/> is set.
    ///
    /// Every ring is logged under category "task", event phone_ring, object
    /// BossSmsMessage or WeatherAlertMessage, value = ring number (1 = the
    /// arrival ring), so the data shows how many rings it took to get the
    /// message opened.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneRingRepeater : MonoBehaviour
    {
        [Header("Bindings (auto-resolved if empty)")]
        [SerializeField] private PhoneScreenController _phone;
        [Tooltip("Vibration animation on the phone model. Optional.")]
        [SerializeField] private PhoneRinger _ringer;

        [Header("Sound (optional - leave empty for vibration only)")]
        [Tooltip("Played (with their own clips) on every ring.")]
        [SerializeField] private AudioSource[] _ringSources;
        [Tooltip("Off by default: the arrival sound is usually already played by the controller's ButtonShown events.")]
        [SerializeField] private bool _playSoundOnArrival;

        [Header("Which Messages Repeat")]
        [SerializeField] private bool _repeatForBossMessage = true;
        [SerializeField] private bool _repeatForWeatherMessage = true;

        [Header("Repeat Settings")]
        [Tooltip("How many extra rings after the first one, if the message is still unopened.")]
        [SerializeField, Min(0)] private int _repeatCount = 3;
        [Tooltip("Seconds between one ring and the next.")]
        [SerializeField, Min(0.1f)] private float _repeatInterval = 10f;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = true;

        private const string BossLogName = "BossSmsMessage";
        private const string WeatherLogName = "WeatherAlertMessage";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly RingChannel _boss = new RingChannel(BossLogName);
        private readonly RingChannel _weather = new RingChannel(WeatherLogName);

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_phone == null) return;

            _phone.OnBossMessageButtonShownEvent += HandleBossMessageArrived;
            _phone.OnBossMessageOpenedEvent += HandleBossMessageOpened;
            _phone.OnWeatherMessageButtonShownEvent += HandleWeatherMessageArrived;
            _phone.OnWeatherMessageOpenedEvent += HandleWeatherMessageOpened;
        }

        private void OnDisable()
        {
            if (_phone != null)
            {
                _phone.OnBossMessageButtonShownEvent -= HandleBossMessageArrived;
                _phone.OnBossMessageOpenedEvent -= HandleBossMessageOpened;
                _phone.OnWeatherMessageButtonShownEvent -= HandleWeatherMessageArrived;
                _phone.OnWeatherMessageOpenedEvent -= HandleWeatherMessageOpened;
            }

            StopAllRinging();
        }

        /// <summary>Stops every active ring loop immediately.</summary>
        public void StopAllRinging()
        {
            StopChannel(_boss);
            StopChannel(_weather);
        }

        private void HandleBossMessageArrived()
        {
            if (_repeatForBossMessage) StartChannel(_boss);
        }

        private void HandleWeatherMessageArrived()
        {
            if (_repeatForWeatherMessage) StartChannel(_weather);
        }

        private void HandleBossMessageOpened() => StopChannel(_boss);
        private void HandleWeatherMessageOpened() => StopChannel(_weather);

        private void StartChannel(RingChannel channel)
        {
            if (!isActiveAndEnabled) return;

            StopChannel(channel);
            channel.Routine = StartCoroutine(RingLoop(channel));
        }

        private void StopChannel(RingChannel channel)
        {
            if (channel.Routine == null) return;

            StopCoroutine(channel.Routine);
            channel.Routine = null;

            if (_verboseLogs)
                Debug.Log($"[{nameof(PhoneRingRepeater)}] {channel.LogName} ringing stopped after {channel.RingCount} ring(s).", this);
        }

        private IEnumerator RingLoop(RingChannel channel)
        {
            channel.RingCount = 0;
            int totalRings = 1 + _repeatCount;

            while (channel.RingCount < totalRings)
            {
                RingOnce(channel);
                if (channel.RingCount >= totalRings) break;
                yield return new WaitForSeconds(_repeatInterval);
            }

            channel.Routine = null;
        }

        private void RingOnce(RingChannel channel)
        {
            channel.RingCount++;

            if (_ringer != null) _ringer.Ring();
            if (channel.RingCount > 1 || _playSoundOnArrival)
                PlayRingSounds();

            ExperimentDataManager.Instance?.Log("task", "phone_ring", channel.LogName, channel.RingCount,
                $"ring={channel.RingCount}/{1 + _repeatCount}" +
                $"|interval_s={_repeatInterval.ToString("F2", Inv)}" +
                $"|repeat={(channel.RingCount > 1 ? 1 : 0)}");

            if (_verboseLogs)
                Debug.Log($"[{nameof(PhoneRingRepeater)}] {channel.LogName} ring {channel.RingCount}/{1 + _repeatCount}.", this);
        }

        private void PlayRingSounds()
        {
            if (_ringSources == null) return;

            foreach (AudioSource source in _ringSources)
                if (source != null && source.clip != null) source.Play();
        }

        private void ResolveReferences()
        {
            if (_phone == null) _phone = GetComponent<PhoneScreenController>();
            if (_phone == null) _phone = GetComponentInParent<PhoneScreenController>();

            if (_ringer == null) _ringer = GetComponentInChildren<PhoneRinger>(true);
            if (_ringer == null) _ringer = GetComponentInParent<PhoneRinger>();
        }

        private sealed class RingChannel
        {
            public readonly string LogName;
            public Coroutine Routine;
            public int RingCount;

            public RingChannel(string logName)
            {
                LogName = logName;
            }
        }
    }
}
