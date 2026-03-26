namespace CognitiveVR.Models
{
    [System.Serializable]
    public class EventLogEntry
    {
        public float Timestamp;
        public string EventType;
        public string Description;
        public MetricId RelatedMetric;

        public EventLogEntry() { }

        public EventLogEntry(float timestamp, string eventType, string description, MetricId relatedMetric = null)
        {
            Timestamp = timestamp;
            EventType = eventType;
            Description = description;
            RelatedMetric = relatedMetric;
        }

        public override string ToString()
        {
            string metric = RelatedMetric != null ? $" [{RelatedMetric.Code}]" : "";
            return $"[{Timestamp:F1}s] ({EventType}){metric} {Description}";
        }
    }

    public static class EventTypes
    {
        public const string Interaction = "interaction";
        public const string ScheduledEvent = "scheduled_event";
        public const string Freeze = "freeze";
        public const string RuleViolation = "rule_violation";
        public const string TaskStart = "task_start";
        public const string TaskStuck = "task_stuck";
        public const string TaskResumed = "task_resumed";
        public const string TaskComplete = "task_complete";
        public const string TaskFailed = "task_failed";
        public const string TaskProgress = "task_progress";
        public const string GazeEvent = "gaze";
    }
}
