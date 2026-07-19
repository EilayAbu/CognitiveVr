using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CognitiveVR.Tutorial
{
    /// <summary>
    /// Whole tutorial in one component. Put it on a "TUTORIAL" GameObject, fill the
    /// Levels array, and point your start button at StartTutorial().
    ///
    /// Each level: swaps props in/out, sets the instruction text, plays a voice line,
    /// then waits for a goal to be met.
    ///
    /// A level completes when it has collected enough SIGNALS. A signal comes from
    /// whichever of these you bothered to configure:
    ///
    ///   * CompleteLevel()   -- any UnityEvent in the scene calls it
    ///                          (poke button, InteractableUnityEventWrapper,
    ///                           RigPocketTutorial.onConnected, your own script...)
    ///   * Touch Any         -- one of the listed Grabbables gets grabbed / touched
    ///   * Zone              -- 'Must Enter Zone' transform sits inside 'Zone'
    ///   * Auto Complete     -- a plain timer, for the final level
    ///
    /// Nothing here edits your interaction components -- it only listens to them.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialFlow : MonoBehaviour
    {
        [Serializable]
        public class Level
        {
            [Tooltip("Just a label for the Inspector and the console.")]
            public string name = "Level";

            [Header("Instruction")]
            [Tooltip("Shown on the Instruction Label. Leave empty to keep the previous text.")]
            [TextArea(2, 4)] public string instruction;

            [Tooltip("Voice line, played on the shared Speaker.")]
            public AudioClip voiceClip;

            [Tooltip("Optional: use this AudioSource instead of the shared Speaker (for a positional line).")]
            public AudioSource voiceSource;

            [Header("Scene setup when this level starts")]
            public GameObject[] show;
            public GameObject[] hide;

            [Header("Goal -- fill in whichever applies")]
            [Tooltip("Grabbing / touching any one of these counts as a signal. Level 1: the table objects.")]
            public Grabbable[] touchAny;

            [Tooltip("Box / Sphere / Capsule collider (convex only). Level 2: inside the box. Level 5: the space above the chair.")]
            public Collider zone;

            [Tooltip("What has to be inside the Zone. Level 2: the doll. Level 5: CenterEyeAnchor.")]
            public Transform mustEnterZone;

            [Tooltip("How long it must stay inside before it counts.")]
            public float zoneDwell = 0.5f;

            [Tooltip("Signals needed to finish. 1 = the first thing that happens wins.")]
            public int signalsNeeded = 1;

            [Tooltip("Finish on a timer no matter what. 0 = off. Use this for the last level.")]
            public float autoCompleteAfter = 0f;

            [Header("Finish")]
            [Tooltip("The 'var time' -- pause here before the next level starts.")]
            public float delayBeforeNext = 1.5f;

            [Tooltip("Success ding, layered over the shared Speaker.")]
            public AudioClip successClip;

            [Space]
            public UnityEvent onStart;
            public UnityEvent onComplete;
        }

        [SerializeField] private Level[] levels;

        [Header("Instruction UI")]
        [Tooltip("The TextMeshPro label the player reads. World Space canvas for VR.")]
        [SerializeField] private TMP_Text instructionLabel;

        [Tooltip("Optional second label showing '3 / 6'. Leave empty to skip.")]
        [SerializeField] private TMP_Text progressLabel;

        [Tooltip("Format for the progress label. {0} = current level, {1} = total.")]
        [SerializeField] private string progressFormat = "{0} / {1}";

        [Tooltip("Switch the instruction label off while its text is empty.")]
        [SerializeField] private bool hideLabelWhenEmpty = true;

        [Header("Audio")]
        [Tooltip("One AudioSource used as the narrator for every level.")]
        [SerializeField] private AudioSource speaker;

        [Header("Flow")]
        [Tooltip("OFF = wait for your start button to call StartTutorial().")]
        [SerializeField] private bool autoStart = false;

        [Tooltip("Ignore signals for this long after a level starts, so the click that finished the last level can't leak into the next one.")]
        [SerializeField] private float armDelay = 0.35f;

        [Tooltip("Text shown once every level is done. Leave empty to keep the last level's text.")]
        [TextArea(2, 4)] [SerializeField] private string finishedInstruction;

        [SerializeField] private UnityEvent onTutorialFinished;

        [SerializeField] private bool enableDebugLogs = true;

        private readonly Dictionary<Grabbable, Action<PointerEvent>> _hooks =
            new Dictionary<Grabbable, Action<PointerEvent>>();

        private int _index = -1;
        private bool _running;
        private bool _closing;
        private int _signals;
        private float _armTime;
        private float _zoneSince = -1f;

        private Type _labelType;
        private PropertyInfo _rtlProperty;

        public bool IsRunning => _running;
        public int CurrentIndex => _index;
        public Level Current => (_index >= 0 && _index < levels.Length) ? levels[_index] : null;

        private void Start()
        {
            if (autoStart) StartTutorial();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Start button -> here.</summary>
        public void StartTutorial()
        {
            if (_running) return;
            _running = true;
            Log("started");
            Enter(0);
        }

        /// <summary>
        /// The universal "this level is done" hook. Wire ANY UnityEvent to it:
        /// poke buttons, InteractableUnityEventWrapper.WhenSelect,
        /// RigPocketTutorial.onConnected, animation events, your own scripts.
        /// </summary>
        public void CompleteLevel()
        {
            if (!Armed)
            {
                Log("signal ignored (level not armed yet)");
                return;
            }

            _signals++;
            Level level = Current;
            Log($"signal {_signals}/{Mathf.Max(1, level.signalsNeeded)}");

            if (_signals >= Mathf.Max(1, level.signalsNeeded)) Finish();
        }

        /// <summary>Debug / skip button. Ends the current level regardless of its goal.</summary>
        public void SkipLevel()
        {
            if (_running && !_closing) Finish();
        }

        /// <summary>Testing: jump straight to a level (0 = level 1).</summary>
        public void JumpTo(int index)
        {
            if (index < 0 || index >= levels.Length) return;
            Unhook();
            StopAllCoroutines();
            _running = true;
            _closing = false;
            Enter(index);
        }

        public void RestartTutorial()
        {
            Unhook();
            StopAllCoroutines();
            _running = false;
            _closing = false;
            _index = -1;
            StartTutorial();
        }

        /// <summary>Set the instruction text from anywhere (hints, retries, etc.).</summary>
        public void SetInstruction(string value)
        {
            if (instructionLabel == null) return;

            if (string.IsNullOrEmpty(value))
            {
                if (hideLabelWhenEmpty) instructionLabel.gameObject.SetActive(false);
                return;
            }

            instructionLabel.gameObject.SetActive(true);
            WriteText(instructionLabel, value);
        }

        // ------------------------------------------------------------------ flow

        private bool Armed => _running && !_closing && Current != null && Time.time >= _armTime;

        private void Enter(int index)
        {
            _index = index;

            if (_index >= levels.Length)
            {
                _running = false;
                if (!string.IsNullOrEmpty(finishedInstruction)) SetInstruction(finishedInstruction);
                if (progressLabel != null) WriteText(progressLabel, string.Empty);
                Log("tutorial finished");
                onTutorialFinished?.Invoke();
                return;
            }

            Level level = levels[_index];
            _signals = 0;
            _zoneSince = -1f;
            _closing = false;
            _armTime = Time.time + armDelay;

            SetActiveAll(level.show, true);
            SetActiveAll(level.hide, false);

            if (!string.IsNullOrEmpty(level.instruction)) SetInstruction(level.instruction);
            if (progressLabel != null)
            {
                WriteText(progressLabel, string.Format(progressFormat, _index + 1, levels.Length));
            }

            PlayVoice(level);
            Hook(level);
            level.onStart?.Invoke();

            Log($"--> level {_index + 1}/{levels.Length}: {level.name}");

            if (level.autoCompleteAfter > 0f) StartCoroutine(AutoComplete(level));
        }

        private void Finish()
        {
            if (_closing) return;
            _closing = true;

            Level level = Current;
            Unhook();

            if (level.successClip != null && speaker != null) speaker.PlayOneShot(level.successClip);
            level.onComplete?.Invoke();

            Log($"level {_index + 1} complete -- next in {level.delayBeforeNext:0.##}s");
            StartCoroutine(NextAfterDelay(level.delayBeforeNext));
        }

        private IEnumerator NextAfterDelay(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Enter(_index + 1);
        }

        private IEnumerator AutoComplete(Level level)
        {
            yield return new WaitForSeconds(level.autoCompleteAfter);
            if (_running && !_closing && Current == level)
            {
                Log("auto-completing");
                Finish();
            }
        }

        private void PlayVoice(Level level)
        {
            if (level.voiceSource != null)
            {
                level.voiceSource.Play();
                return;
            }

            if (level.voiceClip == null || speaker == null) return;

            speaker.Stop();               // cut the previous line off
            speaker.clip = level.voiceClip;
            speaker.Play();
        }

        // ------------------------------------------------------------------ zone goal

        private void Update()
        {
            if (!Armed) return;

            Level level = Current;
            if (level.zone == null || level.mustEnterZone == null) return;

            Vector3 point = level.mustEnterZone.position;
            bool inside = (level.zone.ClosestPoint(point) - point).sqrMagnitude < 0.0001f;

            if (!inside)
            {
                _zoneSince = -1f;
                return;
            }

            if (_zoneSince < 0f)
            {
                _zoneSince = Time.time;
                Log($"'{level.mustEnterZone.name}' entered the zone -- holding...");
            }

            if (Time.time - _zoneSince >= level.zoneDwell)
            {
                _zoneSince = -1f;
                CompleteLevel();
            }
        }

        // ------------------------------------------------------------------ grabbable goal

        private void Hook(Level level)
        {
            if (level.touchAny == null) return;

            foreach (Grabbable grabbable in level.touchAny)
            {
                if (grabbable == null || _hooks.ContainsKey(grabbable)) continue;

                Action<PointerEvent> handler = evt =>
                {
                    if (evt.Type == PointerEventType.Select) CompleteLevel();
                };

                _hooks[grabbable] = handler;
                grabbable.WhenPointerEventRaised += handler;
            }
        }

        private void Unhook()
        {
            foreach (KeyValuePair<Grabbable, Action<PointerEvent>> pair in _hooks)
            {
                if (pair.Key != null) pair.Key.WhenPointerEventRaised -= pair.Value;
            }
            _hooks.Clear();
        }

        private void OnDisable() => Unhook();

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Writes to a TMP label. RTLTMPro labels run their Hebrew/Arabic fixer through
        /// an 'OriginalText' property -- assigning .text via a TMP_Text reference would
        /// skip it and render the string reversed, so use that property when it exists.
        /// </summary>
        private void WriteText(TMP_Text label, string value)
        {
            if (label == null) return;

            Type type = label.GetType();
            if (type != _labelType)
            {
                _labelType = type;
                _rtlProperty = type.GetProperty("OriginalText", BindingFlags.Public | BindingFlags.Instance);
                if (_rtlProperty != null && _rtlProperty.PropertyType != typeof(string)) _rtlProperty = null;
            }

            if (_rtlProperty != null) _rtlProperty.SetValue(label, value);
            else label.text = value;
        }

        private static void SetActiveAll(GameObject[] list, bool state)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] != null) list[i].SetActive(state);
            }
        }

        private void Log(string message)
        {
            if (enableDebugLogs) Debug.Log($"[Tutorial] {message}", this);
        }
    }
}
