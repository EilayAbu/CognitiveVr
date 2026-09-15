using System;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CognitiveVR.Phone
{
    /// <summary>
    /// Sits on Notification_Visual (per spec). The Visual is anchored inside
    /// the empty Notification_Slot, so dragging it horizontally does not push
    /// neighbouring rows. On commit, fires <see cref="OnDismissed"/> — the
    /// owning PhoneNotificationItem on the parent Slot is responsible for
    /// destroying the row so the VerticalLayoutGroup reflows cleanly.
    ///
    /// Opened boss/weather messages also bind a <see cref="PokeInteractable"/>
    /// so a finger poke on the phone can drive the same slide (the canvas is
    /// ray-only and never receives poke EventSystem drags).
    /// </summary>
    [DisallowMultipleComponent]
    public class SwipeToDelete : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Bindings")]
        [Tooltip("The Notification_Visual RectTransform (child) that slides horizontally. Must NOT be the slot itself.")]
        [SerializeField] private RectTransform _visual;
        [Tooltip("Optional CanvasGroup on the visual; auto-created if missing.")]
        [SerializeField] private CanvasGroup _visualCanvasGroup;
        [Tooltip("Optional X button that triggers a programmatic dismiss.")]
        [SerializeField] private Button _deleteButton;
        [Tooltip("Optional poke target. When set, finger pokes slide the visual from TouchPoint.")]
        [SerializeField] private PokeInteractable _poke;

        [Header("Swipe Tuning")]
        [Tooltip("Horizontal travel (in canvas units) required to commit a dismiss.")]
        [SerializeField] private float _dismissDistance = 220f;
        [Tooltip("Duration of the slide-off animation when a dismiss is committed.")]
        [SerializeField] private float _animSeconds = 0.18f;
        [Tooltip("How far off-screen the visual slides during the dismiss animation.")]
        [SerializeField] private float _offscreenDistance = 1200f;
        [Tooltip("If true, only a swipe to the right can dismiss. Left swipes snap back.")]
        [SerializeField] private bool _rightOnly;

        [Header("Events")]
        public UnityEvent OnDismissed;

        public event Action OnDismissedEvent;

        private bool _dragging;
        private Vector2 _dragStartLocal;
        private Vector2 _visualStartAnchored;
        private bool _animating;
        private float _animStart;
        private Vector2 _animFrom;
        private Vector2 _animTo;
        private bool _destroyed;
        private Vector2 _restAnchored;
        private bool _restCaptured;
        private PokeInteractor _pokeInteractor;

        private void Awake()
        {
            if (_visual == null)
                _visual = transform as RectTransform;
            if (_visual != null && _visualCanvasGroup == null)
            {
                _visualCanvasGroup = _visual.GetComponent<CanvasGroup>();
                if (_visualCanvasGroup == null)
                    _visualCanvasGroup = _visual.gameObject.AddComponent<CanvasGroup>();
            }

            CaptureRestIfNeeded();
        }

        private void OnEnable()
        {
            CaptureRestIfNeeded();
            ResetVisual();

            if (_deleteButton != null)
                _deleteButton.onClick.AddListener(Dismiss);

            if (_poke != null)
            {
                _poke.WhenSelectingInteractorViewAdded += HandlePokeSelectingAdded;
                _poke.WhenSelectingInteractorViewRemoved += HandlePokeSelectingRemoved;
            }
        }

        private void OnDisable()
        {
            if (_deleteButton != null)
                _deleteButton.onClick.RemoveListener(Dismiss);

            if (_poke != null)
            {
                _poke.WhenSelectingInteractorViewAdded -= HandlePokeSelectingAdded;
                _poke.WhenSelectingInteractorViewRemoved -= HandlePokeSelectingRemoved;
            }

            _pokeInteractor = null;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_pokeInteractor != null) return;
            if (!TryBeginDrag()) return;
            if (_visual.parent is not RectTransform parentRect) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out _dragStartLocal);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_pokeInteractor != null) return;
            if (!_dragging || _animating) return;
            if (_visual.parent is not RectTransform parentRect) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out Vector2 currentLocal);
            ApplyDragDelta(currentLocal.x - _dragStartLocal.x);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_pokeInteractor != null) return;
            EndDrag();
        }

        /// <summary>Programmatic dismiss (used by the DeleteButton).</summary>
        public void Dismiss()
        {
            if (_animating || _destroyed) return;
            if (_visual == null)
            {
                FinalizeDismiss();
                return;
            }
            StartDismissAnimation(1f);
        }

        private void HandlePokeSelectingAdded(IInteractorView view)
        {
            if (view is not PokeInteractor poke) return;
            if (!TryBeginDrag()) return;
            if (!TryWorldToParentLocal(poke.TouchPoint, out _dragStartLocal))
            {
                _dragging = false;
                return;
            }

            _pokeInteractor = poke;
        }

        private void HandlePokeSelectingRemoved(IInteractorView view)
        {
            if (view is not PokeInteractor poke) return;
            if (_pokeInteractor != poke) return;
            _pokeInteractor = null;
            EndDrag();
        }

        private bool TryBeginDrag()
        {
            if (_animating || _destroyed || _dragging) return false;
            if (_visual == null) return false;
            if (_visual.parent is not RectTransform) return false;

            _visualStartAnchored = _visual.anchoredPosition;
            _dragging = true;
            return true;
        }

        private void ApplyDragDelta(float deltaX)
        {
            if (_visual == null) return;
            if (_rightOnly)
                deltaX = Mathf.Max(0f, deltaX);

            _visual.anchoredPosition = new Vector2(_visualStartAnchored.x + deltaX, _visualStartAnchored.y);

            if (_visualCanvasGroup != null)
            {
                float fade = Mathf.Clamp01(1f - Mathf.Abs(deltaX) / Mathf.Max(1f, _dismissDistance));
                _visualCanvasGroup.alpha = Mathf.Lerp(0.4f, 1f, fade);
            }
        }

        private void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;

            float deltaX = _visual.anchoredPosition.x - _visualStartAnchored.x;
            bool shouldDismiss = _rightOnly
                ? deltaX >= _dismissDistance
                : Mathf.Abs(deltaX) >= _dismissDistance;

            if (shouldDismiss)
            {
                StartDismissAnimation(_rightOnly ? 1f : Mathf.Sign(deltaX));
            }
            else
            {
                _visual.anchoredPosition = _visualStartAnchored;
                if (_visualCanvasGroup != null) _visualCanvasGroup.alpha = 1f;
            }
        }

        private void StartDismissAnimation(float direction)
        {
            _animating = true;
            _animStart = Time.unscaledTime;
            _animFrom = _visual.anchoredPosition;
            float sign = Mathf.Approximately(direction, 0f) ? 1f : Mathf.Sign(direction);
            _animTo = new Vector2(sign * _offscreenDistance, _visualStartAnchored.y);
        }

        private void Update()
        {
            if (_pokeInteractor != null && _dragging && !_animating)
            {
                if (TryWorldToParentLocal(_pokeInteractor.TouchPoint, out Vector2 currentLocal))
                    ApplyDragDelta(currentLocal.x - _dragStartLocal.x);
            }

            if (!_animating || _visual == null) return;

            float t = (Time.unscaledTime - _animStart) / Mathf.Max(0.01f, _animSeconds);
            if (t >= 1f)
            {
                _visual.anchoredPosition = _animTo;
                if (_visualCanvasGroup != null) _visualCanvasGroup.alpha = 0f;
                _animating = false;
                FinalizeDismiss();
                return;
            }

            float eased = 1f - Mathf.Pow(1f - t, 3f);
            _visual.anchoredPosition = Vector2.LerpUnclamped(_animFrom, _animTo, eased);
            if (_visualCanvasGroup != null)
                _visualCanvasGroup.alpha = Mathf.Clamp01(1f - t);
        }

        private bool TryWorldToParentLocal(Vector3 world, out Vector2 local)
        {
            local = default;
            if (_visual == null || _visual.parent is not RectTransform parentRect) return false;
            Vector3 local3 = parentRect.InverseTransformPoint(world);
            local = new Vector2(local3.x, local3.y);
            return true;
        }

        private void FinalizeDismiss()
        {
            if (_destroyed) return;
            _destroyed = true;
            _pokeInteractor = null;
            OnDismissed?.Invoke();
            OnDismissedEvent?.Invoke();
            // Intentionally does NOT destroy itself - the owning
            // PhoneNotificationItem on the parent Slot does that so the layout
            // group on Content reflows the row correctly. Opened messages
            // (boss / weather) reuse this object via SetActive, so OnEnable
            // resets _destroyed and snaps the visual back.
        }

        private void CaptureRestIfNeeded()
        {
            if (_restCaptured || _visual == null) return;
            _restAnchored = _visual.anchoredPosition;
            _restCaptured = true;
        }

        private void ResetVisual()
        {
            _destroyed = false;
            _animating = false;
            _dragging = false;
            _pokeInteractor = null;

            if (_visual != null)
                _visual.anchoredPosition = _restAnchored;
            if (_visualCanvasGroup != null)
                _visualCanvasGroup.alpha = 1f;
        }
    }
}
