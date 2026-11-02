using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 6f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.4f;
    [SerializeField] private float gravity = -25f;

    [Header("Look")]
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;
    [SerializeField, Range(1f, 89f)] private float lookLimit = 85f;

    [Header("Respawn")]
    [Tooltip("Return to the initial player position below this world Y coordinate.")]
    [SerializeField] private float fallLimit = -20f;

    private CharacterController controller;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float verticalSpeed;
    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera == null)
        {
            Debug.LogError("PlayerMovement needs a camera under the Player.", this);
            enabled = false;
            return;
        }

        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        playerCamera.transform.localRotation = Quaternion.identity;
    }

    private void Start()
    {
        SetCursorLocked(true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool acceptInput = Application.isFocused &&
                           Cursor.lockState == CursorLockMode.Locked;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
            acceptInput = false;
        }
        else if (!acceptInput && Application.isFocused && mouse != null &&
                 mouse.leftButton.wasPressedThisFrame)
        {
            SetCursorLocked(true);
        }

        Vector2 movementInput = Vector2.zero;
        bool jumpPressed = false;

        if (acceptInput)
        {
            if (mouse != null)
            {
                Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                transform.Rotate(0f, look.x, 0f);
                pitch = Mathf.Clamp(pitch - look.y, -lookLimit, lookLimit);
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            if (keyboard != null)
            {
                movementInput.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                                  (keyboard.aKey.isPressed ? 1f : 0f);
                movementInput.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                                  (keyboard.sKey.isPressed ? 1f : 0f);
                jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
            }
        }

        movementInput = Vector2.ClampMagnitude(movementInput, 1f);
        Vector3 horizontalVelocity = (transform.right * movementInput.x +
                                      transform.forward * movementInput.y) * moveSpeed;

        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        if (controller.isGrounded && jumpPressed)
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalSpeed += gravity * Time.deltaTime;

        CollisionFlags collisions = controller.Move(
            (horizontalVelocity + Vector3.up * verticalSpeed) * Time.deltaTime);

        if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
            verticalSpeed = 0f;

        if (transform.position.y < fallLimit)
            Respawn();
    }

    private void Respawn()
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        controller.enabled = true;
        verticalSpeed = 0f;
        pitch = 0f;
        playerCamera.transform.localRotation = Quaternion.identity;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            SetCursorLocked(false);
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnValidate()
    {
        gravity = Mathf.Min(gravity, -0.01f);
    }
}
