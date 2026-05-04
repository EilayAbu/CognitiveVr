using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CognitiveVR.Phone
{
    public enum PhoneAppId
    {
        None,
        Home,
        Weather,
        Messages
    }

    /// <summary>
    /// Top-level orchestrator on the Smartphone GameObject.
    /// Auto-builds the world-space canvas hierarchy on Awake (or via context menu),
    /// mirroring the procedural-build pattern of TaskNoteVisual. Owns refs to
    /// status bar, home/weather/messages app panels and the notification layer,
    /// and exposes OpenApp / CloseApp to the rest of the phone subsystem.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneScreenController : MonoBehaviour
    {
        [Header("Canvas Build")]
        [SerializeField] private bool _buildOnAwake = true;
        [Tooltip("Logical canvas size in pixels (will be scaled to phone face by canvasLocalScale).")]
        [SerializeField] private Vector2 _canvasSize = new Vector2(540f, 1080f);
        [SerializeField] private Vector3 _canvasLocalPosition = new Vector3(0f, 0f, 0.005f);
        [SerializeField] private Vector3 _canvasLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 _canvasLocalScale = Vector3.one * 0.0002f;

        [Header("Style")]
        [SerializeField] private Color _backgroundColor = new Color(0.08f, 0.10f, 0.16f, 1f);
        [SerializeField] private Sprite _wallpaperSprite;
        [SerializeField] private Color _statusBarColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _panelColor = new Color(0.12f, 0.14f, 0.20f, 1f);
        [SerializeField] private Color _accentColor = new Color(0.25f, 0.55f, 1f, 1f);
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private TMP_FontAsset _fontAsset;

        [Header("Layout")]
        [Tooltip("Height of the system status bar at the top of the screen (px).")]
        [SerializeField] private float _statusBarHeight = 64f;
        [Tooltip("Extra vertical offset (px) for notifications under the status bar so messages don't start at the very top.")]
        [SerializeField] private float _notificationTopOffset = 32f;

        [Header("Runtime References (auto-resolved)")]
        [SerializeField] private RectTransform _statusBar;
        [SerializeField] private TMP_Text _clockText;
        [SerializeField] private RectTransform _homePanel;
        [SerializeField] private RectTransform _weatherPanel;
        [SerializeField] private RectTransform _messagesPanel;
        [SerializeField] private RectTransform _notificationLayer;
        [SerializeField] private RectTransform _notificationContent;
        [SerializeField] private PhoneClockDisplay _clockDisplay;
        [SerializeField] private PhoneNotificationManager _notificationManager;
        [SerializeField] private WeatherAppScreen _weatherApp;
        [SerializeField] private MessagesAppScreen _messagesApp;

        private const string CanvasName = "Screen";
        private const string BackgroundName = "Background";
        private const string StatusBarName = "StatusBar";
        private const string ClockTextName = "ClockText";
        private const string HomePanelName = "HomeScreen";
        private const string WeatherPanelName = "WeatherApp";
        private const string MessagesPanelName = "MessagesApp";
        private const string NotificationLayerName = "NotificationLayer";
        private const string NotificationScrollName = "NotificationScroll";
        private const string NotificationContentName = "Content";
        private const string TopOffsetSpacerName = "TopOffsetSpacer";

        private PhoneAppId _currentApp = PhoneAppId.Home;

        public PhoneAppId CurrentApp => _currentApp;
        public PhoneNotificationManager NotificationManager => _notificationManager;
        public RectTransform NotificationContent => _notificationContent;
        public TMP_FontAsset FontAsset => _fontAsset;
        public Color TextColor => _textColor;
        public Color PanelColor => _panelColor;
        public Color AccentColor => _accentColor;

        private void Awake()
        {
            if (_buildOnAwake)
                BuildOrRefresh();
        }

        private void Start()
        {
            OpenApp(PhoneAppId.Home);
        }

        [ContextMenu("Build / Refresh Phone Screen")]
        public void BuildOrRefresh()
        {
            Canvas canvas = GetOrCreateCanvas();
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();

            GetOrCreateBackground(canvasRect);
            BuildStatusBar(canvasRect);
            BuildHomePanel(canvasRect);
            BuildWeatherPanel(canvasRect);
            BuildMessagesPanel(canvasRect);
            BuildNotificationLayer(canvasRect);

            EnsureRuntimeComponents();
        }

        public void OpenApp(PhoneAppId app)
        {
            _currentApp = app;
            if (_homePanel != null) _homePanel.gameObject.SetActive(app == PhoneAppId.Home);
            if (_weatherPanel != null) _weatherPanel.gameObject.SetActive(app == PhoneAppId.Weather);
            if (_messagesPanel != null) _messagesPanel.gameObject.SetActive(app == PhoneAppId.Messages);

            if (app == PhoneAppId.Weather && _weatherApp != null)
                _weatherApp.NotifyOpened();
            if (app == PhoneAppId.Messages && _messagesApp != null)
                _messagesApp.RefreshList();
        }

        public void CloseApp()
        {
            OpenApp(PhoneAppId.Home);
        }

        #region Canvas Construction

        private Canvas GetOrCreateCanvas()
        {
            Transform existing = transform.Find(CanvasName);
            GameObject canvasGo;

            if (existing == null)
            {
                canvasGo = new GameObject(
                    CanvasName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
            }
            else
            {
                canvasGo = existing.gameObject;
            }

            RectTransform rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = _canvasSize;
            rect.localPosition = _canvasLocalPosition;
            rect.localEulerAngles = _canvasLocalEuler;
            rect.localScale = _canvasLocalScale;

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            return canvas;
        }

        private RectTransform GetOrCreateBackground(RectTransform canvasRect)
        {
            Transform existing = canvasRect.Find(BackgroundName);
            GameObject bgGo;
            if (existing == null)
            {
                bgGo = new GameObject(BackgroundName, typeof(RectTransform), typeof(Image));
                bgGo.transform.SetParent(canvasRect, false);
            }
            else
            {
                bgGo = existing.gameObject;
            }

            RectTransform rect = bgGo.GetComponent<RectTransform>();
            StretchToFill(rect);

            Image img = bgGo.GetComponent<Image>();
            img.color = _backgroundColor;
            img.sprite = _wallpaperSprite;
            img.raycastTarget = false;
            rect.SetAsFirstSibling();
            return rect;
        }

        private void BuildStatusBar(RectTransform canvasRect)
        {
            Transform existing = canvasRect.Find(StatusBarName);
            GameObject barGo;
            if (existing == null)
            {
                barGo = new GameObject(StatusBarName, typeof(RectTransform), typeof(Image));
                barGo.transform.SetParent(canvasRect, false);
            }
            else
            {
                barGo = existing.gameObject;
            }

            RectTransform barRect = barGo.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.offsetMin = new Vector2(0f, -_statusBarHeight);
            barRect.offsetMax = new Vector2(0f, 0f);

            Image barImg = barGo.GetComponent<Image>();
            barImg.color = _statusBarColor;
            barImg.raycastTarget = false;

            Transform existingClock = barGo.transform.Find(ClockTextName);
            GameObject clockGo;
            if (existingClock == null)
            {
                clockGo = new GameObject(ClockTextName, typeof(RectTransform), typeof(TextMeshProUGUI));
                clockGo.transform.SetParent(barGo.transform, false);
            }
            else
            {
                clockGo = existingClock.gameObject;
            }

            RectTransform clockRect = clockGo.GetComponent<RectTransform>();
            clockRect.anchorMin = new Vector2(0f, 0f);
            clockRect.anchorMax = new Vector2(1f, 1f);
            clockRect.offsetMin = new Vector2(20f, 0f);
            clockRect.offsetMax = new Vector2(-20f, 0f);

            TextMeshProUGUI clockTmp = clockGo.GetComponent<TextMeshProUGUI>();
            clockTmp.text = "08:52";
            clockTmp.font = _fontAsset;
            clockTmp.fontSize = 36f;
            clockTmp.color = _textColor;
            clockTmp.alignment = TextAlignmentOptions.Center;
            clockTmp.fontStyle = FontStyles.Bold;
            clockTmp.raycastTarget = false;

            _statusBar = barRect;
            _clockText = clockTmp;
        }

        private void BuildHomePanel(RectTransform canvasRect)
        {
            RectTransform panel = GetOrCreateBodyPanel(canvasRect, HomePanelName);
            _homePanel = panel;

            EnsureSingleChildLabel(panel, "HomeTitle",
                "מסך הבית", topOffset: 40f, height: 80f, fontSize: 44, isRtl: true, bold: true);

            BuildAppIcon(panel, "WeatherAppIcon", "מזג אוויר", new Vector2(-130f, -240f), PhoneAppId.Weather);
            BuildAppIcon(panel, "MessagesAppIcon", "הודעות", new Vector2(130f, -240f), PhoneAppId.Messages);
        }

        private void BuildAppIcon(RectTransform parent, string name, string label, Vector2 anchoredPosition, PhoneAppId targetApp)
        {
            Transform existing = parent.Find(name);
            GameObject iconGo;
            if (existing == null)
            {
                iconGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                iconGo.transform.SetParent(parent, false);
            }
            else
            {
                iconGo = existing.gameObject;
            }

            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(180f, 180f);
            iconRect.anchoredPosition = anchoredPosition;

            Image iconImg = iconGo.GetComponent<Image>();
            iconImg.color = _accentColor;

            Button btn = iconGo.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            PhoneAppId capturedApp = targetApp;
            btn.onClick.AddListener(() => OpenApp(capturedApp));

            Transform existingLabel = iconGo.transform.Find("Label");
            GameObject labelGo;
            if (existingLabel == null)
            {
                labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(iconGo.transform, false);
            }
            else
            {
                labelGo = existingLabel.gameObject;
            }

            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0.35f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
            labelTmp.text = label;
            labelTmp.font = _fontAsset;
            labelTmp.color = _textColor;
            labelTmp.fontSize = 28f;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.isRightToLeftText = true;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.raycastTarget = false;
        }

        private void BuildWeatherPanel(RectTransform canvasRect)
        {
            RectTransform panel = GetOrCreateBodyPanel(canvasRect, WeatherPanelName);
            _weatherPanel = panel;

            EnsureSingleChildLabel(panel, "WeatherTitle",
                "מזג אוויר", topOffset: 40f, height: 80f, fontSize: 44, isRtl: true, bold: true);

            EnsureSingleChildLabel(panel, "ForecastText",
                "גשם צפוי בהמשך היום", topOffset: 200f, height: 200f, fontSize: 50, isRtl: true, bold: false);

            EnsureBackButton(panel);
        }

        private void BuildMessagesPanel(RectTransform canvasRect)
        {
            RectTransform panel = GetOrCreateBodyPanel(canvasRect, MessagesPanelName);
            _messagesPanel = panel;

            EnsureSingleChildLabel(panel, "MessagesTitle",
                "הודעות", topOffset: 40f, height: 80f, fontSize: 44, isRtl: true, bold: true);

            EnsureMessagesScroll(panel);
            EnsureBackButton(panel);
        }

        private void EnsureMessagesScroll(RectTransform parent)
        {
            const string scrollName = "MessagesScroll";
            Transform existing = parent.Find(scrollName);
            GameObject scrollGo;
            if (existing == null)
            {
                scrollGo = new GameObject(scrollName, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
                scrollGo.transform.SetParent(parent, false);
            }
            else
            {
                scrollGo = existing.gameObject;
            }

            RectTransform scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(20f, 110f);
            scrollRect.offsetMax = new Vector2(-20f, -150f);

            Image bg = scrollGo.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);

            ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            Transform existingViewport = scrollGo.transform.Find("Viewport");
            GameObject viewportGo;
            if (existingViewport == null)
            {
                viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
                viewportGo.transform.SetParent(scrollGo.transform, false);
            }
            else
            {
                viewportGo = existingViewport.gameObject;
            }
            RectTransform vpRect = viewportGo.GetComponent<RectTransform>();
            StretchToFill(vpRect);
            Image vpImg = viewportGo.GetComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.01f);
            Mask mask = viewportGo.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            Transform existingContent = viewportGo.transform.Find("Content");
            GameObject contentGo;
            if (existingContent == null)
            {
                contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                contentGo.transform.SetParent(viewportGo.transform, false);
            }
            else
            {
                contentGo = existingContent.gameObject;
            }
            RectTransform contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRect;
            sr.content = contentRect;
        }

        private void EnsureBackButton(RectTransform parent)
        {
            const string name = "BackButton";
            Transform existing = parent.Find(name);
            GameObject btnGo;
            if (existing == null)
            {
                btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(parent, false);
            }
            else
            {
                btnGo = existing.gameObject;
            }

            RectTransform btnRect = btnGo.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0f);
            btnRect.anchorMax = new Vector2(0.5f, 0f);
            btnRect.pivot = new Vector2(0.5f, 0f);
            btnRect.sizeDelta = new Vector2(220f, 80f);
            btnRect.anchoredPosition = new Vector2(0f, 30f);

            Image btnImg = btnGo.GetComponent<Image>();
            btnImg.color = _accentColor;

            Button btn = btnGo.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(CloseApp);

            Transform existingLabel = btnGo.transform.Find("Label");
            GameObject labelGo;
            if (existingLabel == null)
            {
                labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(btnGo.transform, false);
            }
            else
            {
                labelGo = existingLabel.gameObject;
            }
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            StretchToFill(labelRect);
            TextMeshProUGUI labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
            labelTmp.text = "חזרה";
            labelTmp.font = _fontAsset;
            labelTmp.color = _textColor;
            labelTmp.fontSize = 32f;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.isRightToLeftText = true;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.raycastTarget = false;
        }

        private RectTransform GetOrCreateBodyPanel(RectTransform canvasRect, string panelName)
        {
            Transform existing = canvasRect.Find(panelName);
            GameObject panelGo;
            if (existing == null)
            {
                panelGo = new GameObject(panelName, typeof(RectTransform), typeof(Image));
                panelGo.transform.SetParent(canvasRect, false);
            }
            else
            {
                panelGo = existing.gameObject;
            }

            RectTransform rect = panelGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(0f, -_statusBarHeight);

            Image img = panelGo.GetComponent<Image>();
            img.color = _panelColor;

            return rect;
        }

        private void EnsureSingleChildLabel(RectTransform parent, string name, string text,
            float topOffset, float height, int fontSize, bool isRtl, bool bold)
        {
            Transform existing = parent.Find(name);
            GameObject go;
            if (existing == null)
            {
                go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
            }
            else
            {
                go = existing.gameObject;
            }

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(20f, -(topOffset + height));
            rect.offsetMax = new Vector2(-20f, -topOffset);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = _fontAsset;
            tmp.color = _textColor;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.isRightToLeftText = isRtl;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
        }

        private void BuildNotificationLayer(RectTransform canvasRect)
        {
            Transform existing = canvasRect.Find(NotificationLayerName);
            GameObject layerGo;
            if (existing == null)
            {
                layerGo = new GameObject(NotificationLayerName, typeof(RectTransform));
                layerGo.transform.SetParent(canvasRect, false);
            }
            else
            {
                layerGo = existing.gameObject;
            }

            RectTransform layerRect = layerGo.GetComponent<RectTransform>();
            // Layer fills below the status bar, ignoring the requested top offset spacer.
            layerRect.anchorMin = new Vector2(0f, 0f);
            layerRect.anchorMax = new Vector2(1f, 1f);
            layerRect.pivot = new Vector2(0.5f, 1f);
            layerRect.offsetMin = new Vector2(10f, 10f);
            layerRect.offsetMax = new Vector2(-10f, -_statusBarHeight);
            layerGo.transform.SetAsLastSibling();

            Transform existingScroll = layerRect.Find(NotificationScrollName);
            GameObject scrollGo;
            if (existingScroll == null)
            {
                scrollGo = new GameObject(NotificationScrollName, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
                scrollGo.transform.SetParent(layerRect, false);
            }
            else
            {
                scrollGo = existingScroll.gameObject;
            }

            RectTransform scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(0f, 0f);
            // Top inset: the requested vertical offset so notifications never start at the very top.
            scrollRect.offsetMax = new Vector2(0f, -_notificationTopOffset);

            Image scrollBg = scrollGo.GetComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0f);
            scrollBg.raycastTarget = true;

            ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            Transform existingViewport = scrollGo.transform.Find("Viewport");
            GameObject viewportGo;
            if (existingViewport == null)
            {
                viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
                viewportGo.transform.SetParent(scrollGo.transform, false);
            }
            else
            {
                viewportGo = existingViewport.gameObject;
            }
            RectTransform vpRect = viewportGo.GetComponent<RectTransform>();
            StretchToFill(vpRect);
            Image vpImg = viewportGo.GetComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.01f);
            Mask mask = viewportGo.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            Transform existingContent = viewportGo.transform.Find(NotificationContentName);
            GameObject contentGo;
            if (existingContent == null)
            {
                contentGo = new GameObject(NotificationContentName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                contentGo.transform.SetParent(viewportGo.transform, false);
            }
            else
            {
                contentGo = existingContent.gameObject;
            }
            RectTransform contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRect;
            sr.content = contentRect;

            _notificationLayer = layerRect;
            _notificationContent = contentRect;
        }

        private static void StretchToFill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        #endregion

        private void EnsureRuntimeComponents()
        {
            if (_clockDisplay == null)
            {
                _clockDisplay = GetComponent<PhoneClockDisplay>();
                if (_clockDisplay == null)
                    _clockDisplay = gameObject.AddComponent<PhoneClockDisplay>();
            }
            _clockDisplay.SetTarget(_clockText);

            if (_notificationManager == null)
            {
                _notificationManager = GetComponent<PhoneNotificationManager>();
                if (_notificationManager == null)
                    _notificationManager = gameObject.AddComponent<PhoneNotificationManager>();
            }
            _notificationManager.Initialize(this, _notificationContent);

            if (_weatherPanel != null)
            {
                _weatherApp = _weatherPanel.GetComponent<WeatherAppScreen>();
                if (_weatherApp == null)
                    _weatherApp = _weatherPanel.gameObject.AddComponent<WeatherAppScreen>();
                _weatherApp.Bind(this);
            }

            if (_messagesPanel != null)
            {
                _messagesApp = _messagesPanel.GetComponent<MessagesAppScreen>();
                if (_messagesApp == null)
                    _messagesApp = _messagesPanel.gameObject.AddComponent<MessagesAppScreen>();
                _messagesApp.Bind(this);
            }
        }
    }
}
