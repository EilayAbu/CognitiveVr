namespace CognitiveVR.Models
{
    public enum MetricDataType
    {
        Seconds,
        Count,
        Binary,
        Categorical
    }

    public enum ScoringDirection
    {
        HighIsWeak,
        HighIsGood,
        Strategic
    }

    [System.Serializable]
    public class MetricDefinition
    {
        public MetricId Id;
        public string DisplayNameHe;
        public string DisplayNameEn;
        public string TaskContext;
        public MetricDataType DataType;
        public ScoringDirection Direction;
        public CognitiveComponentType Component;

        public MetricDefinition(
            MetricType type, int index,
            string displayHe, string displayEn,
            string taskContext,
            MetricDataType dataType,
            ScoringDirection direction,
            CognitiveComponentType component)
        {
            Id = new MetricId(type, index);
            DisplayNameHe = displayHe;
            DisplayNameEn = displayEn;
            TaskContext = taskContext;
            DataType = dataType;
            Direction = direction;
            Component = component;
        }
    }
}
