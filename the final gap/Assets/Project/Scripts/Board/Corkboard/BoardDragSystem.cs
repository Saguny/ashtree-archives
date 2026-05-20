using UnityEngine;
using UnityEngine.InputSystem;

public class BoardDragSystem : MonoBehaviour
{
    public static BoardDragSystem Instance { get; private set; }

    [Header("Drag Settings")]
    [SerializeField] float springStrength = 18f;
    [SerializeField] float liftAmount = 0.05f;

    [Header("Board")]
    [SerializeField] LayerMask cardLayerMask;
    [SerializeField] Transform boardTransform;
    /// <summary>
    /// Half-extents of the board surface in local board space (x = horizontal, y = vertical).
    /// Set to (0, 0) to disable clamping entirely.
    /// </summary>
    [SerializeField] Vector2 boardExtents = Vector2.zero;
    /// <summary>
    /// Inset from boardExtents edges — keeps cards away from the frame border.
    /// The gizmo shows the full extents; the effective pin area is extents - padding.
    /// </summary>
    [SerializeField] float boardPadding = 0.02f;
    /// <summary>Small gap so the card sits just in front of the board surface without z-fighting.</summary>
    [SerializeField] float cardSurfaceOffset = 0.01f;

    CardBehaviour _dragging;
    bool _pickedUpThisFrame;
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

        _pickedUpThisFrame = false;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (TryPickupFromBoard())
                _pickedUpThisFrame = true;
            else
                TryPlaceFromHotbar();
        }

        // Guard against same-frame pickup+release (fast click) dropping the card immediately
        if (Mouse.current.leftButton.wasReleasedThisFrame && _dragging != null && !_pickedUpThisFrame)
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

    bool TryPickupFromBoard()
    {
        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 10f, cardLayerMask)) return false;

        CardBehaviour card = hit.collider.GetComponent<CardBehaviour>();
        if (card == null || !card.IsPinned) return false;

        card.OnUnpinned();

        _dragging = card;
        _dragging.Rigidbody.isKinematic = true;

        // Fix the drag plane at the card's position so it doesn't shift during drag
        _dragPlane = new Plane(-boardTransform.forward, card.transform.position);
        return true;
    }

    void TryPlaceFromHotbar()
    {
        CardBehaviour selected = HotbarSystem.Instance.SelectedCard;
        if (selected == null) return;

        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane boardPlane = new Plane(-boardTransform.forward, boardTransform.position + boardTransform.forward * cardSurfaceOffset);
        if (!boardPlane.Raycast(ray, out float dist)) return;

        Vector3 hitPoint = ray.GetPoint(dist);

        // Reject the placement entirely if the cursor isn't pointing at the
        // physical board surface — prevents pinning in mid-air when the camera
        // drifts slightly while in board mode.
        if (!IsOnBoard(hitPoint)) return;

        Vector3 pinPos = SnapToBoard(ClampToBoardBounds(hitPoint));
        Quaternion pinRot = boardTransform.rotation * Quaternion.Euler(90f, 0f, 0f);

        selected.gameObject.SetActive(true);
        selected.OnPinned(pinPos, pinRot);
        HotbarSystem.Instance.RemoveSelected();
    }

    void TryDrop()
    {
        // Raycast against the fixed board surface plane — same as TryPlaceFromHotbar —
        // so depth is always consistent regardless of liftAmount or lerp lag.
        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane surfacePlane = new Plane(-boardTransform.forward, boardTransform.position + boardTransform.forward * cardSurfaceOffset);

        Vector3 pinPos;
        if (surfacePlane.Raycast(ray, out float dist))
        {
            // Always clamp — the board plane is infinite, so if the camera has drifted
            // the intersection point can be far outside the physical board surface.
            pinPos = SnapToBoard(ClampToBoardBounds(ray.GetPoint(dist)));
        }
        else
        {
            // Fallback: project the card's current position onto the board surface.
            pinPos = SnapToBoard(ClampToBoardBounds(_dragging.transform.position));
        }

        Quaternion pinRot = boardTransform.rotation * Quaternion.Euler(90f, 0f, 0f);
        _dragging.OnPinned(pinPos, pinRot);
        _dragging = null;
    }

    /// <summary>Projects a world position flush onto the board plane with a small surface offset.</summary>
    Vector3 SnapToBoard(Vector3 worldPos)
    {
        Vector3 toPoint = worldPos - boardTransform.position;
        float normalDist = Vector3.Dot(toPoint, boardTransform.forward);
        return worldPos - normalDist * boardTransform.forward + boardTransform.forward * cardSurfaceOffset;
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

    /// <summary>Effective half-extents after subtracting the frame padding.</summary>
    Vector2 EffectiveExtents => new Vector2(
        Mathf.Max(0f, boardExtents.x - boardPadding),
        Mathf.Max(0f, boardExtents.y - boardPadding));

    /// <summary>
    /// Returns true if <paramref name="worldPos"/> lies within the board's effective
    /// extents (boardExtents minus boardPadding). Always true when boardExtents is (0,0).
    /// </summary>
    bool IsOnBoard(Vector3 worldPos)
    {
        if (boardExtents == Vector2.zero) return true;
        Vector2 e = EffectiveExtents;
        Vector3 local = boardTransform.InverseTransformPoint(worldPos);
        return Mathf.Abs(local.x) <= e.x && Mathf.Abs(local.y) <= e.y;
    }

    /// <summary>
    /// Clamps a world position to the board's effective extents (boardExtents minus
    /// boardPadding). The depth component (Z) is preserved so the result stays on the
    /// board plane.
    /// </summary>
    Vector3 ClampToBoardBounds(Vector3 worldPos)
    {
        if (boardExtents == Vector2.zero) return worldPos;
        Vector2 e = EffectiveExtents;
        Vector3 local = boardTransform.InverseTransformPoint(worldPos);
        local.x = Mathf.Clamp(local.x, -e.x, e.x);
        local.y = Mathf.Clamp(local.y, -e.y, e.y);
        return boardTransform.TransformPoint(local);
    }

    void OnDrawGizmos()
    {
        if (boardTransform == null || boardExtents == Vector2.zero) return;

        // Outer rect — full extents (matches Inspector values)
        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.matrix = boardTransform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero,
            new Vector3(boardExtents.x * 2f, boardExtents.y * 2f, 0.002f));

        // Inner rect — effective pin area after padding
        Vector2 e = EffectiveExtents;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero,
            new Vector3(e.x * 2f, e.y * 2f, 0.002f));
    }

}
