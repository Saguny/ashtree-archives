using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float gravity = -9.81f;

    [Header("Look")]
    [Range(0.1f, 5f)]
    [SerializeField] float mouseSensitivity = 2f;
    [SerializeField] float mouseSmoothTime = 0.05f;
    [SerializeField] Transform cameraHolder;

    [Header("Crouch")]
    [SerializeField] float crouchHeight = 1f;
    [SerializeField] float crouchMoveSpeed = 2f;
    [SerializeField] float crouchTransitionSpeed = 8f;

    [Header("Head Bob")]
    [SerializeField] bool enableHeadBob = true;
    [SerializeField] float bobFrequency = 1.2f;
    [SerializeField] float bobAmplitude = 0.03f;

    [Header("Camera Tilt")]
    [SerializeField] bool enableStrafeTilt = true;
    [SerializeField] float tiltAngle = 2.5f;
    [SerializeField] float tiltSpeed = 5f;

    CharacterController _controller;
    Vector2 _moveInput;
    Vector2 _lookInput;
    Vector2 _smoothLookInput;
    Vector2 _smoothLookVelocity;
    Vector3 _velocity;
    float _xRotation;
    float _currentTilt;
    bool _canMove = true;

    // Crouch
    bool _isCrouching;
    float _standHeight;
    Vector3 _standCenter;
    Vector3 _camBaseLocalPos;

    // Head bob
    float _bobTimer;
    float _bobY;

    // Debug
    Vector3 _lastPosition;
    float _currentKmh;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _standHeight = _controller.height;
        _standCenter = _controller.center;
        _camBaseLocalPos = cameraHolder.localPosition;
        _lastPosition = transform.position;
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
    void OnCrouch(InputValue value)
    {
        if (!_canMove) return;
        _isCrouching = !_isCrouching;
    }

    void Update()
    {
        if (!_canMove) return;
        HandleLook();
        HandleMovement();
        HandleCrouch();
        HandleHeadBob();
        ApplyCameraTransform();
    }

    void HandleLook()
    {
        _smoothLookInput = Vector2.SmoothDamp(
            _smoothLookInput, _lookInput, ref _smoothLookVelocity, mouseSmoothTime
        );

        float mouseX = _smoothLookInput.x * mouseSensitivity * Time.deltaTime * 100f;
        float mouseY = _smoothLookInput.y * mouseSensitivity * Time.deltaTime * 100f;

        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -80f, 80f);

        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        float speed = _isCrouching ? crouchMoveSpeed : moveSpeed;
        Vector3 move = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        _controller.Move(move * speed * Time.deltaTime);

        if (_controller.isGrounded && _velocity.y < 0) _velocity.y = -2f;
        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    void HandleCrouch()
    {
        float targetHeight = _isCrouching ? crouchHeight : _standHeight;
        _controller.height = Mathf.Lerp(_controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
        float ratio = _controller.height / _standHeight;
        _controller.center = new Vector3(_standCenter.x, _standCenter.y * ratio, _standCenter.z);
    }

    void HandleHeadBob()
    {
        bool isMoving = _moveInput.magnitude > 0.1f && _controller.isGrounded;
        if (isMoving)
        {
            _bobTimer += Time.deltaTime * bobFrequency * Mathf.PI * 2f;
            _bobY = Mathf.Sin(_bobTimer) * bobAmplitude;
        }
        else
        {
            _bobTimer = 0f;
            _bobY = Mathf.Lerp(_bobY, 0f, 6f * Time.deltaTime);
        }
    }

    void ApplyCameraTransform()
    {
        float crouchOffset = _isCrouching ? -(_standHeight - crouchHeight) * 0.5f : 0f;
        float bobY = enableHeadBob ? _bobY : 0f;
        float targetY = _camBaseLocalPos.y + crouchOffset + bobY;

        float smoothY = Mathf.Lerp(cameraHolder.localPosition.y, targetY, crouchTransitionSpeed * Time.deltaTime);
        cameraHolder.localPosition = new Vector3(_camBaseLocalPos.x, smoothY, _camBaseLocalPos.z);

        if (enableStrafeTilt)
        {
            float targetTilt = -_moveInput.x * tiltAngle;
            _currentTilt = Mathf.Lerp(_currentTilt, targetTilt, tiltSpeed * Time.deltaTime);
        }
        else _currentTilt = 0f;

        cameraHolder.localRotation = Quaternion.Euler(_xRotation, 0f, _currentTilt);
    }

#if UNITY_EDITOR
    void LateUpdate()
    {
        Vector3 delta = transform.position - _lastPosition;
        delta.y = 0f;
        _currentKmh = (delta.magnitude / Time.deltaTime) * 3.6f;
        _lastPosition = transform.position;
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 220, 25), $"Speed: {_currentKmh:F1} km/h  {(_isCrouching ? "[CROUCHING]" : "")}");
    }
#endif
}
