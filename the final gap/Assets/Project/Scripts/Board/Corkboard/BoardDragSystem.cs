using UnityEngine;
using UnityEngine.InputSystem;

public class BoardDragSystem : MonoBehaviour
{
    public static BoardDragSystem Instance { get; private set; }

    [Header("Drag Settings")]
    [SerializeField] float springStrength = 18f;
    [SerializeField] float liftAmount = 0.05f;
    [SerializeField] float snapDistance = 0.15f;

    [Header("Board")]
    [SerializeField] LayerMask cardLayerMask;
    [SerializeField] Transform boardTransform;

    CardBehaviour _dragging;
    Camera _cam;
    Plane _dragPlane;

    public bool IsDragging => _dragging != null;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _cam = Camera.main;
    }

    void OnEnable() => GameEvents.OnGameStateChanged += OnStateChanged;
    void OnDisable() => GameEvents.OnGameStateChanged -= OnStateChanged;

    void OnStateChanged(GameState state)
    {
        if (state != GameState.BoardMode && _dragging != null)
            CancelDrag();
    }

    void Update()
    {
        if (GameManager.Instance.CurrentState != GameState.BoardMode) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            TryPickupFromBoard();

        if (Mouse.current.leftButton.wasReleasedThisFrame && _dragging != null)
            TryDrop();
    }

    void FixedUpdate()
    {
        if (_dragging == null) return;

        Vector3 target = GetMouseOnBoardPlane();
        if (target == Vector3.zero) return;

        target += boardTransform.forward * liftAmount;

        _dragging.transform.position = Vector3.Lerp(
            _dragging.transform.position,
            target,
            springStrength * Time.fixedDeltaTime
        );
    }
    public Vector3 DragPosition => _dragging != null ? _dragging.transform.position : Vector3.zero;
    void TryPickupFromBoard()
{
    Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
    if (!Physics.Raycast(ray, out RaycastHit hit, 10f, cardLayerMask)) return;

    CardBehaviour card = hit.collider.GetComponent<CardBehaviour>();
    if (card == null || !card.IsPinned) return;

    card.CurrentSlot?.Vacate();
    card.OnUnpinned();

    _dragging = card;
    _dragging.Rigidbody.isKinematic = true;

    // Fix the plane at the card's position at pickup time, never moves again
    _dragPlane = new Plane(-boardTransform.forward, card.transform.position);
}

    void TryDrop()
    {
        SlotBehaviour nearest = FindNearestSlot();

        if (nearest != null && !nearest.IsOccupied)
        {
            _dragging.Rigidbody.isKinematic = true;
            nearest.TryOccupy(_dragging);
        }
        else
        {
            _dragging.Rigidbody.isKinematic = false;
            _dragging.Rigidbody.useGravity = true;
        }

        _dragging = null;
    }

    void CancelDrag()
    {
        if (_dragging == null) return;
        _dragging.Rigidbody.isKinematic = false;
        _dragging.Rigidbody.useGravity = true;
        _dragging = null;
    }

    Vector3 GetMouseOnBoardPlane()
{
    Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
    if (_dragPlane.Raycast(ray, out float dist))
        return ray.GetPoint(dist);
    return Vector3.zero;
}

    SlotBehaviour FindNearestSlot()
    {
        SlotBehaviour nearest = null;
        float nearestDist = snapDistance;

        foreach (var slot in FindObjectsByType<SlotBehaviour>(FindObjectsSortMode.None))
        {
            float dist = Vector3.Distance(_dragging.transform.position, slot.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = slot;
            }
        }
        return nearest;
    }
}