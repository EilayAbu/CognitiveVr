using System.Collections.Generic;

namespace CognitiveVR.Models
{
    [System.Serializable]
    public class ComponentScore
    {
        public CognitiveComponentType Component;
        public float AverageDeviation;
        public int Score;
        public List<MetricRecord> ContributingMetrics = new List<MetricRecord>();
        public List<FailureRecord> Failures = new List<FailureRecord>();

        /// <summary>
        /// Conversion table from the specification document.
        /// Maps average SD deviation to a 0-100 clinical score.
        /// </summary>
        private static readonly (float maxDeviation, int score)[] ConversionTable =
        {
            (0.0f,  100),
            (0.5f,  85),
            (1.0f,  70),
            (1.5f,  55),
            (2.0f,  40),
            (2.5f,  30),
            (3.0f,  20),
        };

        public static int DeviationToScore(float averageDeviation)
        {
            if (averageDeviation <= 0f) return 100;

            for (int i = 0; i < ConversionTable.Length - 1; i++)
            {
                var (lowDev, highScore) = ConversionTable[i];
                var (highDev, lowScore) = ConversionTable[i + 1];

                if (averageDeviation >= lowDev && averageDeviation <= highDev)
                {
                    float t = (averageDeviation - lowDev) / (highDev - lowDev);
                    return UnityEngine.Mathf.RoundToInt(
                        UnityEngine.Mathf.Lerp(highScore, lowScore, t));
                }
            }

            return 20;
        }

        public void ComputeScore()
        {
            Score = DeviationToScore(AverageDeviation);
        }
    }
}
