using UnityEngine;

namespace CognitiveVR.Tasks.Window
{
    /// <summary>
    /// View component that plays sound in reaction to the window task events:
    /// a one-shot when the window opens and closes, plus an optional looping wind
    /// sound while the window is open. Add or remove this component without touching
    /// the controller.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class WindowAudioView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WindowTaskController controller;
        [SerializeField] private AudioSource audioSource;

        [Header("Clips")]
        [Tooltip("Played once when the window opens (e.g. window swinging open).")]
        [SerializeField] private AudioClip openClip;

        [Tooltip("Played once when the window closes.")]
        [SerializeField] private AudioClip closeClip;

        [Tooltip("Looping wind sound while the window is open.")]
        [SerializeField] private AudioClip windLoopClip;

        [SerializeField] private bool playWindWhileOpen = true;

        [Range(0f, 1f)]
        [SerializeField] private float windVolume = 1f;

        private void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.WindowOpened += HandleOpened;
                controller.WindowClosed += HandleClosed;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.WindowOpened -= HandleOpened;
                controller.WindowClosed -= HandleClosed;
            }
        }

        public void HandleOpened()
        {
            if (openClip != null)
                audioSource.PlayOneShot(openClip);

            if (playWindWhileOpen && windLoopClip != null)
            {
                audioSource.clip = windLoopClip;
                audioSource.loop = true;
                audioSource.volume = windVolume;
                audioSource.Play();
            }
        }

        private void HandleClosed()
        {
            if (audioSource.loop && audioSource.clip == windLoopClip)
            {
                audioSource.Stop();
                audioSource.loop = false;
                audioSource.clip = null;
            }

            if (closeClip != null)
                audioSource.PlayOneShot(closeClip);
        }
    }
}
