using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Place on the child trigger collider inside the Toaster.
    /// Forwards toast enter/exit events to the parent ToasterController.
    /// The toast GameObject must be tagged "toast".
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ToasterTriggerZone : MonoBehaviour
    {
        [SerializeField] private bool enableDebugLogs = true;
        private ToasterController _controller;

        private void Awake()
        {
            _controller = GetComponentInParent<ToasterController>();
            if (enableDebugLogs && _controller == null)
                Debug.LogWarning($"[ToasterTriggerZone] No ToasterController found in parents for {name}.", this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("toast"))
            {
                if (enableDebugLogs)
                    Debug.Log($"[ToasterTriggerZone] Toast entered trigger: {other.name}", other);
                _controller?.NotifyToastEntered(other.gameObject);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("toast"))
            {
                if (enableDebugLogs)
                    Debug.Log($"[ToasterTriggerZone] Toast exited trigger: {other.name}", other);
                _controller?.NotifyToastExited();
            }
        }
    }
}
