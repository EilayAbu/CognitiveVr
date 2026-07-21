using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CognitiveVR.UI
{
    /// <summary>
    /// Fades the headset view to black and shows a closing message when the
    /// session ends. Hook PlayEndSequence() to SessionEndTrigger.onSessionComplete
    /// (and, if you like, Hide() to SessionEndTrigger.ResetTrigger()).
    ///
    /// Nothing else needs changing - this is a drop-on component.
    ///
    /// Two ways to use it:
    ///   1. Assign your own already-styled World Space Canvas: a CanvasGroup for
    ///      the black fade and a TMP / RTLTextMeshPro for the message. Recommended
    ///      for Hebrew, so shaping is correct.
    ///   2. Leave the refs empty and it builds a plain black overlay at runtime.
    ///      The auto-built text is plain TMP and will NOT shape Hebrew - fine as a
    ///      fallback, but for a Hebrew message assign an RTLTextMeshPro yourself.
    ///
    /// VR note: a Screen Space - Overlay canvas does not render in the headset,
    /// so the auto-built canvas is World Space and parented to the camera.
    /// </summary>
    public class SessionEndScreen : MonoBehaviour
    {
        [Header("References (optional - auto-built if left empty)")]
        [Tooltip("The full-screen black overlay's CanvasGroup. Auto-built if empty.")]
        [SerializeField] private CanvasGroup fadeGroup;
        [Tooltip("Message label. Assign an RTLTextMeshPro for correct Hebrew shaping.")]
        [SerializeField] private TMP_Text messageText;
        [Tooltip("Center eye / Main Camera. Auto-found from Camera.main if empty.")]
        [SerializeField] private Transform head;

        [Header("Message")]
        [TextArea(2, 4)]
        [SerializeField] private string message = "כל הכבוד! סיימת את המשימה";
        [SerializeField] private bool showMessage = true;

        [Header("Timing (seconds)")]
        [SerializeField] private float startDelay = 0f;
        [SerializeField] private float fadeToBlackDuration = 1.5f;
        [SerializeField] private float messageFadeInDuration = 0.75f;
        [Tooltip("How long to hold on black + message. Negative = stay forever.")]
        [SerializeField] private float holdDuration = -1f;
        [Tooltip("Fade the black back out after the hold. 0 = leave it black.")]
        [SerializeField] private float fadeBackDuration = 0f;

        [Header("Look")]
        [SerializeField] private Color fadeColor = Color.black;
        [Tooltip("Distance the auto-built overlay sits in front of the camera (m).")]
        [SerializeField] private float overlayDistance = 0.4f;

        private Coroutine _routine;
        private bool _built;

        /// <summary>Hook this to SessionEndTrigger.onSessionComplete.</summary>
        public void PlayEndSequence()
        {
            EnsureOverlay();
            if (fadeGroup == null)
            {
                Debug.LogWarning($"[{nameof(SessionEndScreen)}] No fade overlay and no camera to build one on.", this);
                return;
            }

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(FadeRoutine());
        }

        /// <summary>Clear the screen again, e.g. from SessionEndTrigger.ResetTrigger().</summary>
        public void Hide()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;

            if (fadeGroup != null)
            {
                fadeGroup.alpha = 0f;
                fadeGroup.blocksRaycasts = false;
            }
            SetTextAlpha(0f);
        }

        private IEnumerator FadeRoutine()
        {
            if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

            fadeGroup.blocksRaycasts = true;
            SetTextAlpha(0f);

            // Fade the view to black.
            yield return Lerp(fadeToBlackDuration, t => fadeGroup.alpha = Mathf.Lerp(0f, 1f, t));
            fadeGroup.alpha = 1f;

            // Reveal the message.
            if (showMessage && messageText != null)
            {
                SetMessage(message);
                yield return Lerp(messageFadeInDuration, SetTextAlpha);
                SetTextAlpha(1f);
            }

            // Hold (negative = stay on screen for good).
            if (holdDuration < 0f) yield break;
            if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

            // Optional fade back out.
            if (fadeBackDuration > 0f)
            {
                yield return Lerp(fadeBackDuration, t =>
                {
                    fadeGroup.alpha = Mathf.Lerp(1f, 0f, t);
                    SetTextAlpha(Mathf.Lerp(1f, 0f, t));
                });
            }

            fadeGroup.alpha = 0f;
            SetTextAlpha(0f);
            fadeGroup.blocksRaycasts = false;
        }

        private static IEnumerator Lerp(float duration, System.Action<float> step)
        {
            if (duration <= 0f) { step(1f); yield break; }

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                step(Mathf.Clamp01(t));
                yield return null;
            }
        }

        private void SetMessage(string s)
        {
            if (messageText == null) return;

            // RTLTextMeshPro shapes Hebrew via its OriginalText property; writing
            // straight to .text bypasses shaping. Fall back to .text for plain TMP.
            PropertyInfo prop = messageText.GetType().GetProperty("OriginalText");
            if (prop != null && prop.CanWrite) prop.SetValue(messageText, s);
            else messageText.text = s;
        }

        private void SetTextAlpha(float a)
        {
            if (messageText != null) messageText.alpha = a;
        }

        private void EnsureOverlay()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;

            if (fadeGroup != null) { _built = true; return; }
            if (_built) return;
            _built = true;

            if (head == null) return; // Nothing to attach to; PlayEndSequence warns.

            // World Space canvas parented to the camera so it follows the head.
            var canvasGO = new GameObject("SessionEndOverlay");
            canvasGO.transform.SetParent(head, false);
            canvasGO.transform.localPosition = new Vector3(0f, 0f, overlayDistance);
            canvasGO.transform.localRotation = Quaternion.identity;

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32767;

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(4f, 4f); // metres - comfortably covers the FOV
            rt.localScale = Vector3.one;

            fadeGroup = canvasGO.AddComponent<CanvasGroup>();
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;

            // Black fill stretched to fill the canvas.
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(canvasGO.transform, false);
            var img = fillGO.AddComponent<Image>();
            img.color = fadeColor;
            var fillRT = img.rectTransform;
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            // Fallback message (plain TMP - won't shape Hebrew; assign your own for that).
            if (messageText == null && showMessage)
            {
                var textGO = new GameObject("Message");
                textGO.transform.SetParent(canvasGO.transform, false);
                var tmp = textGO.AddComponent<TextMeshProUGUI>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = 0.4f; // world units
                tmp.color = Color.white;
                var textRT = tmp.rectTransform;
                textRT.anchorMin = new Vector2(0.1f, 0.35f);
                textRT.anchorMax = new Vector2(0.9f, 0.65f);
                textRT.offsetMin = Vector2.zero;
                textRT.offsetMax = Vector2.zero;
                messageText = tmp;
            }

            SetTextAlpha(0f);
        }
    }
}
