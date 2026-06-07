using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class VRDoorHandle : MonoBehaviour
{
    [Header("Door")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private float openAngle = 35f;
    [SerializeField] private float doorOpenSpeed = 2.5f;

    [Header("Handle")]
    [SerializeField] private Transform handle;
    [SerializeField] private float handlePressedXAngle = -60f;
    [SerializeField] private float handleRotateSpeed = 8f;
    [SerializeField] private float handleReturnDelay = 0.25f;

    [Header("Interaction")]
    [SerializeField] private string controllerTag = "PlayerHand";
    [SerializeField] private float requiredDownMovement = 0.10f;

    [Header("Events")]
    [SerializeField] private UnityEvent onDoorOpened;

    [Header("Debug")]
    [SerializeField] private bool enableKeyboardTest = true;

    private Transform currentController;
    private float controllerStartY;
    private bool controllerNearHandle;
    private bool doorOpened;
    private bool handlePressed;

    private Coroutine openRoutine;

    private Quaternion handleRestRotation;
    private Quaternion handlePressedRotation;

    private void Start()
    {
        if (handle != null)
        {
            handleRestRotation = handle.localRotation;

            // ВАЖНО: у твоей ручки рабочая ось вращения — X.
            handlePressedRotation = handleRestRotation * Quaternion.Euler(handlePressedXAngle, 0f, 0f);
        }
    }

    private void Update()
    {
        if (enableKeyboardTest &&
            Keyboard.current != null &&
            Keyboard.current.oKey.wasPressedThisFrame)
        {
            PressHandleAndOpenDoor();
        }

        if (controllerNearHandle && currentController != null && !doorOpened)
        {
            float downMovement = controllerStartY - currentController.position.y;

            if (downMovement >= requiredDownMovement)
            {
                PressHandleAndOpenDoor();
            }
        }

        UpdateHandleVisual();
    }

    private void PressHandleAndOpenDoor()
    {
        if (doorOpened)
            return;

        handlePressed = true;
        doorOpened = true;

        if (openRoutine != null)
            StopCoroutine(openRoutine);

        openRoutine = StartCoroutine(OpenDoorRoutine());
    }

    private void UpdateHandleVisual()
    {
        if (handle == null)
            return;

        Quaternion targetRotation = handlePressed
            ? handlePressedRotation
            : handleRestRotation;

        handle.localRotation = Quaternion.Slerp(
            handle.localRotation,
            targetRotation,
            handleRotateSpeed * Time.deltaTime
        );
    }

    private IEnumerator OpenDoorRoutine()
    {
        if (doorPivot == null)
        {
            Debug.LogWarning("VRDoorHandle: Door Pivot is not assigned.");
            yield break;
        }

        Debug.Log("Door opening started.");

        Quaternion startRotation = doorPivot.localRotation;

        Quaternion targetRotation = Quaternion.Euler(
            doorPivot.localEulerAngles.x,
            openAngle,
            doorPivot.localEulerAngles.z
        );

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * doorOpenSpeed;

            doorPivot.localRotation = Quaternion.Slerp(
                startRotation,
                targetRotation,
                t
            );

            yield return null;
        }

        doorPivot.localRotation = targetRotation;

        Debug.Log("Door opened.");

        onDoorOpened.Invoke();

        // После открытия двери ручка должна вернуться в исходное положение.
        yield return new WaitForSeconds(handleReturnDelay);

        handlePressed = false;

        Debug.Log("Handle returned to rest position.");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(controllerTag))
            return;

        controllerNearHandle = true;
        currentController = other.transform;
        controllerStartY = currentController.position.y;

        Debug.Log("PlayerHand touched the handle.");
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentController != null && other.transform == currentController)
        {
            controllerNearHandle = false;
            currentController = null;

            Debug.Log("PlayerHand left the handle.");
        }
    }

    public void ResetDoor()
    {
        if (openRoutine != null)
            StopCoroutine(openRoutine);

        currentController = null;
        controllerNearHandle = false;
        doorOpened = false;
        handlePressed = false;

        if (handle != null)
            handle.localRotation = handleRestRotation;

        Debug.Log("Door reset.");
    }
}