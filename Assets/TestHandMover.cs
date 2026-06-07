using UnityEngine;
using UnityEngine.InputSystem;

public class TestHandMover : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 1.5f;

    private void Update()
    {
        Vector3 move = Vector3.zero;

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.wKey.isPressed)
            move += Vector3.forward;

        if (keyboard.sKey.isPressed)
            move += Vector3.back;

        if (keyboard.aKey.isPressed)
            move += Vector3.left;

        if (keyboard.dKey.isPressed)
            move += Vector3.right;

        if (keyboard.eKey.isPressed)
            move += Vector3.up;

        if (keyboard.qKey.isPressed)
            move += Vector3.down;

        transform.position += move * moveSpeed * Time.deltaTime;
    }
}