using System;
using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Optional helper that detects a horizontal "swipe" using Meta ISDK
    /// pointer events (Grabbable.WhenPointerEventRaised). Subscribe pattern is
    /// the same as InventoryItemMetaBridge: hook Select / Move / Unselect and
    /// compute a canvas-space horizontal delta. Fires OnSwipeDismiss when the
    /// horizontal travel since Select exceeds the threshold.
    /// </summary>
    /// <remarks>
    /// This is a fallback path for setups that wire a Grabbable directly onto a
    /// notification panel. The default flow uses Unity's EventSystem +
    /// IDragHandler (PhoneNotificationItem). This component is only required
    /// when PointableCanvasModule isn't routing UI drags to the EventSystem.
    /// </remarks>
    [DisallowMultipleComponent]
    public class PhoneRaySwipeDetector : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("ISDK Grabbable on this notification panel. If empty, GetComponent on Awake.")]
        [SerializeField] private Grabbable _grabbable;

        [Header("Tuning")]
        [Tooltip("World-space horizontal travel required to trigger dismiss (meters).")]
        [SerializeField] private float _dismissWorldDistance = 0.05f;

        [Tooltip("Reference RectTransform used to project the world-space pointer onto a horizontal axis. Defaults to this RectTransform.")]
        [SerializeField] private RectTransform _canvasReference;

        public event Action<float> OnSwipeDismiss;

        private bool _isDragging;
        private Vector3 _selectStartWorld;

        private void Reset()
        {
            _grabbable = GetComponent<Grabbable>();
            _canvasReference = transform as RectTransform;
        }

        private void Awake()
        {
            if (_grabbable == null) _grabbable = GetComponent<Grabbable>();
            if (_canvasReference == null) _canvasReference = transform as RectTransform;
        }

        private void OnEnable()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        private void OnDisable()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    _isDragging = true;
                    _selectStartWorld = evt.Pose.position;
                    break;

                case PointerEventType.Move:
                    if (!_isDragging) return;
                    if (TryGetHorizontalDelta(evt.Pose.position, out float delta))
                    {
                        if (Mathf.Abs(delta) >= _dismissWorldDistance)
                        {
                            _isDragging = false;
                            OnSwipeDismiss?.Invoke(delta);
                        }
                    }
                    break;

                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    _isDragging = false;
                    break;
            }
        }

        private bool TryGetHorizontalDelta(Vector3 currentWorld, out float deltaAlongRight)
        {
            if (_canvasReference == null)
            {
                deltaAlongRight = currentWorld.x - _selectStartWorld.x;
                return true;
            }

            Vector3 right = _canvasReference.right;
            Vector3 worldDelta = currentWorld - _selectStartWorld;
            deltaAlongRight = Vector3.Dot(worldDelta, right.normalized);
            return true;
        }
    }
}
