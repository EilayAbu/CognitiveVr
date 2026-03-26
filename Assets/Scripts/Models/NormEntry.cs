namespace CognitiveVR.Models
{
    [System.Serializable]
    public class NormEntry
    {
        public MetricId Id;
        public float Mean;
        public float SD;
        public float BinaryHealthyRate;

        public NormEntry() { }

        public NormEntry(MetricType type, int index, float mean, float sd, float binaryRate = 0f)
        {
            Id = new MetricId(type, index);
            Mean = mean;
            SD = sd;
            BinaryHealthyRate = binaryRate;
        }

        /// <summary>
        /// Computes how many standard deviations the given value is from the healthy mean.
        /// For binary metrics, use GetBinaryDeviation instead.
        /// </summary>
        public float GetDeviation(float value, ScoringDirection direction)
        {
            if (SD <= 0f) return 0f;

            float raw = (value - Mean) / SD;

            return direction == ScoringDirection.HighIsGood ? -raw : raw;
        }

        /// <summary>
        /// For binary metrics (yes/no): returns a fixed deviation based on healthy population rate.
        /// If >=90% of healthy people do it and the patient didn't -> 2.0-2.5 SD.
        /// If ~70% of healthy people do it and the patient didn't -> 1.0-1.5 SD.
        /// </summary>
        public float GetBinaryDeviation(bool patientDidIt)
        {
            if (patientDidIt) return 0f;

            if (BinaryHealthyRate >= 0.9f) return 2.25f;
            if (BinaryHealthyRate >= 0.7f) return 1.25f;
            return 0.5f;
        }
    }
}
