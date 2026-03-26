namespace CognitiveVR.Models
{
    [System.Serializable]
    public class FailureRecord
    {
        public MetricId Id;
        public string TaskName;
        public string Description;
        public float Timestamp;
        public float Duration;
        public CognitiveComponentType Component;
        public float DeviationFromNorm;

        public FailureRecord() { }

        public FailureRecord(
            MetricId id,
            string taskName,
            string description,
            float timestamp,
            float duration,
            CognitiveComponentType component,
            float deviationFromNorm)
        {
            Id = id;
            TaskName = taskName;
            Description = description;
            Timestamp = timestamp;
            Duration = duration;
            Component = component;
            DeviationFromNorm = deviationFromNorm;
        }

        public override string ToString()
        {
            return $"[{Id.Code}] {TaskName} - {Description} " +
                   $"(t={Timestamp:F1}s, dur={Duration:F1}s, dev={DeviationFromNorm:F2} SD)";
        }
    }
}
