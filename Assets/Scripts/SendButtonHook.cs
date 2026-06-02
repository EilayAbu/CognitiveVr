using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;

public class SendButtonHook : MonoBehaviour
{
    [SerializeField] private QuestionnaireSendLogger logger;
    [SerializeField] private bool debugLogs = true;

    private InteractableUnityEventWrapper _interactableWrapper;
    private Button _uiButton;

    private void Awake()
    {
        _interactableWrapper = GetComponent<InteractableUnityEventWrapper>();
        _uiButton = GetComponent<Button>();

        if (logger == null)
            logger = GetComponentInParent<QuestionnaireSendLogger>();

        if (debugLogs)
            Debug.Log($"[SendButtonHook] Awake on {name} | persistentDataPath={Application.persistentDataPath} | logger={(logger ? logger.name : "NULL")}");
    }

    private void OnEnable()
    {
        if (_interactableWrapper != null)
            _interactableWrapper.WhenSelect.AddListener(OnSend);

        if (_uiButton != null)
            _uiButton.onClick.AddListener(OnSend);
    }

    private void OnDisable()
    {
        if (_interactableWrapper != null)
            _interactableWrapper.WhenSelect.RemoveListener(OnSend);

        if (_uiButton != null)
            _uiButton.onClick.RemoveListener(OnSend);
    }

    private void OnSend()
    {
        if (debugLogs) Debug.Log("[SendButtonHook] SEND fired ✅");

        if (logger == null)
        {
            Debug.LogError("[SendButtonHook] No QuestionnaireSendLogger found in parents.");
            return;
        }

        logger.OnSendPressed();
    }
}