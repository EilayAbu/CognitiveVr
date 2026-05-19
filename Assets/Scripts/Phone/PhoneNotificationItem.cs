using System;
using TMPro;
using UnityEngine;
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
    /// Pure data-binding component on a Notification_Slot prefab. Holds
    /// references to the title / body / timestamp labels and the SwipeToDelete
    /// behaviour on the visual child. Swipe + delete-button + dismiss
    /// animation live in <see cref="SwipeToDelete"/> - this class no longer
    /// reads input.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneNotificationItem : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _bodyLabel;
        [SerializeField] private TMP_Text _timestampLabel;
        [SerializeField] private Image _iconImage;
        [SerializeField] private SwipeToDelete _swipe;

        public event Action<PhoneNotificationItem> OnDismissRequested;

        public PhoneNotificationData Data { get; private set; }
        public SwipeToDelete Swipe => _swipe;

        public void ConfigureReferences(
            TMP_Text title,
            TMP_Text body,
            TMP_Text timestamp,
            SwipeToDelete swipe,
            Image icon = null)
        {
            _titleLabel = title;
            _bodyLabel = body;
            _timestampLabel = timestamp;
            _swipe = swipe;
            _iconImage = icon;
        }

        public void Bind(PhoneNotificationData data)
        {
            Data = data;
            if (_titleLabel != null) _titleLabel.text = data?.Title ?? string.Empty;
            if (_bodyLabel != null) _bodyLabel.text = data?.Body ?? string.Empty;
            if (_timestampLabel != null) _timestampLabel.text = data?.TimestampLabel ?? string.Empty;
        }

        public void UpdateTimestamp(string timestamp)
        {
            if (_timestampLabel != null) _timestampLabel.text = timestamp;
        }

        private void OnEnable()
        {
            if (_swipe == null) _swipe = GetComponentInChildren<SwipeToDelete>(true);
            if (_swipe != null) _swipe.OnDismissedEvent += HandleSwipeDismissed;
        }

        private void OnDisable()
        {
            if (_swipe != null) _swipe.OnDismissedEvent -= HandleSwipeDismissed;
        }

        private void HandleSwipeDismissed()
        {
            OnDismissRequested?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>
        /// Triggers the same dismiss animation as a swipe. Used by the
        /// manager's DismissAll() or external callers.
        /// </summary>
        public void RequestDismiss()
        {
            if (_swipe != null) _swipe.Dismiss();
            else
            {
                OnDismissRequested?.Invoke(this);
                Destroy(gameObject);
            }
        }
    }
}
