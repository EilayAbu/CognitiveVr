using UnityEngine;

namespace CognitiveVR.Tasks.Window
{
    /// <summary>
    /// View component that logs the window task result. Listens to the controller's
    /// TaskReported event and prints a human-readable summary. Replaceable by any other
    /// consumer (analytics, UI) without modifying the controller.
    /// </summary>
    public class WindowTaskReporter : MonoBehaviour
    {
        [SerializeField] private WindowTaskController controller;

        private void OnEnable()
        {
            if (controller != null)
                controller.TaskReported += HandleReported;
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.TaskReported -= HandleReported;
        }

        private void HandleReported(WindowTaskResult result)
        {
            switch (result.Outcome)
            {
                case WindowTaskOutcome.Closed:
                    Debug.Log($"[WindowTask] Window closed in {result.TimeToClose:F1} seconds.", this);
                    break;
                case WindowTaskOutcome.AttemptedNotClosed:
                    Debug.Log($"[WindowTask] Player tried to close the window after {result.TimeToFirstAttempt:F1} seconds but did not finish.", this);
                    break;
                case WindowTaskOutcome.NeverAttempted:
                    Debug.Log("[WindowTask] Player never tried to close the window.", this);
                    break;
            }
        }
    }
}
