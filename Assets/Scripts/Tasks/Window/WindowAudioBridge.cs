using UnityEngine;

namespace CognitiveVR.Tasks.Window
{
    /// <summary>
    /// Non-invasive audio bridge for the window task. Subscribes to the
    /// <see cref="WindowTaskController"/> and raises an AudioSource's volume when the
    /// window opens, fading it back to its starting value when the window closes.
    ///
    /// Enable <see cref="scaleWithAngle"/> to make the volume track how far the window
    /// is open (via the controller's CurrentAngle) instead of a fixed open/closed step.
    /// </summary>
    public class WindowAudioBridge : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Controller to listen to. Falls back to one found in the scene.")]
        [SerializeField] private WindowTaskController controller;

        [Tooltip("AudioSource whose volume is driven. Falls back to one on this object.")]
        [SerializeField] private AudioSource audioSource;

        [Header("Volume")]
        [Tooltip("Volume when the window is open.")]
        [Range(0f, 1f)]
        [SerializeField] private float openVolume = 1f;

        [Tooltip("Seconds for the volume to fade between states.")]
        [SerializeField] private float fadeDuration = 0.75f;

        [Tooltip("If on, volume ramps smoothly with the open angle instead of a fixed step.")]
        [SerializeField] private bool scaleWithAngle = false;

        [Tooltip("Angle (deg) treated as fully open when scaling with angle.")]
        [SerializeField] private float fullOpenAngle = 60f;

        private float _startVolume;
        private float _targetVolume;

        private void Awake()
        {
            if (controller == null)
                controller = FindFirstObjectByType<WindowTaskController>();
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            _startVolume = audioSource != null ? audioSource.volume : 0f;
            _targetVolume = _startVolume;
        }

        private void OnEnable()
        {
            if (controller == null)
                return;
            controller.WindowOpened += HandleOpened;
            controller.WindowClosed += HandleClosed;
        }

        private void OnDisable()
        {
            if (controller == null)
                return;
            controller.WindowOpened -= HandleOpened;
            controller.WindowClosed -= HandleClosed;
        }

        private void HandleOpened() => _targetVolume = openVolume;
        private void HandleClosed() => _targetVolume = _startVolume;

        private void Update()
        {
            if (audioSource == null)
                return;

            // Continuous mode: map the current open angle onto the volume range.
            if (scaleWithAngle && controller != null)
            {
                float t = Mathf.Clamp01(controller.CurrentAngle / Mathf.Max(1f, fullOpenAngle));
                _targetVolume = Mathf.Lerp(_startVolume, openVolume, t);
            }

            float maxDelta = fadeDuration > 0f ? Time.deltaTime / fadeDuration : 1f;
            audioSource.volume = Mathf.MoveTowards(audioSource.volume, _targetVolume, maxDelta);
        }
    }
}