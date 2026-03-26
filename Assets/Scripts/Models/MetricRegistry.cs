using System.Collections.Generic;
using System.Linq;

namespace CognitiveVR.Models
{
    public static class MetricRegistry
    {
        private static readonly List<MetricDefinition> _definitions = new List<MetricDefinition>
        {
            // ===== Initiation (INIT) =====
            new MetricDefinition(
                MetricType.INIT, 1,
                "זמן עד פעולה ראשונה",
                "Time to first action",
                "תחילת סצנה",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Initiation),

            new MetricDefinition(
                MetricType.INIT, 2,
                "הסתכלות על רשימה לפני פעולה",
                "Looked at list before first action",
                "רשימה על המקרר",
                MetricDataType.Binary, ScoringDirection.HighIsGood,
                CognitiveComponentType.Initiation),

            // ===== Monitoring / Sustained Attention (ATTEN) =====
            new MetricDefinition(
                MetricType.ATTEN, 1,
                "זמן עד בדיקה ראשונה של טוסטר",
                "Time to first toaster check",
                "טוסטר",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Monitoring),

            new MetricDefinition(
                MetricType.ATTEN, 2,
                "מספר בדיקות יזומות לטוסטר",
                "Number of proactive toaster checks",
                "טוסטר",
                MetricDataType.Count, ScoringDirection.HighIsGood,
                CognitiveComponentType.Monitoring),

            new MetricDefinition(
                MetricType.ATTEN, 3,
                "זמן חריגה מרגע מוכנות",
                "Overshoot time from toast ready",
                "טוסטר",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Monitoring),

            new MetricDefinition(
                MetricType.ATTEN, 4,
                "דרגת שריפה",
                "Burn severity level",
                "טוסטר",
                MetricDataType.Categorical, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Monitoring),

            new MetricDefinition(
                MetricType.ATTEN, 5,
                "זמן תגובה לעשן",
                "Smoke reaction time",
                "טוסטר",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Monitoring),

            // ===== Working Memory (WM) =====
            new MetricDefinition(
                MetricType.WM, 1,
                "מספר פריטים נכונים בתיק",
                "Correct items in bag",
                "אריזת תיק",
                MetricDataType.Count, ScoringDirection.HighIsGood,
                CognitiveComponentType.WorkingMemory),

            new MetricDefinition(
                MetricType.WM, 2,
                "מספר השמטות",
                "Number of omissions",
                "אריזת תיק",
                MetricDataType.Count, ScoringDirection.HighIsWeak,
                CognitiveComponentType.WorkingMemory),

            new MetricDefinition(
                MetricType.WM, 3,
                "בדיקה חוזרת של הרשימה",
                "List re-checks",
                "רשימה על המקרר",
                MetricDataType.Count, ScoringDirection.Strategic,
                CognitiveComponentType.WorkingMemory),

            // ===== Flexibility & Inhibition (FLEX) =====
            new MetricDefinition(
                MetricType.FLEX, 1,
                "זמן תגובה להודעת SMS",
                "SMS reaction time",
                "אירוע SMS",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Flexibility),

            new MetricDefinition(
                MetricType.FLEX, 2,
                "האם הוצא לפטופ מהתיק",
                "Laptop removed from bag",
                "אירוע SMS",
                MetricDataType.Binary, ScoringDirection.HighIsGood,
                CognitiveComponentType.Flexibility),

            new MetricDefinition(
                MetricType.FLEX, 3,
                "האם הוכנס טאבלט לתיק",
                "Tablet placed in bag",
                "אירוע SMS",
                MetricDataType.Binary, ScoringDirection.HighIsGood,
                CognitiveComponentType.Flexibility),

            new MetricDefinition(
                MetricType.FLEX, 4,
                "זמן עד השלמת החלפה",
                "Time to complete swap",
                "אירוע SMS",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Flexibility),

            // ===== Planning / Problem Solving (PLAN) =====
            new MetricDefinition(
                MetricType.PLAN, 1,
                "זמן קיפאון מול המדף הגבוה",
                "Freeze time at high shelf",
                "מדף גבוה",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Planning),

            new MetricDefinition(
                MetricType.PLAN, 2,
                "מספר ניסיונות לא יעילים",
                "Ineffective attempts",
                "מדף גבוה",
                MetricDataType.Count, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Planning),

            new MetricDefinition(
                MetricType.PLAN, 3,
                "זמן עד שימוש בכלי עזר",
                "Time to use aid tool",
                "מדף גבוה",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Planning),

            new MetricDefinition(
                MetricType.PLAN, 4,
                "רמת רמז שנדרשה",
                "Hint level required",
                "מדף גבוה",
                MetricDataType.Categorical, ScoringDirection.HighIsWeak,
                CognitiveComponentType.Planning),

            // ===== Prospective Memory (PROS) =====
            new MetricDefinition(
                MetricType.PROS, 1,
                "האם הרדיו כבוי בעת יציאה",
                "Radio off at exit",
                "יציאה מהבית",
                MetricDataType.Binary, ScoringDirection.HighIsGood,
                CognitiveComponentType.ProspectiveMemory),

            new MetricDefinition(
                MetricType.PROS, 2,
                "זמן כיבוי רדיו ביחס ליציאה",
                "Radio shutoff timing relative to exit",
                "יציאה מהבית",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.ProspectiveMemory),

            new MetricDefinition(
                MetricType.PROS, 3,
                "האם לקח מטריה",
                "Took umbrella after rain forecast",
                "יציאה מהבית",
                MetricDataType.Binary, ScoringDirection.HighIsGood,
                CognitiveComponentType.ProspectiveMemory),

            // ===== Rule Monitoring (RULE) =====
            new MetricDefinition(
                MetricType.RULE, 1,
                "מספר חפצים שהונחו על הרצפה",
                "Items placed on floor",
                "כלל הסצנה",
                MetricDataType.Count, ScoringDirection.HighIsWeak,
                CognitiveComponentType.RuleMonitoring),

            new MetricDefinition(
                MetricType.RULE, 2,
                "חריגה מזמן 5 דקות",
                "Time over 5 minute mark",
                "ניהול זמן",
                MetricDataType.Seconds, ScoringDirection.HighIsWeak,
                CognitiveComponentType.RuleMonitoring),

            new MetricDefinition(
                MetricType.RULE, 3,
                "סיום כפוי ב-7 דקות",
                "Forced session end at 7 min",
                "ניהול זמן",
                MetricDataType.Binary, ScoringDirection.HighIsWeak,
                CognitiveComponentType.RuleMonitoring),
        };

        public static IReadOnlyList<MetricDefinition> All => _definitions;

        public static MetricDefinition Get(MetricType type, int index)
        {
            return _definitions.FirstOrDefault(d => d.Id.Type == type && d.Id.Index == index);
        }

        public static MetricDefinition Get(MetricId id) => Get(id.Type, id.Index);

        public static IEnumerable<MetricDefinition> GetByComponent(CognitiveComponentType component)
        {
            return _definitions.Where(d => d.Component == component);
        }

        public static IEnumerable<MetricDefinition> GetByType(MetricType type)
        {
            return _definitions.Where(d => d.Id.Type == type);
        }

        public static int CountForType(MetricType type)
        {
            return _definitions.Count(d => d.Id.Type == type);
        }
    }
}
