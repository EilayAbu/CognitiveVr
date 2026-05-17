using System;
using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Spawns and manages persistent notifications inside the phone screen's
    /// Content RectTransform. Each notification is an instance of
    /// <see cref="_itemPrefab"/> (the baked NotificationCard prefab); it stays
    /// on screen until the user dismisses it via swipe or the X button on the
    /// card. The procedural builder that used to live here has been removed -
    /// the prefab is the single source of truth for the visual.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneNotificationManager : MonoBehaviour
    {
        [Header("Card Prefab (assigned by Rebuild Notification Card Prefab menu)")]
        [Tooltip("The NotificationCard prefab. Its root MUST have a PhoneNotificationItem; a child MUST have a SwipeToDelete.")]
        [SerializeField] private PhoneNotificationItem _itemPrefab;

        public event Action<PhoneNotificationData> OnNotificationPushed;
        public event Action<PhoneNotificationData> OnNotificationDismissed;

        private PhoneScreenController _phone;
        private RectTransform _content;
        private readonly List<PhoneNotificationItem> _live = new List<PhoneNotificationItem>();
        private readonly List<PhoneNotificationData> _history = new List<PhoneNotificationData>();

        public IReadOnlyList<PhoneNotificationData> History => _history;
        public PhoneNotificationItem ItemPrefab => _itemPrefab;

        public void Initialize(PhoneScreenController phone, RectTransform content)
        {
            _phone = phone;
            _content = content;
        }

        /// <summary>Push a notification onto the screen. Persistent until dismissed.</summary>
        public PhoneNotificationItem PushNotification(PhoneNotificationData data)
        {
            if (_content == null)
            {
                Debug.LogWarning($"[{nameof(PhoneNotificationManager)}] No content rect bound; cannot push notification.", this);
                return null;
            }

            if (_itemPrefab == null)
            {
                Debug.LogError(
                    $"[{nameof(PhoneNotificationManager)}] No card prefab assigned. " +
                    $"Run 'CognitiveVR/Rebuild Notification Card Prefab' to bake one, then re-open this scene.",
                    this);
                return null;
            }

            PhoneNotificationItem item = Instantiate(_itemPrefab, _content);
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
        }
    }
}
