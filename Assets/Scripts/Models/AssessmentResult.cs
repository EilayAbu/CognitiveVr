using System.Collections.Generic;
using System.Linq;

namespace CognitiveVR.Models
{
    [System.Serializable]
    public class AssessmentResult
    {
        public PatientProfile Patient;
        public float SessionDuration;
        public System.DateTime SessionTimestamp;

        public List<ComponentScore> ComponentScores = new List<ComponentScore>();
        public List<MetricRecord> AllRecords = new List<MetricRecord>();
        public List<EventLogEntry> EventLog = new List<EventLogEntry>();
        public List<FailureRecord> Failures = new List<FailureRecord>();

        public int GlobalScore
        {
            get
            {
                if (ComponentScores == null || ComponentScores.Count == 0) return 0;
                return UnityEngine.Mathf.RoundToInt(
                    (float)ComponentScores.Average(c => c.Score));
            }
        }

        public ComponentScore GetComponentScore(CognitiveComponentType component)
        {
            return ComponentScores.Find(c => c.Component == component);
        }

        public IEnumerable<FailureRecord> GetFailuresByComponent(CognitiveComponentType component)
        {
            return Failures.Where(f => f.Component == component);
        }

        public string PrintSummary()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("========================================");
            sb.AppendLine($"  Assessment Result: {Patient?.FullName ?? "Unknown"}");
            sb.AppendLine($"  Date: {SessionTimestamp:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"  Duration: {SessionDuration:F1}s");
            sb.AppendLine("========================================");
            sb.AppendLine();

            foreach (var cs in ComponentScores)
            {
                sb.AppendLine($"  {cs.Component}: {cs.Score}/100 (deviation: {cs.AverageDeviation:F2} SD)");

                foreach (var f in cs.Failures)
                {
                    sb.AppendLine($"    FAIL [{f.Id.Code}] {f.TaskName}: {f.Description} " +
                                  $"(t={f.Timestamp:F1}s, duration={f.Duration:F1}s, dev={f.DeviationFromNorm:F2} SD)");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"  GLOBAL SCORE: {GlobalScore}/100");
            sb.AppendLine("========================================");

            return sb.ToString();
        }
    }
}
