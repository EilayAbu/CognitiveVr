using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CognitiveVR.Phone
{
    public enum PhoneNotificationKind
    {
        Generic,
        Sms,
        WeatherAlert
    }

    [System.Serializable]
    public class PhoneNotificationData
    {
        public string Id;
        public PhoneNotificationKind Kind;
        public string Title;
        public string Body;
        public string TimestampLabel;
        public float CreatedAt;

        public PhoneNotificationData() { }

        public PhoneNotificationData(string id, PhoneNotificationKind kind, string title, string body, string timestampLabel, float createdAt)
        {
            Id = id;
            Kind = kind;
            Title = title;
            Body = body;
            TimestampLabel = timestampLabel;
            CreatedAt = createdAt;
        }
    }

    /// <summary>
    /// A single persistent notification card on the phone screen.
    /// - Stays on screen until dismissed by the user.
    /// - Body uses an inner ScrollRect so long messages can be scrolled by ray drag.
    /// - Implements IBeginDragHandler / IDragHandler / IEndDragHandler on the
    ///   header to detect a horizontal swipe, which fires OnDismissRequested.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneNotificationItem : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Bindings")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private RectTransform _swipeTarget;
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _bodyLabel;
        [SerializeField] private TMP_Text _timestampLabel;
        [SerializeField] private Image _iconImage;
        [SerializeField] private ScrollRect _bodyScroll;

        [Header("Swipe")]
        [Tooltip("Horizontal travel (in canvas-local units) required to dismiss.")]
        [SerializeField] private float _dismissDistance = 220f;
        [Tooltip("Animation duration when sliding off the screen on dismiss.")]
        [SerializeField] private float _dismissAnimSeconds = 0.18f;

        public event Action<PhoneNotificationItem> OnDismissRequested;

        public PhoneNotificationData Data { get; private set; }

        private bool _dragging;
        private Vector2 _dragStartLocal;
        private Vector2 _swipeStartAnchored;
        private bool _animatingDismiss;
        private float _dismissAnimStart;
        private Vector2 _dismissFromAnchored;
        private Vector2 _dismissToAnchored;
        private CanvasGroup _canvasGroup;

        public void ConfigureReferences(
            RectTransform root,
            RectTransform swipeTarget,
            TMP_Text title,
            TMP_Text body,
            TMP_Text timestamp,
            ScrollRect bodyScroll,
            Image icon = null)
        {
            _root = root;
            _swipeTarget = swipeTarget;
            _titleLabel = title;
            _bodyLabel = body;
            _timestampLabel = timestamp;
            _bodyScroll = bodyScroll;
            _iconImage = icon;
        }

        public void Bind(PhoneNotificationData data)
        {
            Data = data;
            if (_titleLabel != null) _titleLabel.text = data.Title;
            if (_bodyLabel != null) _bodyLabel.text = data.Body;
            if (_timestampLabel != null) _timestampLabel.text = data.TimestampLabel;

            if (_bodyScroll != null)
                _bodyScroll.verticalNormalizedPosition = 1f;
        }

        public void UpdateTimestamp(string timestamp)
        {
            if (_timestampLabel != null)
                _timestampLabel.text = timestamp;
        }

        private void Awake()
        {
            if (_root == null) _root = GetComponent<RectTransform>();
            if (_swipeTarget == null) _swipeTarget = _root;
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        private void Update()
        {
            if (!_animatingDismiss) return;

            float t = (Time.unscaledTime - _dismissAnimStart) / Mathf.Max(0.01f, _dismissAnimSeconds);
            if (t >= 1f)
            {
                _swipeTarget.anchoredPosition = _dismissToAnchored;
                _canvasGroup.alpha = 0f;
                _animatingDismiss = false;
                OnDismissRequested?.Invoke(this);
                return;
            }

            float eased = 1f - Mathf.Pow(1f - t, 3f);
            _swipeTarget.anchoredPosition = Vector2.LerpUnclamped(_dismissFromAnchored, _dismissToAnchored, eased);
            _canvasGroup.alpha = Mathf.Clamp01(1f - t);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_animatingDismiss) return;
            if (_swipeTarget == null) return;
            if (_swipeTarget.parent is not RectTransform parentRect) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out _dragStartLocal);
            _swipeStartAnchored = _swipeTarget.anchoredPosition;
            _dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || _animatingDismiss) return;
            if (_swipeTarget.parent is not RectTransform parentRect) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out Vector2 currentLocal);

            float deltaX = currentLocal.x - _dragStartLocal.x;
            _swipeTarget.anchoredPosition = new Vector2(_swipeStartAnchored.x + deltaX, _swipeStartAnchored.y);

            float fade = Mathf.Clamp01(1f - Mathf.Abs(deltaX) / _dismissDistance);
            _canvasGroup.alpha = Mathf.Lerp(0.4f, 1f, fade);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;

            float deltaX = _swipeTarget.anchoredPosition.x - _swipeStartAnchored.x;
            if (Mathf.Abs(deltaX) >= _dismissDistance)
            {
                StartDismissAnimation(Mathf.Sign(deltaX));
            }
            else
            {
                _swipeTarget.anchoredPosition = _swipeStartAnchored;
                _canvasGroup.alpha = 1f;
            }
        }

        public void RequestDismiss()
        {
            if (_animatingDismiss) return;
            StartDismissAnimation(1f);
        }

        private void StartDismissAnimation(float direction)
        {
            _animatingDismiss = true;
            _dismissAnimStart = Time.unscaledTime;
            _dismissFromAnchored = _swipeTarget.anchoredPosition;
            float offscreenX = (Mathf.Sign(direction) == 0 ? 1f : Mathf.Sign(direction)) * 1200f;
            _dismissToAnchored = new Vector2(offscreenX, _swipeStartAnchored.y);
        }
    }
}
