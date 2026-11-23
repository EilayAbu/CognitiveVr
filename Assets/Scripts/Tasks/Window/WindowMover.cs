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

        // The scene starts with the window shut (same assumption as the controller),
        // so the startup rotation is the reference the open pose is measured from.
        private Quaternion _closedLocalRotation;
        private Quaternion _closedWorldRotation;

        private void Awake()
        {
            if (windowPivot == null)
                windowPivot = transform;

            _closedLocalRotation = windowPivot.localRotation;
            _closedWorldRotation = windowPivot.rotation;
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

        /// <summary>
        /// Moves the window from wherever it is now to the fully open pose
        /// (closed rotation + OpenRotationAmount around Y). The target is absolute,
        /// so a window the player already opened part way does not overshoot.
        /// </summary>
        private IEnumerator RotateYRoutine(WindowTaskModel model)
        {
            bool local = model.UseLocalRotation;
            float degrees = model.OpenRotationAmount;

            Quaternion target = local
                ? _closedLocalRotation * Quaternion.Euler(0f, degrees, 0f)
                : Quaternion.AngleAxis(degrees, Vector3.up) * _closedWorldRotation;
            Quaternion from = local ? windowPivot.localRotation : windowPivot.rotation;

            if (model.OpenDuration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < model.OpenDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / model.OpenDuration);
                    float curveValue = model.OpenCurve != null ? model.OpenCurve.Evaluate(t) : t;

                    SetRotation(Quaternion.SlerpUnclamped(from, target, curveValue), local);
                    yield return null;
                }
            }

            // Land exactly on the open pose.
            SetRotation(target, local);
            _rotateRoutine = null;
        }

        private void SetRotation(Quaternion rotation, bool local)
        {
            if (local)
                windowPivot.localRotation = rotation;
            else
                windowPivot.rotation = rotation;
        }
    }
}
