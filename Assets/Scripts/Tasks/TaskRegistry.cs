using System.Collections.Generic;
using System.Linq;
using CognitiveVR.Models;

namespace CognitiveVR.Tasks
{
    public static class TaskRegistry
    {
        private static readonly List<TaskDefinition> _definitions = new List<TaskDefinition>
        {
            new TaskDefinition(
                TaskType.Toast,
                "toast",
                "הכנת טוסט",
                "Make Toast",
                new[] { CognitiveComponentType.Monitoring, CognitiveComponentType.ProspectiveMemory },
                defaultStuckTimeout: 20f,
                stepIds: new[] { "activate_toaster", "check_toaster", "remove_toast" }),

            new TaskDefinition(
                TaskType.PackBag,
                "pack_bag",
                "אריזת תיק",
                "Pack Bag",
                new[] { CognitiveComponentType.WorkingMemory, CognitiveComponentType.RuleMonitoring },
                defaultStuckTimeout: 25f,
                stepIds: new[] { "wallet", "laptop", "water_bottle", "keys", "medicine" }),

            new TaskDefinition(
                TaskType.CheckWeather,
                "check_weather",
                "בדיקת מזג אוויר",
                "Check Weather",
                new[] { CognitiveComponentType.ProspectiveMemory },
                defaultStuckTimeout: 30f,
                stepIds: new[] { "open_phone", "read_forecast", "take_umbrella" }),

            new TaskDefinition(
                TaskType.SmsSwap,
                "sms_swap",
                "החלפת לפטופ וטאבלט",
                "SMS Swap",
                new[] { CognitiveComponentType.Flexibility },
                defaultStuckTimeout: 20f,
                stepIds: new[] { "remove_laptop", "place_tablet" }),

            new TaskDefinition(
                TaskType.HighShelf,
                "high_shelf",
                "פתרון בעיית מדף גבוה",
                "High Shelf Problem",
                new[] { CognitiveComponentType.Planning },
                defaultStuckTimeout: 30f,
                stepIds: new[] { "identify_problem", "use_tool", "retrieve_item" }),

            new TaskDefinition(
                TaskType.Exit,
                "exit",
                "יציאה מהבית",
                "Exit House",
                new[] { CognitiveComponentType.ProspectiveMemory, CognitiveComponentType.RuleMonitoring },
                defaultStuckTimeout: 30f,
                stepIds: new[] { "turn_off_radio", "take_bag", "take_umbrella", "open_door" }),
        };

        public static IReadOnlyList<TaskDefinition> All => _definitions;

        public static TaskDefinition Get(TaskType type)
        {
            return _definitions.FirstOrDefault(d => d.Type == type);
        }

        public static IEnumerable<TaskDefinition> GetByComponent(CognitiveComponentType component)
        {
            return _definitions.Where(d => d.Components.Contains(component));
        }

        public static int Count => _definitions.Count;
    }
}
