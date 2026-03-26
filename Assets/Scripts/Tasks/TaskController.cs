using System;
using System.Collections.Generic;
using System.Linq;
using CognitiveVR.Core;
using CognitiveVR.Models;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    public class TaskController : MonoBehaviour
    {
        private static TaskController _instance;

        public static TaskController Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<TaskController>();
                if (_instance != null) return _instance;

                var go = new GameObject("TaskController");
                _instance = go.AddComponent<TaskController>();
                return _instance;
            }
        }

        [Header("References")]
        [SerializeField] private SessionTimer _sessionTimer;

        [Header("Settings")]
        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private bool _verboseLogs = true;

        private readonly Dictionary<TaskType, TaskState> _taskStates = new Dictionary<TaskType, TaskState>();
        private readonly List<EventLogEntry> _eventLog = new List<EventLogEntry>();

        public event Action<TaskState> OnTaskStateChanged;

        public IReadOnlyDictionary<TaskType, TaskState> TaskStates => _taskStates;

        #region Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (_dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnEnable()
        {
            BindSessionTimer();
        }

        private void OnDisable()
        {
            UnbindSessionTimer();
        }

        private void OnDestroy()
        {
            UnbindSessionTimer();

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region SessionTimer Integration

        public float GetSessionTime()
        {
            if (_sessionTimer != null && _sessionTimer.IsRunning)
                return _sessionTimer.ElapsedTime;

            return Time.time;
        }

        private void BindSessionTimer()
        {
            if (_sessionTimer == null)
                _sessionTimer = FindObjectOfType<SessionTimer>();

            if (_sessionTimer == null) return;

            _sessionTimer.OnSessionStarted += HandleSessionStarted;
            _sessionTimer.OnSessionEnded += HandleSessionEnded;
        }

        private void UnbindSessionTimer()
        {
            if (_sessionTimer == null) return;

            _sessionTimer.OnSessionStarted -= HandleSessionStarted;
            _sessionTimer.OnSessionEnded -= HandleSessionEnded;
        }

        private void HandleSessionStarted()
        {
            ResetAll();
        }

        private void HandleSessionEnded()
        {
            float timestamp = GetSessionTime();
            var keys = new List<TaskType>(_taskStates.Keys);

            foreach (var key in keys)
            {
                TaskState state = _taskStates[key];
                if (!state.IsTerminal && state.Status != TaskStatus.NotStarted)
                {
                    MarkFailed(key, timestamp, "Session ended");
                }
            }
        }

        #endregion

        #region Task Registration

        public TaskState RegisterTask(TaskType type)
        {
            if (_taskStates.TryGetValue(type, out TaskState existing))
                return existing;

            var state = new TaskState(type);
            _taskStates.Add(type, state);
            return state;
        }

        public bool TryGetTaskState(TaskType type, out TaskState state)
        {
            return _taskStates.TryGetValue(type, out state);
        }

        public void ResetTask(TaskType type)
        {
            if (_taskStates.ContainsKey(type))
                _taskStates.Remove(type);

            RegisterTask(type);
        }

        #endregion

        #region State Mutations

        public void MarkProgress(TaskType type, float timestamp, string message = "")
        {
            TaskState state = RegisterTask(type);
            if (state.IsTerminal || state.Status == TaskStatus.NotStarted) return;

            state.LastUpdateTime = timestamp;
            state.Duration = Mathf.Max(0f, timestamp - state.StartTime);
            state.LastMessage = message;

            NotifyStateChanged(state);
        }

        public void MarkStarted(TaskType type, float timestamp, string message = "")
        {
            TaskState state = RegisterTask(type);
            if (state.IsTerminal) return;

            state.Status = TaskStatus.InProgress;
            state.StartTime = state.StartTime < 0f ? timestamp : state.StartTime;
            state.LastUpdateTime = timestamp;
            state.EndTime = -1f;
            state.Duration = 0f;
            state.LastMessage = message;

            TaskDefinition def = TaskRegistry.Get(type);
            LogEvent(timestamp, EventTypes.TaskStart, def, message);
            NotifyStateChanged(state);
        }

        public void MarkStuck(TaskType type, float timestamp, string message = "")
        {
            TaskState state = RegisterTask(type);
            if (state.IsTerminal) return;

            if (state.StartTime < 0f)
                state.StartTime = timestamp;

            state.Status = TaskStatus.Stuck;
            state.LastUpdateTime = timestamp;
            state.Duration = Mathf.Max(0f, timestamp - state.StartTime);
            state.LastMessage = message;
            state.BeginStuck(timestamp);

            TaskDefinition def = TaskRegistry.Get(type);
            LogEvent(timestamp, EventTypes.TaskStuck, def, message);
            NotifyStateChanged(state);
        }

        public void MarkResumed(TaskType type, float timestamp, string message = "")
        {
            TaskState state = RegisterTask(type);
            if (state.IsTerminal) return;

            if (state.StartTime < 0f)
                state.StartTime = timestamp;

            state.EndStuck(timestamp);
            state.Status = TaskStatus.InProgress;
            state.LastUpdateTime = timestamp;
            state.Duration = Mathf.Max(0f, timestamp - state.StartTime);
            state.LastMessage = message;

            TaskDefinition def = TaskRegistry.Get(type);
            LogEvent(timestamp, EventTypes.TaskResumed, def, message);
            NotifyStateChanged(state);
        }

        public void MarkStepCompleted(TaskType type, string stepId, float timestamp, string message = "")
        {
            TaskState state = RegisterTask(type);
            if (state.IsTerminal) return;

            if (state.StartTime < 0f)
                state.StartTime = timestamp;

            bool isNew = state.CompleteStep(stepId);
            if (!isNew) return;

            state.LastUpdateTime = timestamp;
            state.Duration = Mathf.Max(0f, timestamp - state.StartTime);
            state.LastMessage = message;

            TaskDefinition def = TaskRegistry.Get(type);
            string desc = string.IsNullOrWhiteSpace(message)
                ? $"Step completed: {stepId}"
                : $"Step completed: {stepId} | {message}";
            LogEvent(timestamp, EventTypes.TaskProgress, def, desc);
            NotifyStateChanged(state);
        }

        public void MarkCompleted(TaskType type, float timestamp, string message = "")
        {
            TaskState state = RegisterTask(type);
            if (state.IsTerminal) return;

            if (state.StartTime < 0f)
                state.StartTime = timestamp;

            state.EndStuck(timestamp);
            state.Status = TaskStatus.Completed;
            state.EndTime = timestamp;
            state.LastUpdateTime = timestamp;
            state.Duration = Mathf.Max(0f, timestamp - state.StartTime);
            state.LastMessage = message;

            TaskDefinition def = TaskRegistry.Get(type);
            LogEvent(timestamp, EventTypes.TaskComplete, def, message);
            NotifyStateChanged(state);
        }

        public void MarkFailed(TaskType type, float timestamp, string message = "")
        {
            TaskState state = RegisterTask(type);
            if (state.IsTerminal) return;

            if (state.StartTime < 0f)
                state.StartTime = timestamp;

            state.EndStuck(timestamp);
            state.Status = TaskStatus.Failed;
            state.EndTime = timestamp;
            state.LastUpdateTime = timestamp;
            state.Duration = Mathf.Max(0f, timestamp - state.StartTime);
            state.LastMessage = message;

            TaskDefinition def = TaskRegistry.Get(type);
            LogEvent(timestamp, EventTypes.TaskFailed, def, message);
            NotifyStateChanged(state);
        }

        #endregion

        #region Query Helpers

        public List<EventLogEntry> GetEventLog()
        {
            return new List<EventLogEntry>(_eventLog);
        }

        public (int total, int completed, int failed, int inProgress) GetCompletionSummary()
        {
            int total = _taskStates.Count;
            int completed = _taskStates.Values.Count(s => s.Status == TaskStatus.Completed);
            int failed = _taskStates.Values.Count(s => s.Status == TaskStatus.Failed);
            int inProgress = _taskStates.Values.Count(s => s.IsRunning);
            return (total, completed, failed, inProgress);
        }

        public void ResetAll()
        {
            _taskStates.Clear();
            _eventLog.Clear();
        }

        #endregion

        #region Internal

        private void LogEvent(float timestamp, string eventType, TaskDefinition def, string message)
        {
            string taskLabel = def != null ? def.DisplayNameEn : "Unknown";
            string description = string.IsNullOrWhiteSpace(message)
                ? taskLabel
                : $"{taskLabel}: {message}";

            var entry = new EventLogEntry(timestamp, eventType, description);
            _eventLog.Add(entry);

            if (_verboseLogs)
            {
                Debug.Log($"[TaskController] [{eventType}] {description} at {timestamp:F1}s");
            }
        }

        private void NotifyStateChanged(TaskState state)
        {
            OnTaskStateChanged?.Invoke(state);
        }

        #endregion
    }
}
