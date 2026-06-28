using System;
using UnityEngine;

namespace CognitiveVR.Tasks.Window
{
    /// <summary>
    /// MVC Model for the window task. Holds all tunable parameters in one place.
    /// Lives serialized inside <see cref="WindowTaskController"/> so it can be edited
    /// from the Inspector without touching code.
    /// </summary>
    [Serializable]
    public class WindowTaskModel
    {
        [Header("Trigger")]
        [Tooltip("Open the window automatically after OpenDelay seconds from game start.")]
        public bool AutoOpenOnStart = true;

        [Tooltip("Seconds from game start (Start) until the window opens automatically.")]
        public float OpenDelay = 5f;

        [Header("Open Motion")]
        [Tooltip("Amount of rotation around the Y axis (degrees) used to open the window. Flip the sign if the geometry is inverted.")]
        public float OpenRotationAmount = 90f;

        [Tooltip("Duration of the open animation in seconds. 0 = instant.")]
        public float OpenDuration = 1f;

        [Tooltip("Rotate around the pivot's local Y axis (true) or world Y axis (false).")]
        public bool UseLocalRotation = true;

        [Tooltip("Normalized easing curve for the open motion (x and y both 0..1).")]
        public AnimationCurve OpenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
}
