using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Models
{
    [CreateAssetMenu(fileName = "NormTable", menuName = "CognitiveVR/Norm Table")]
    public class NormTable : ScriptableObject
    {
        public List<NormEntry> Entries = new List<NormEntry>();

        public NormEntry GetNorm(MetricId id)
        {
            return Entries.Find(e => e.Id != null && e.Id.Equals(id));
        }

        public NormEntry GetNorm(MetricType type, int index)
        {
            return GetNorm(new MetricId(type, index));
        }

        /// <summary>
        /// Populates the table with placeholder norms from the specification document.
        /// These are theoretical values for illustration only -- to be replaced with
        /// real pilot data from 40 healthy participants aged 20-40.
        /// </summary>
        public void PopulateWithPlaceholderNorms()
        {
            Entries.Clear();

            // Initiation
            Entries.Add(new NormEntry(MetricType.INIT, 1, mean: 6f, sd: 3f));
            Entries.Add(new NormEntry(MetricType.INIT, 2, mean: 0f, sd: 0f, binaryRate: 0.85f));

            // Monitoring
            Entries.Add(new NormEntry(MetricType.ATTEN, 1, mean: 25f, sd: 8f));
            Entries.Add(new NormEntry(MetricType.ATTEN, 2, mean: 1.5f, sd: 0.8f));
            Entries.Add(new NormEntry(MetricType.ATTEN, 3, mean: 5f, sd: 4f));
            Entries.Add(new NormEntry(MetricType.ATTEN, 4, mean: 0.4f, sd: 0.6f));
            Entries.Add(new NormEntry(MetricType.ATTEN, 5, mean: 4f, sd: 2f));

            // Working Memory
            Entries.Add(new NormEntry(MetricType.WM, 1, mean: 4.7f, sd: 0.5f));
            Entries.Add(new NormEntry(MetricType.WM, 2, mean: 0.3f, sd: 0.4f));
            Entries.Add(new NormEntry(MetricType.WM, 3, mean: 1.5f, sd: 0.7f));

            // Flexibility
            Entries.Add(new NormEntry(MetricType.FLEX, 1, mean: 8f, sd: 3f));
            Entries.Add(new NormEntry(MetricType.FLEX, 2, mean: 0f, sd: 0f, binaryRate: 0.95f));
            Entries.Add(new NormEntry(MetricType.FLEX, 3, mean: 0f, sd: 0f, binaryRate: 0.95f));
            Entries.Add(new NormEntry(MetricType.FLEX, 4, mean: 12f, sd: 4f));

            // Planning
            Entries.Add(new NormEntry(MetricType.PLAN, 1, mean: 10f, sd: 6f));
            Entries.Add(new NormEntry(MetricType.PLAN, 2, mean: 0.8f, sd: 0.7f));
            Entries.Add(new NormEntry(MetricType.PLAN, 3, mean: 8f, sd: 5f));
            Entries.Add(new NormEntry(MetricType.PLAN, 4, mean: 0.3f, sd: 0.5f));

            // Prospective Memory
            Entries.Add(new NormEntry(MetricType.PROS, 1, mean: 0f, sd: 0f, binaryRate: 0.92f));
            Entries.Add(new NormEntry(MetricType.PROS, 2, mean: 3f, sd: 2f));
            Entries.Add(new NormEntry(MetricType.PROS, 3, mean: 0f, sd: 0f, binaryRate: 0.70f));

            // Rule Monitoring
            Entries.Add(new NormEntry(MetricType.RULE, 1, mean: 0.1f, sd: 0.3f));
            Entries.Add(new NormEntry(MetricType.RULE, 2, mean: 0f, sd: 5f));
            Entries.Add(new NormEntry(MetricType.RULE, 3, mean: 0f, sd: 0f, binaryRate: 0.97f));
        }
    }
}
