using CognitiveVR.Models;

namespace CognitiveVR.Tasks
{
    [System.Serializable]
    public class TaskDefinition
    {
        public TaskType Type;
        public string Id;
        public string DisplayNameHe;
        public string DisplayNameEn;
        public CognitiveComponentType[] Components;
        public float DefaultStuckTimeout;
        public string[] StepIds;

        public TaskDefinition(
            TaskType type,
            string id,
            string displayNameHe,
            string displayNameEn,
            CognitiveComponentType[] components,
            float defaultStuckTimeout,
            string[] stepIds = null)
        {
            Type = type;
            Id = id;
            DisplayNameHe = displayNameHe;
            DisplayNameEn = displayNameEn;
            Components = components;
            DefaultStuckTimeout = defaultStuckTimeout;
            StepIds = stepIds ?? System.Array.Empty<string>();
        }

        public bool HasSteps => StepIds != null && StepIds.Length > 0;
    }
}
