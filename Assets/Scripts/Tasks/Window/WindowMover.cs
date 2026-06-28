using System.Collections;
using UnityEngine;

namespace CognitiveVR.Tasks.Window
{
    /// <summary>
    /// View component that performs the physical window motion. Listens for the
    /// controller's RequestOpen event and rotates the pivot around its Y axis using
    /// the parameters from <see cref="WindowTaskModel"/>.
    /// </summary>
    public class WindowMover : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WindowTaskController controller;

        [Tooltip("The window pivot to rotate when opening. Falls back to this transform.")]
        [SerializeField] private Transform windowPivot;

        private Coroutine _rotateRoutine;

        private void Awake()
        {
            if (windowPivot == null)
                windowPivot = transform;
        }

        private void OnEnable()
        {
            if (controller != null)
                controller.RequestOpen += Open;
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.RequestOpen -= Open;
        }

        /// <summary>
        /// Opens the window using the controller's model parameters. Also exposed publicly
        /// so it can be wired manually to a UnityEvent in the Inspector.
        /// </summary>
        public void Open()
        {
            if (windowPivot == null || controller == null)
                return;

            var model = controller.Model;

            if (_rotateRoutine != null)
                StopCoroutine(_rotateRoutine);

            _rotateRoutine = StartCoroutine(RotateYRoutine(model));
        }

        private IEnumerator RotateYRoutine(WindowTaskModel model)
        {
            float degrees = model.OpenRotationAmount;
            Space space = model.UseLocalRotation ? Space.Self : Space.World;

            if (model.OpenDuration <= 0f)
            {
                windowPivot.Rotate(0f, degrees, 0f, space);
                yield break;
            }

            float elapsed = 0f;
            float appliedNormalized = 0f;

            while (elapsed < model.OpenDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / model.OpenDuration);
                float curveValue = model.OpenCurve != null ? model.OpenCurve.Evaluate(t) : t;

                float delta = (curveValue - appliedNormalized) * degrees;
                windowPivot.Rotate(0f, delta, 0f, space);
                appliedNormalized = curveValue;

                yield return null;
            }

            // Ensure we land exactly on the full rotation.
            float remainder = (1f - appliedNormalized) * degrees;
            if (Mathf.Abs(remainder) > Mathf.Epsilon)
                windowPivot.Rotate(0f, remainder, 0f, space);

            _rotateRoutine = null;
        }
    }
}
