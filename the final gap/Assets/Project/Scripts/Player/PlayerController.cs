using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float gravity = -9.81f;

    [Header("Look")]
    [SerializeField] float mouseSensitivity = 2f;
    [SerializeField] Transform cameraHolder;

    CharacterController _controller;
    Vector2 _moveInput;
    Vector2 _lookInput;
    Vector3 _velocity;
    float _xRotation;
    bool _canMove = true;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnEnable() => GameEvents.OnGameStateChanged += OnStateChanged;
    void OnDisable() => GameEvents.OnGameStateChanged -= OnStateChanged;

    void OnStateChanged(GameState state)
    {
        _canMove = state == GameState.Exploration;
        Cursor.lockState = _canMove ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !_canMove;
    }

    void OnMove(InputValue value) => _moveInput = value.Get<Vector2>();
    void OnLook(InputValue value) => _lookInput = value.Get<Vector2>();

    void Update()
    {
        if (!_canMove) return;
        HandleLook();
        HandleMovement();
    }

    void HandleLook()
    {
        float mouseX = _lookInput.x * mouseSensitivity * Time.deltaTime * 100f;
        float mouseY = _lookInput.y * mouseSensitivity * Time.deltaTime * 100f;

        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -80f, 80f);

        cameraHolder.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        Vector3 move = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        _controller.Move(move * moveSpeed * Time.deltaTime);

        if (_controller.isGrounded && _velocity.y < 0) _velocity.y = -2f;
        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }
}