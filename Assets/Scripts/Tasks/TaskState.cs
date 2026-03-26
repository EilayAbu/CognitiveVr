using System.Collections.Generic;

namespace CognitiveVR.Tasks
{
    public enum TaskStatus
    {
        NotStarted = 0,
        InProgress = 1,
        Stuck = 2,
        Completed = 3,
        Failed = 4
    }

    [System.Serializable]
    public class TaskState
    {
        public TaskType Type;
        public TaskStatus Status;
        public float StartTime;
        public float LastUpdateTime;
        public float EndTime;
        public float Duration;
        public int StuckCount;
        public float TotalStuckDuration;
        public string LastMessage;

        private float _lastStuckStartTime;
        private readonly HashSet<string> _completedSteps = new HashSet<string>();

        public bool IsCompleted => Status == TaskStatus.Completed;
        public bool IsStuck => Status == TaskStatus.Stuck;
        public bool IsRunning => Status == TaskStatus.InProgress || Status == TaskStatus.Stuck;
        public bool IsTerminal => Status == TaskStatus.Completed || Status == TaskStatus.Failed;
        public IReadOnlyCollection<string> CompletedSteps => _completedSteps;

        public TaskState(TaskType type)
        {
            Type = type;
            Status = TaskStatus.NotStarted;
            StartTime = -1f;
            LastUpdateTime = 0f;
            EndTime = -1f;
            Duration = 0f;
            StuckCount = 0;
            TotalStuckDuration = 0f;
            LastMessage = string.Empty;
            _lastStuckStartTime = -1f;
        }

        public void BeginStuck(float timestamp)
        {
            _lastStuckStartTime = timestamp;
            StuckCount++;
        }

        public void EndStuck(float timestamp)
        {
            if (_lastStuckStartTime >= 0f)
            {
                TotalStuckDuration += UnityEngine.Mathf.Max(0f, timestamp - _lastStuckStartTime);
                _lastStuckStartTime = -1f;
            }
        }

        public bool CompleteStep(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId)) return false;
            return _completedSteps.Add(stepId);
        }

        public bool IsStepCompleted(string stepId)
        {
            return _completedSteps.Contains(stepId);
        }

        public void ClearSteps()
        {
            _completedSteps.Clear();
        }

        public float GetStepCompletionRatio(TaskDefinition definition)
        {
            if (definition == null || !definition.HasSteps) return 0f;
            return (float)_completedSteps.Count / definition.StepIds.Length;
        }
    }
}
