using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Builds a yellow world-space task note with a Hebrew checklist.
    /// Attach to a GameObject that represents the physical note.
    /// </summary>
    [DisallowMultipleComponent]
    public class TaskNoteVisual : MonoBehaviour
    {
        [Header("Canvas Build")]
        [SerializeField] private bool _buildOnAwake = true;
        [SerializeField] private Vector2 _canvasSize = new Vector2(720f, 980f);
        [SerializeField] private Vector3 _canvasLocalPosition = new Vector3(0f, 0f, 0.01f);
        [SerializeField] private Vector3 _canvasLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 _canvasLocalScale = Vector3.one * 0.001f;

        [Header("Style")]
        [SerializeField] private Color _noteColor = new Color(1f, 0.92f, 0.25f, 1f);
        [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.2f);
        [SerializeField] private Vector2 _shadowOffset = new Vector2(10f, -10f);
        [SerializeField] private Color _borderColor = new Color(0.42f, 0.34f, 0.08f, 0.55f);
        [SerializeField] private float _borderThickness = 4f;
        [SerializeField] private Color _textColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField] private TMP_FontAsset _titleFontAsset;
        [SerializeField] private TMP_FontAsset _itemFontAsset;
        [SerializeField] private int _titleFontSize = 54;
        [SerializeField] private int _itemFontSize = 40;
        [SerializeField] private string _title = "משימות בוקר";

        [Header("Optional Custom Lines (if empty, inferred from TaskRegistry)")]
        [SerializeField] private List<string> _customTaskLines = new List<string>();

        private const string CanvasName = "TaskNoteCanvas";
        private const string ShadowName = "Shadow";
        private const string BackgroundName = "Background";
        private const string BorderName = "Border";
        private const string TitleName = "TitleText";
        private const string ListRootName = "ChecklistRoot";

        private void Awake()
        {
            if (_buildOnAwake)
                BuildOrRefresh();
        }

        [ContextMenu("Build / Refresh Task Note")]
        public void BuildOrRefresh()
        {
            Canvas canvas = GetOrCreateCanvas();
            RectTransform shadow = GetOrCreateShadow(canvas.transform);
            RectTransform background = GetOrCreateBackground(canvas.transform);
            RectTransform border = GetOrCreateBorder(background);
            List<string> lines = BuildTasksLines();
            TextMeshProUGUI title = GetOrCreateTitle(background);
            RectTransform listRoot = GetOrCreateChecklistRoot(background);

            shadow.SetAsFirstSibling();
            background.SetAsLastSibling();
            border.SetAsLastSibling();
            title.text = _title;
            BuildChecklistItems(listRoot, lines);
        }

        private Canvas GetOrCreateCanvas()
        {
            Transform existing = transform.Find(CanvasName);
            GameObject canvasGo;

            if (existing == null)
            {
                canvasGo = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

        private RectTransform GetOrCreateBackground(Transform canvasTransform)
        {
            Transform existing = canvasTransform.Find(BackgroundName);
            GameObject bgGo;

            if (existing == null)
            {
                bgGo = new GameObject(BackgroundName, typeof(RectTransform), typeof(Image));
                bgGo.transform.SetParent(canvasTransform, false);
            }
            else
            {
                bgGo = existing.gameObject;
            }

            RectTransform rect = bgGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 12f);
            rect.offsetMax = new Vector2(-12f, -12f);

            Image img = bgGo.GetComponent<Image>();
            img.color = _noteColor;

            return rect;
        }

        private RectTransform GetOrCreateShadow(Transform canvasTransform)
        {
            Transform existing = canvasTransform.Find(ShadowName);
            GameObject shadowGo;

            if (existing == null)
            {
                shadowGo = new GameObject(ShadowName, typeof(RectTransform), typeof(Image));
                shadowGo.transform.SetParent(canvasTransform, false);
            }
            else
            {
                shadowGo = existing.gameObject;
            }

            RectTransform rect = shadowGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f + _shadowOffset.x, 12f + _shadowOffset.y);
            rect.offsetMax = new Vector2(-12f + _shadowOffset.x, -12f + _shadowOffset.y);

            Image img = shadowGo.GetComponent<Image>();
            img.color = _shadowColor;
            img.raycastTarget = false;

            return rect;
        }

        private RectTransform GetOrCreateBorder(RectTransform background)
        {
            Transform existing = background.Find(BorderName);
            GameObject borderGo;

            if (existing == null)
            {
                borderGo = new GameObject(BorderName, typeof(RectTransform), typeof(Image));
                borderGo.transform.SetParent(background, false);
            }
            else
            {
                borderGo = existing.gameObject;
            }

            RectTransform rect = borderGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image img = borderGo.GetComponent<Image>();
            img.color = _borderColor;
            img.raycastTarget = false;

            Outline outline = borderGo.GetComponent<Outline>();
            if (outline == null)
                outline = borderGo.gameObject.AddComponent<Outline>();
            outline.effectColor = _borderColor;
            outline.effectDistance = new Vector2(_borderThickness, -_borderThickness);
            outline.useGraphicAlpha = true;

            return rect;
        }

        private TextMeshProUGUI GetOrCreateTitle(RectTransform background)
        {
            Transform existing = background.Find(TitleName);
            GameObject textGo;

            if (existing == null)
            {
                textGo = new GameObject(TitleName, typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(background, false);
            }
            else
            {
                textGo = existing.gameObject;
            }

            RectTransform rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(30f, -120f);
            rect.offsetMax = new Vector2(-30f, -20f);

            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = _titleFontAsset;
            text.fontSize = _titleFontSize;
            text.color = _textColor;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.isRightToLeftText = true;
            text.fontStyle = FontStyles.Bold;
            text.outlineColor = new Color(0f, 0f, 0f, 0.18f);
            text.outlineWidth = 0.16f;

            return text;
        }

        private RectTransform GetOrCreateChecklistRoot(RectTransform background)
        {
            Transform existing = background.Find(ListRootName);
            GameObject rootGo;

            if (existing == null)
            {
                rootGo = new GameObject(ListRootName, typeof(RectTransform));
                rootGo.transform.SetParent(background, false);
            }
            else
            {
                rootGo = existing.gameObject;
            }

            RectTransform rect = rootGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(40f, 35f);
            rect.offsetMax = new Vector2(-40f, -140f);

            return rect;
        }

        private void BuildChecklistItems(RectTransform listRoot, List<string> lines)
        {
            ClearChildren(listRoot);

            if (lines == null || lines.Count == 0)
                return;

            float height = listRoot.rect.height > 1f ? listRoot.rect.height : 700f;
            float step = height / (lines.Count + 1);

            for (int i = 0; i < lines.Count; i++)
            {
                GameObject itemGo = new GameObject($"Item_{i + 1}", typeof(RectTransform), typeof(TextMeshProUGUI));
                itemGo.transform.SetParent(listRoot, false);

                RectTransform itemRect = itemGo.GetComponent<RectTransform>();
                itemRect.anchorMin = new Vector2(0f, 1f);
                itemRect.anchorMax = new Vector2(1f, 1f);
                itemRect.pivot = new Vector2(0.5f, 0.5f);
                itemRect.offsetMin = new Vector2(0f, 0f);
                itemRect.offsetMax = new Vector2(0f, 0f);
                itemRect.sizeDelta = new Vector2(0f, 68f);

                float y = -step * (i + 1);
                itemRect.anchoredPosition = new Vector2(0f, y);

                TextMeshProUGUI itemText = itemGo.GetComponent<TextMeshProUGUI>();
                itemText.font = _itemFontAsset;
                itemText.fontSize = _itemFontSize;
                itemText.color = _textColor;
                itemText.alignment = TextAlignmentOptions.Right;
                itemText.isRightToLeftText = true;
                itemText.enableWordWrapping = true;
                itemText.fontStyle = FontStyles.Bold;
                itemText.outlineColor = new Color(0f, 0f, 0f, 0.14f);
                itemText.outlineWidth = 0.1f;
                itemText.text = $"•  {lines[i]}";
            }
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (Application.isPlaying)
                    Object.Destroy(child.gameObject);
                else
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        private List<string> BuildTasksLines()
        {
            var lines = new List<string>();

            if (_customTaskLines != null && _customTaskLines.Count > 0)
            {
                lines.AddRange(_customTaskLines);
            }
            else
            {
                foreach (TaskDefinition task in TaskRegistry.All)
                {
                    if (!string.IsNullOrWhiteSpace(task.DisplayNameHe))
                        lines.Add(task.DisplayNameHe);
                }

                lines.Add("לארוז לפי הרשימה שעל המקרר");
                lines.Add("לא להניח חפצים על הרצפה");
                lines.Add("לצאת עד 09:00");
            }

            return lines;
        }
    }
}
