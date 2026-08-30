using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Thin reference holder living on the Smartphone GameObject. The entire UI
    /// hierarchy (Canvas, ScreenBackground, ClockText, NotificationScrollView,
    /// Content, PhoneSurface) is authored as real prefab children via the
    /// CognitiveVR/Rebuild Phone Screen Prefab editor menu - no runtime canvas
    /// construction. This component just hands the baked references to the
    /// clock display and the notification manager.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneScreenController : MonoBehaviour
    {
        [Header("Baked References (assigned by Rebuild Phone Screen Prefab)")]
        [SerializeField] private TMP_Text _clockText;
        [SerializeField] private RectTransform _notificationContent;
        [SerializeField] private PhoneClockDisplay _clockDisplay;
        [SerializeField] private PhoneNotificationManager _notificationManager;

        [Header("Style (still consumed by MessagesAppScreen / WeatherAppScreen)")]
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private Color _panelColor = new Color(0.12f, 0.14f, 0.20f, 1f);
        [SerializeField] private Color _accentColor = new Color(0.25f, 0.55f, 1f, 1f);

        [Header("Pre-Authored Message GameObjects (toggled at runtime)")]
        [Tooltip("Boss/SMS message GameObject - revealed when the user opens the SMS via its button.")]
        [SerializeField] private GameObject _bossMessageObject;
        [Tooltip("Weather alert message GameObject - revealed when the user opens the alert via its button.")]
        [SerializeField] private GameObject _weatherMessageObject;
        [Tooltip("If true, both message objects are hidden on Awake.")]
        [SerializeField] private bool _hideMessagesOnAwake = true;

        [Header("Pre-Authored Open-Message Buttons (lit when a message arrives)")]
        [Tooltip("Button GameObject lit up when the plan-change SMS arrives. Clicking it opens the boss message.")]
        [SerializeField] private GameObject _bossMessageButtonObject;
        [Tooltip("Button GameObject lit up when the rain push arrives. Clicking it opens the weather message.")]
        [SerializeField] private GameObject _weatherMessageButtonObject;

        [Header("Milestone Events (hook data-collection scripts here)")]
        [Tooltip("Fired when the SMS open-message button lights up.")]
        [SerializeField] private UnityEvent _onBossMessageButtonShown;
        [Tooltip("Fired when the boss message is opened (button clicked).")]
        [SerializeField] private UnityEvent _onBossMessageOpened;
        [Tooltip("Fired when the weather open-message button lights up.")]
        [SerializeField] private UnityEvent _onWeatherMessageButtonShown;
        [Tooltip("Fired when the weather message is opened (button clicked).")]
        [SerializeField] private UnityEvent _onWeatherMessageOpened;

        /// <summary>Raised when the boss message is opened. For code subscribers.</summary>
        public event Action OnBossMessageButtonShownEvent;
        public event Action OnBossMessageOpenedEvent;
        public event Action OnBossMessageClosedEvent;
        public event Action OnWeatherMessageButtonShownEvent;
        public event Action OnWeatherMessageOpenedEvent;
        public event Action OnWeatherMessageClosedEvent;

        // Visibility state, so an event fires once per transition no matter how
        // many times the scene wiring calls Show/Hide.
        private bool _bossMessageVisible;
        private bool _weatherMessageVisible;
        private bool _bossMessageButtonVisible;
        private bool _weatherMessageButtonVisible;

        public PhoneNotificationManager NotificationManager => _notificationManager;
        public RectTransform NotificationContent => _notificationContent;
        public TMP_FontAsset FontAsset => _fontAsset;
        public Color TextColor => _textColor;
        public Color PanelColor => _panelColor;
        public Color AccentColor => _accentColor;
        public GameObject BossMessageObject => _bossMessageObject;
        public GameObject WeatherMessageObject => _weatherMessageObject;
        public GameObject BossMessageButtonObject => _bossMessageButtonObject;
        public GameObject WeatherMessageButtonObject => _weatherMessageButtonObject;

        private void Awake()
        {
            if (_clockDisplay == null) _clockDisplay = GetComponent<PhoneClockDisplay>();
            if (_notificationManager == null) _notificationManager = GetComponent<PhoneNotificationManager>();

            if (_clockDisplay != null && _clockText != null)
                _clockDisplay.SetTarget(_clockText);

            if (_notificationManager != null && _notificationContent != null)
                _notificationManager.Initialize(this, _notificationContent);

            if (_hideMessagesOnAwake)
            {
                if (_bossMessageObject != null) _bossMessageObject.SetActive(false);
                if (_weatherMessageObject != null) _weatherMessageObject.SetActive(false);
                if (_bossMessageButtonObject != null) _bossMessageButtonObject.SetActive(false);
                if (_weatherMessageButtonObject != null) _weatherMessageButtonObject.SetActive(false);
            }

            _bossMessageVisible = _bossMessageObject != null && _bossMessageObject.activeSelf;
            _weatherMessageVisible = _weatherMessageObject != null && _weatherMessageObject.activeSelf;
            _bossMessageButtonVisible = _bossMessageButtonObject != null && _bossMessageButtonObject.activeSelf;
            _weatherMessageButtonVisible = _weatherMessageButtonObject != null && _weatherMessageButtonObject.activeSelf;
        }

        // --- Open-message buttons (lit when a message arrives) ---

        public void ShowBossMessageButton()
        {
            if (_bossMessageButtonObject != null) _bossMessageButtonObject.SetActive(true);
            if (_bossMessageButtonVisible) return;

            _bossMessageButtonVisible = true;
            _onBossMessageButtonShown?.Invoke();
            OnBossMessageButtonShownEvent?.Invoke();
        }

        public void HideBossMessageButton()
        {
            if (_bossMessageButtonObject != null) _bossMessageButtonObject.SetActive(false);
            _bossMessageButtonVisible = false;
        }

        public void ShowWeatherMessageButton()
        {
            if (_weatherMessageButtonObject != null) _weatherMessageButtonObject.SetActive(true);
            if (_weatherMessageButtonVisible) return;

            _weatherMessageButtonVisible = true;
            _onWeatherMessageButtonShown?.Invoke();
            OnWeatherMessageButtonShownEvent?.Invoke();
        }

        public void HideWeatherMessageButton()
        {
            if (_weatherMessageButtonObject != null) _weatherMessageButtonObject.SetActive(false);
            _weatherMessageButtonVisible = false;
        }

        // --- Open message (invoked by the open-message button click) ---

        public void OpenBossMessage()
        {
            HideBossMessageButton();
            ShowBossMessage();
        }

        public void OpenWeatherMessage()
        {
            HideWeatherMessageButton();
            ShowWeatherMessage();
        }

        // --- Low-level message toggles ---
        // The open/closed events live here, not in Open*Message: the scene wires
        // its buttons straight to Show/Hide, so this is the only path guaranteed
        // to run for every reveal.

        public void ShowBossMessage()
        {
            if (_bossMessageObject != null) _bossMessageObject.SetActive(true);
            if (_bossMessageVisible) return;

            _bossMessageVisible = true;
            _onBossMessageOpened?.Invoke();
            OnBossMessageOpenedEvent?.Invoke();
        }

        public void HideBossMessage()
        {
            if (_bossMessageObject != null) _bossMessageObject.SetActive(false);
            if (!_bossMessageVisible) return;

            _bossMessageVisible = false;
            OnBossMessageClosedEvent?.Invoke();
        }

        public void ShowWeatherMessage()
        {
            if (_weatherMessageObject != null) _weatherMessageObject.SetActive(true);
            if (_weatherMessageVisible) return;

            _weatherMessageVisible = true;
            _onWeatherMessageOpened?.Invoke();
            OnWeatherMessageOpenedEvent?.Invoke();
        }

        public void HideWeatherMessage()
        {
            if (_weatherMessageObject != null) _weatherMessageObject.SetActive(false);
            if (!_weatherMessageVisible) return;

            _weatherMessageVisible = false;
            OnWeatherMessageClosedEvent?.Invoke();
        }

        public void HideAllMessages()
        {
            HideBossMessage();
            HideWeatherMessage();
        }
    }
}
