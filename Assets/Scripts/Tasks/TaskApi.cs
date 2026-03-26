namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Static facade for task lifecycle reporting.
    /// All calls resolve session time through TaskController and route state changes to it.
    /// CognitiveTask and game logic should use this API exclusively.
    /// </summary>
    public static class TaskApi
    {
        public static void ReportStarted(TaskType type, string message = "")
        {
            float t = TaskController.Instance.GetSessionTime();
            TaskController.Instance.MarkStarted(type, t, message);
        }

        public static void ReportProgress(TaskType type, string message = "")
        {
            float t = TaskController.Instance.GetSessionTime();
            TaskController.Instance.MarkProgress(type, t, message);
        }

        public static void ReportStepCompleted(TaskType type, string stepId, string message = "")
        {
            float t = TaskController.Instance.GetSessionTime();
            TaskController.Instance.MarkStepCompleted(type, stepId, t, message);
        }

        public static void ReportStuck(TaskType type, string reason = "")
        {
            float t = TaskController.Instance.GetSessionTime();
            TaskController.Instance.MarkStuck(type, t, reason);
        }

        public static void ReportResumed(TaskType type, string message = "")
        {
            float t = TaskController.Instance.GetSessionTime();
            TaskController.Instance.MarkResumed(type, t, message);
        }

        public static void ReportCompleted(TaskType type, string message = "")
        {
            float t = TaskController.Instance.GetSessionTime();
            TaskController.Instance.MarkCompleted(type, t, message);
        }

        public static void ReportFailed(TaskType type, string reason = "")
        {
            float t = TaskController.Instance.GetSessionTime();
            TaskController.Instance.MarkFailed(type, t, reason);
        }
    }
}
