using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Spawns and manages persistent notifications inside the phone screen's
    /// NotificationLayer / Content RectTransform. Notifications never auto-hide;
    /// they only disappear when the user dismisses them via swipe.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneNotificationManager : MonoBehaviour
    {
        [Header("Style")]
        [SerializeField] private Color _smsColor = new Color(0.16f, 0.30f, 0.50f, 0.98f);
        [SerializeField] private Color _weatherColor = new Color(0.20f, 0.40f, 0.55f, 0.98f);
        [SerializeField] private Color _genericColor = new Color(0.18f, 0.20f, 0.28f, 0.98f);
        [SerializeField] private Color _accentColor = new Color(0.95f, 0.85f, 0.40f, 1f);
        [SerializeField] private float _itemMinHeight = 220f;

        [Header("Optional Prefab")]
        [Tooltip("If set, this prefab is instantiated; otherwise the manager builds the item layout from script.")]
        [SerializeField] private PhoneNotificationItem _itemPrefab;

        public event Action<PhoneNotificationData> OnNotificationPushed;
        public event Action<PhoneNotificationData> OnNotificationDismissed;

        private PhoneScreenController _phone;
        private RectTransform _content;
        private readonly List<PhoneNotificationItem> _live = new List<PhoneNotificationItem>();
        private readonly List<PhoneNotificationData> _history = new List<PhoneNotificationData>();

        public IReadOnlyList<PhoneNotificationData> History => _history;

        public void Initialize(PhoneScreenController phone, RectTransform content)
        {
            _phone = phone;
            _content = content;
        }

        /// <summary>Push a notification onto the screen. Persistent until swiped away.</summary>
        public PhoneNotificationItem PushNotification(PhoneNotificationData data)
        {
            if (_content == null)
            {
                Debug.LogWarning($"[{nameof(PhoneNotificationManager)}] No content rect bound; cannot push notification.", this);
                return null;
            }

            PhoneNotificationItem item;
            if (_itemPrefab != null)
            {
                item = Instantiate(_itemPrefab, _content);
            }
            else
            {
                item = BuildDefaultItem(_content, data.Kind);
            }

            item.Bind(data);
            item.transform.SetAsFirstSibling();

            item.OnDismissRequested += HandleDismissRequested;
            _live.Add(item);
            _history.Add(data);

            OnNotificationPushed?.Invoke(data);
            return item;
        }

        public void DismissAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                PhoneNotificationItem item = _live[i];
                if (item != null) item.RequestDismiss();
            }
        }

        private void HandleDismissRequested(PhoneNotificationItem item)
        {
            if (item == null) return;
            _live.Remove(item);
            PhoneNotificationData data = item.Data;
            OnNotificationDismissed?.Invoke(data);
            if (item != null) Destroy(item.gameObject);
        }

        #region Default Item Builder

        private PhoneNotificationItem BuildDefaultItem(RectTransform parent, PhoneNotificationKind kind)
        {
            GameObject root = new GameObject($"Notification_{Time.frameCount}",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);

            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.minHeight = _itemMinHeight;
            layout.preferredHeight = _itemMinHeight;
            layout.flexibleHeight = 0f;

            Image bg = root.GetComponent<Image>();
            bg.color = ColorForKind(kind);

            // Header row
            GameObject header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(root.transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 60f);
            Image headerBg = header.GetComponent<Image>();
            headerBg.color = new Color(0f, 0f, 0f, 0.25f);

            TMP_Text titleTmp = AddTmp(header.transform, "Title",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(20f, 0f), new Vector2(-150f, 0f),
                fontSize: 30, alignment: TextAlignmentOptions.MidlineRight, isRtl: true, bold: true);
            titleTmp.color = Color.white;

            TMP_Text timestampTmp = AddTmp(header.transform, "Timestamp",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-140f, 0f), new Vector2(-20f, 0f),
                fontSize: 26, alignment: TextAlignmentOptions.MidlineLeft, isRtl: false, bold: false);
            timestampTmp.color = new Color(1f, 1f, 1f, 0.85f);

            // Body scroll area
            GameObject bodyArea = new GameObject("BodyScroll",
                typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            bodyArea.transform.SetParent(root.transform, false);
            RectTransform bodyAreaRect = bodyArea.GetComponent<RectTransform>();
            bodyAreaRect.anchorMin = new Vector2(0f, 0f);
            bodyAreaRect.anchorMax = new Vector2(1f, 1f);
            bodyAreaRect.offsetMin = new Vector2(10f, 10f);
            bodyAreaRect.offsetMax = new Vector2(-10f, -70f);
            Image bodyBg = bodyArea.GetComponent<Image>();
            bodyBg.color = new Color(0f, 0f, 0f, 0.15f);

            ScrollRect bodyScroll = bodyArea.GetComponent<ScrollRect>();
            bodyScroll.horizontal = false;
            bodyScroll.vertical = true;
            bodyScroll.movementType = ScrollRect.MovementType.Clamped;
            bodyScroll.scrollSensitivity = 30f;

            GameObject viewport = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(bodyArea.transform, false);
            RectTransform vpRect = viewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            Image vpImg = viewport.GetComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.01f);
            Mask mask = viewport.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject content = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperRight;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            bodyScroll.viewport = vpRect;
            bodyScroll.content = contentRect;

            TMP_Text bodyTmp = AddTmp(content.transform, "Body",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                fontSize: 30, alignment: TextAlignmentOptions.TopRight, isRtl: true, bold: false);
            bodyTmp.color = Color.white;
            bodyTmp.enableWordWrapping = true;
            RectTransform bodyTmpRect = bodyTmp.rectTransform;
            bodyTmpRect.anchorMin = new Vector2(0f, 1f);
            bodyTmpRect.anchorMax = new Vector2(1f, 1f);
            bodyTmpRect.pivot = new Vector2(0.5f, 1f);
            bodyTmpRect.sizeDelta = new Vector2(0f, 30f);
            LayoutElement bodyLe = bodyTmp.gameObject.AddComponent<LayoutElement>();
            bodyLe.preferredHeight = -1f;

            PhoneNotificationItem itemComponent = root.AddComponent<PhoneNotificationItem>();
            itemComponent.ConfigureReferences(
                root: rootRect,
                swipeTarget: rootRect,
                title: titleTmp,
                body: bodyTmp,
                timestamp: timestampTmp,
                bodyScroll: bodyScroll);

            return itemComponent;
        }

        private static TextMeshProUGUI AddTmp(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax,
            int fontSize, TextAlignmentOptions alignment, bool isRtl, bool bold)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.isRightToLeftText = isRtl;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        private Color ColorForKind(PhoneNotificationKind kind)
        {
            switch (kind)
            {
                case PhoneNotificationKind.Sms: return _smsColor;
                case PhoneNotificationKind.WeatherAlert: return _weatherColor;
                default: return _genericColor;
            }
        }

        #endregion
    }
}
