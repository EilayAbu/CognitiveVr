namespace CognitiveVR.Models
{
    [System.Serializable]
    public class MetricId : System.IEquatable<MetricId>
    {
        public MetricType Type;
        public int Index;

        public string Code => $"{Type}_{Index}";

        public MetricId() { }

        public MetricId(MetricType type, int index)
        {
            Type = type;
            Index = index;
        }

        public bool Equals(MetricId other)
        {
            if (other == null) return false;
            return Type == other.Type && Index == other.Index;
        }

        public override bool Equals(object obj) => Equals(obj as MetricId);

        public override int GetHashCode() => (Type, Index).GetHashCode();

        public override string ToString() => Code;
    }
}
