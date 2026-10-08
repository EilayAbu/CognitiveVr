using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Toggles mute on the instruction/narration AudioSources.
    /// Uses AudioSource.mute (not Stop) so clips started later stay silent while muted.
    /// Hook Toggle() to the button's InteractableUnityEventWrapper.WhenSelect.
    /// </summary>
    [DisallowMultipleComponent]
    public class NarrationMuteToggle : MonoBehaviour
    {
        [SerializeField] private List<AudioSource> _narrationSources = new List<AudioSource>();

        [Header("Label")]
        [SerializeField] private TMP_Text _label;
        [SerializeField] private string _muteText = "השתק";
        [SerializeField] private string _unmuteText = "השמע";

        [SerializeField] private bool _startMuted = false;

        public bool IsMuted => _muted;

        private bool _muted;

        private void Start()
        {
            SetMuted(_startMuted);
        }

        public void Toggle()
        {
            SetMuted(!_muted);
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;

            foreach (var source in _narrationSources)
            {
                if (source != null)
                    source.mute = muted;
            }

            if (_label != null)
                _label.text = muted ? _unmuteText : _muteText;
        }
    }
}
