using System;

namespace CognitiveVR.Tasks.Window
{
    /// <summary>
    /// Possible outcomes of the window task.
    /// </summary>
    public enum WindowTaskOutcome
    {
        Closed,
        AttemptedNotClosed,
        NeverAttempted
    }

    /// <summary>
    /// Structured result of the window task, broadcast by the controller so any
    /// consumer (logging, scoring, analytics) can react without knowing its internals.
    /// </summary>
    [Serializable]
    public struct WindowTaskResult
    {
        public WindowTaskOutcome Outcome;

        /// <summary>Seconds from window open until it was closed. Valid when Outcome is Closed.</summary>
        public float TimeToClose;

        /// <summary>Seconds from window open until the first close attempt (grab). Valid when attempted.</summary>
        public float TimeToFirstAttempt;
    }
}
