using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Lightweight inbox screen showing all SMS-style notifications received
    /// during the session. The list is rebuilt from
    /// PhoneNotificationManager.History whenever the panel is opened.
    /// </summary>
    [DisallowMultipleComponent]
    public class MessagesAppScreen : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private PhoneScreenController _phone;
        [Tooltip("VerticalLayoutGroup root that will hold the SMS rows. If empty, autoresolved at runtime.")]
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] private Color _rowColor = new Color(0.10f, 0.12f, 0.18f, 0.95f);
        [SerializeField] private Color _textColor = Color.white;

        public void Bind(PhoneScreenController phone)
        {
            _phone = phone;
            if (_fontAsset == null && phone != null) _fontAsset = phone.FontAsset;
            ResolveListContent();
        }

        public void RefreshList()
        {
            ResolveListContent();
            if (_listContent == null) return;
            if (_phone == null || _phone.NotificationManager == null) return;

            ClearChildren(_listContent);

            IReadOnlyList<PhoneNotificationData> history = _phone.NotificationManager.History;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                PhoneNotificationData data = history[i];
                if (data.Kind != PhoneNotificationKind.Sms) continue;
                BuildRow(data);
            }
        }

        private void ResolveListContent()
        {
            if (_listContent != null) return;

            Transform scroll = transform.Find("MessagesScroll/Viewport/Content");
            if (scroll != null)
                _listContent = scroll as RectTransform;
        }

        private void BuildRow(PhoneNotificationData data)
        {
            GameObject row = new GameObject($"Sms_{data.Id ?? data.CreatedAt.ToString("F1")}",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(_listContent, false);

            RectTransform rect = row.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 140f);

            LayoutElement le = row.GetComponent<LayoutElement>();
            le.minHeight = 140f;
            le.preferredHeight = 160f;
            le.flexibleWidth = 1f;

            Image img = row.GetComponent<Image>();
            img.color = _rowColor;

            GameObject titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(row.transform, false);
            RectTransform titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(12f, -50f);
            titleRect.offsetMax = new Vector2(-12f, 0f);
            TextMeshProUGUI titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
            titleTmp.text = !string.IsNullOrEmpty(data.Title) ? data.Title : "SMS";
            titleTmp.font = _fontAsset;
            titleTmp.color = _textColor;
            titleTmp.fontSize = 28f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.alignment = TextAlignmentOptions.MidlineRight;
            titleTmp.isRightToLeftText = true;
            titleTmp.raycastTarget = false;

            GameObject bodyGo = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
            bodyGo.transform.SetParent(row.transform, false);
            RectTransform bodyRect = bodyGo.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(12f, 12f);
            bodyRect.offsetMax = new Vector2(-12f, -50f);
            TextMeshProUGUI bodyTmp = bodyGo.GetComponent<TextMeshProUGUI>();
            bodyTmp.text = data.Body ?? string.Empty;
            bodyTmp.font = _fontAsset;
            bodyTmp.color = _textColor;
            bodyTmp.fontSize = 26f;
            bodyTmp.alignment = TextAlignmentOptions.TopRight;
            bodyTmp.isRightToLeftText = true;
            bodyTmp.enableWordWrapping = true;
            bodyTmp.raycastTarget = false;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }
    }
}
