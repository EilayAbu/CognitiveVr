using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CognitiveVR.Phone
{
    public enum PhoneMessageTarget
    {
        Boss,
        Weather
    }

    /// <summary>
    /// Sits on an "open message" button that lights up when a message arrives.
    /// Clicking it asks the <see cref="PhoneScreenController"/> to reveal the
    /// matching message and fires <see cref="OnPressed"/> so data-collection
    /// scripts can be wired in the inspector without touching code.
    /// </summary>
    [DisallowMultipleComponent]
    public class PhoneMessageButton : MonoBehaviour
    {
        [Header("Bindings (auto-resolved if empty)")]
        [Tooltip("Owning phone controller. Auto-resolved from parents if left empty.")]
        [SerializeField] private PhoneScreenController _phone;
        [Tooltip("UI Button on this GameObject. Auto-resolved if left empty.")]
        [SerializeField] private Button _button;

        [Header("Config")]
        [Tooltip("Which message this button opens.")]
        [SerializeField] private PhoneMessageTarget _target = PhoneMessageTarget.Boss;

        [Header("Events")]
        [Tooltip("Fired when the button is pressed (hook data-collection scripts here).")]
        public UnityEvent OnPressed;

        private void Awake()
        {
            if (_button == null) _button = GetComponent<Button>();
            if (_phone == null) _phone = GetComponentInParent<PhoneScreenController>();
            if (_phone == null)
            {
#if UNITY_2023_1_OR_NEWER
                _phone = FindFirstObjectByType<PhoneScreenController>();
#else
                _phone = FindObjectOfType<PhoneScreenController>();
#endif
            }
        }

        private void OnEnable()
        {
            if (_button != null) _button.onClick.AddListener(HandleClick);
        }

        private void OnDisable()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);
        }

        public void HandleClick()
        {
            if (_phone != null)
            {
                switch (_target)
                {
                    case PhoneMessageTarget.Boss:
                        _phone.OpenBossMessage();
                        break;
                    case PhoneMessageTarget.Weather:
                        _phone.OpenWeatherMessage();
                        break;
                }
            }

            OnPressed?.Invoke();
        }
    }
}
