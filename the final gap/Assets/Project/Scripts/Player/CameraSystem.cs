using UnityEngine;

public class CameraSystem : MonoBehaviour
{
    public static CameraSystem Instance { get; private set; }

    [Header("Transition")]
    [SerializeField] float lerpSpeed = 3f;

    [Header("Board Swing")]
    [SerializeField] float swingMaxAngle = 20f;
    [SerializeField] float swingSpeed = 3f;
    [SerializeField] float swingReturnSpeed = 5f;
    [SerializeField] float edgeThreshold = 0.3f;

    Camera _cam;
    Transform _defaultParent;
    Vector3 _defaultLocalPos;
    Quaternion _defaultLocalRot;

    Transform _targetTransform;
    bool _isLerping;

    Transform _lookAtTarget;    // used by LookAt() — rotates camera in place toward a world Transform
    bool _isLookingAt;

    Quaternion _boardBaseRot;
    Vector2 _currentSwing;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        _cam = Camera.main;
        _defaultParent = _cam.transform.parent;
        _defaultLocalPos = _cam.transform.localPosition;
        _defaultLocalRot = _cam.transform.localRotation;
    }

    void OnEnable() => GameEvents.OnGameStateChanged += OnStateChanged;
    void OnDisable() => GameEvents.OnGameStateChanged -= OnStateChanged;

    void OnStateChanged(GameState state)
    {
        // Exploration: always auto-return camera to player.
        // TapeMode: TapeDirector manages camera explicitly — don't auto-return.
        if (state == GameState.Exploration)
            ReturnToPlayer();
    }

    public void TransitionTo(Transform target)
    {
        _targetTransform = target;
        _cam.transform.SetParent(null);
        _isLerping   = true;
        _isLookingAt = false;
        _lookAtTarget = null;
        _currentSwing = Vector2.zero;
    }

    /// <summary>
    /// Keeps the camera at the player's current position but smoothly rotates it
    /// to face <paramref name="target"/>. Good for looking at a character without
    /// needing a pre-placed camera rig Transform.
    /// Call ReturnToPlayer() to restore normal first-person look.
    /// </summary>
    public void LookAt(Transform target)
    {
        _lookAtTarget  = target;
        _isLookingAt   = true;
        _isLerping     = false;
        _targetTransform = null;
        _cam.transform.SetParent(null);
        _currentSwing  = Vector2.zero;
    }

    /// <summary>
    /// Returns camera to the player's head. Called automatically on Exploration state.
    /// TapeDirector calls this directly via the UnlockCamera / EndDialogue actions.
    /// </summary>
    public void ReturnToPlayer()
    {
        _targetTransform = null;
        _isLerping       = false;
        _lookAtTarget    = null;
        _isLookingAt     = false;
        _currentSwing    = Vector2.zero;
        _cam.transform.SetParent(_defaultParent);
        _cam.transform.localPosition = _defaultLocalPos;
        _cam.transform.localRotation = _defaultLocalRot;
    }

    void Update()
    {
        if (_isLerping && _targetTransform != null)
            LerpToTarget();

        if (_isLookingAt && _lookAtTarget != null)
            LookAtTarget();

        if (GameManager.Instance.CurrentState == GameState.BoardMode)
            HandleSwing();
    }

    void LerpToTarget()
    {
        _cam.transform.position = Vector3.Lerp(
            _cam.transform.position,
            _targetTransform.position,
            lerpSpeed * Time.deltaTime
        );

        _boardBaseRot = _targetTransform.rotation;

        Quaternion swingRot = Quaternion.Euler(-_currentSwing.y, _currentSwing.x, 0f);
        Quaternion targetRot = _boardBaseRot * swingRot;

        _cam.transform.rotation = Quaternion.Lerp(
            _cam.transform.rotation,
            targetRot,
            lerpSpeed * Time.deltaTime
        );
    }

    // Camera stays at its current world position; only rotation lerps toward the target.
    void LookAtTarget()
    {
        Vector3 direction = _lookAtTarget.position - _cam.transform.position;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion targetRot = Quaternion.LookRotation(direction);
        _cam.transform.rotation = Quaternion.Lerp(_cam.transform.rotation, targetRot, lerpSpeed * Time.deltaTime);
    }

    void HandleSwing()
    {
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        float nx = mousePos.x / Screen.width;
        float ny = mousePos.y / Screen.height;

        Vector2 swingInput = Vector2.zero;

        if (nx < edgeThreshold) swingInput.x = -(1f - nx / edgeThreshold);
        else if (nx > 1f - edgeThreshold) swingInput.x = (nx - (1f - edgeThreshold)) / edgeThreshold;

        if (ny < edgeThreshold) swingInput.y = -(1f - ny / edgeThreshold);
        else if (ny > 1f - edgeThreshold) swingInput.y = (ny - (1f - edgeThreshold)) / edgeThreshold;

        if (swingInput.magnitude > 0.01f)
            _currentSwing = Vector2.Lerp(_currentSwing, swingInput * swingMaxAngle, swingSpeed * Time.deltaTime);
        else
            _currentSwing = Vector2.Lerp(_currentSwing, Vector2.zero, swingReturnSpeed * Time.deltaTime);

        Quaternion swingRot = Quaternion.Euler(-_currentSwing.y, _currentSwing.x, 0f);
        _cam.transform.rotation = _boardBaseRot * swingRot;
    }
}