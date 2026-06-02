using TMPro;
using UnityEngine;

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
        [Tooltip("Boss/SMS message GameObject - lit up when the plan-change SMS arrives.")]
        [SerializeField] private GameObject _bossMessageObject;
        [Tooltip("Weather alert message GameObject - lit up when the rain push arrives.")]
        [SerializeField] private GameObject _weatherMessageObject;
        [Tooltip("If true, both message objects are hidden on Awake.")]
        [SerializeField] private bool _hideMessagesOnAwake = true;

        public PhoneNotificationManager NotificationManager => _notificationManager;
        public RectTransform NotificationContent => _notificationContent;
        public TMP_FontAsset FontAsset => _fontAsset;
        public Color TextColor => _textColor;
        public Color PanelColor => _panelColor;
        public Color AccentColor => _accentColor;
        public GameObject BossMessageObject => _bossMessageObject;
        public GameObject WeatherMessageObject => _weatherMessageObject;

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
            }
        }

        public void ShowBossMessage()
        {
            if (_bossMessageObject != null) _bossMessageObject.SetActive(true);
        }

        public void HideBossMessage()
        {
            if (_bossMessageObject != null) _bossMessageObject.SetActive(false);
        }

        public void ShowWeatherMessage()
        {
            if (_weatherMessageObject != null) _weatherMessageObject.SetActive(true);
        }

        public void HideWeatherMessage()
        {
            if (_weatherMessageObject != null) _weatherMessageObject.SetActive(false);
        }

        public void HideAllMessages()
        {
            HideBossMessage();
            HideWeatherMessage();
        }
    }
}
