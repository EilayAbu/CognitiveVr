namespace CognitiveVR.Models
{
    [System.Serializable]
    public class MetricRecord
    {
        public MetricId Id;
        public float Value;
        public float Timestamp;
        public string Context;

        public MetricRecord() { }

        public MetricRecord(MetricId id, float value, float timestamp, string context = "")
        {
            Id = id;
            Value = value;
            Timestamp = timestamp;
            Context = context;
        }

        public MetricRecord(MetricType type, int index, float value, float timestamp, string context = "")
            : this(new MetricId(type, index), value, timestamp, context)
        {
        }

        public override string ToString()
        {
            return $"[{Timestamp:F1}s] {Id.Code} = {Value} ({Context})";
        }
    }
}
