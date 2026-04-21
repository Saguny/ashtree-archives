using UnityEngine;
using UnityEngine.InputSystem;

public class PickupSystem : MonoBehaviour
{
    public static PickupSystem Instance { get; private set; }

    [Header("Hold Settings")]
    [SerializeField] float holdDistance = 2f;
    [SerializeField] float holdMaxSpeed = 20f;

    [Header("Throw Settings")]
    [SerializeField] float throwForce = 8f;
    [SerializeField] int velocitySamples = 10;

    IPickable _held;
    Camera _cam;

    Vector3 _targetPosition;
    Vector3 _prevTargetPosition;
    Vector3[] _velocityBuffer;
    int _velocityIndex;

    public bool IsHolding => _held != null;
    public Vector3 HeldPosition => _held != null ? _held.Rigidbody.position : Vector3.zero;
    public CardBehaviour HeldCard => _held is CardBehaviour card ? card : null;
    public VhsTape HeldTape => _held is VhsTape tape ? tape : null;
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _cam = Camera.main;
        _velocityBuffer = new Vector3[velocitySamples];
    }

    void Update()
    {
        if (_held == null) return;

        UpdateTargetPosition();

        if (Mouse.current.leftButton.wasReleasedThisFrame)
            Release();
    }

    void FixedUpdate()
    {
        if (_held == null) return;
        MoveHeld();
        TrackVelocity();
    }

    void UpdateTargetPosition()
    {
        Ray ray = _cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f));
        _targetPosition = ray.GetPoint(holdDistance);
    }

    public void ForceRelease()
    {
        if (_held == null) return;
        _held.Rigidbody.useGravity = true;
        _held.Rigidbody.linearDamping = 0f;
        _held.Rigidbody.linearVelocity = Vector3.zero;
        _held.OnDropped(Vector3.zero);
        _held = null;
    }

    void MoveHeld()
    {
        Vector3 direction = _targetPosition - _held.Rigidbody.position;
        Vector3 velocity = direction / Time.fixedDeltaTime;
        velocity = Vector3.ClampMagnitude(velocity, holdMaxSpeed);

        _held.Rigidbody.linearVelocity = velocity;
        _held.Rigidbody.angularVelocity = Vector3.zero;
    }

    void TrackVelocity()
    {
        _velocityBuffer[_velocityIndex % velocitySamples] =
            (_targetPosition - _prevTargetPosition) / Time.fixedDeltaTime;
        _prevTargetPosition = _targetPosition;
        _velocityIndex++;
    }

    public void PickUp(IPickable pickable)
    {
        if (_held != null) return;

        _held = pickable;
        _held.Rigidbody.useGravity = false;
        _held.Rigidbody.linearDamping = 0f;
        _held.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _held.Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        _targetPosition = _held.Rigidbody.position;
        _prevTargetPosition = _targetPosition;

        System.Array.Clear(_velocityBuffer, 0, _velocityBuffer.Length);
        _velocityIndex = 0;

        _held.OnPickedUp();
    }

    void Release()
    {
        Vector3 avgVelocity = Vector3.zero;
        int count = Mathf.Min(_velocityIndex, velocitySamples);

        for (int i = 0; i < count; i++)
            avgVelocity += _velocityBuffer[i];

        if (count > 0) avgVelocity /= count;

        if (float.IsNaN(avgVelocity.x) || float.IsNaN(avgVelocity.y) || float.IsNaN(avgVelocity.z))
            avgVelocity = Vector3.zero;

        Vector3 throwVelocity = Vector3.ClampMagnitude(avgVelocity * throwForce, 20f);

        _held.Rigidbody.useGravity = true;
        _held.Rigidbody.linearVelocity = Vector3.zero;

        _held.OnDropped(throwVelocity);
        _held = null;
    }
}