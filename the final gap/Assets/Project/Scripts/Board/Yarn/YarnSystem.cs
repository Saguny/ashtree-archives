using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class YarnSystem : MonoBehaviour
{
    public static YarnSystem Instance { get; private set; }

    [Header("Yarn Settings")]
    [SerializeField] float yarnWidth = 0.004f;
    [SerializeField] Color yarnColor = new Color(0.8f, 0.3f, 0.2f, 1f);
    [SerializeField] int maxConnectionsPerCard = 3;
    [SerializeField] float boardOffset = 0.02f;

    [Header("Preview")]
    [SerializeField] float previewSag = 0.005f;

    [SerializeField] Transform boardTransform;

    List<YarnConnection> _connections = new List<YarnConnection>();
    PinBehaviour _pendingFrom;
    LineRenderer _preview;
    Material _yarnMat;
    Camera _cam;

    public bool IsPending => _pendingFrom != null;

    /// <summary>Total number of active yarn connections on the board.</summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>Returns true if cards a and b are directly yarn-connected (order-independent).</summary>
    public bool AreConnected(CardBehaviour a, CardBehaviour b)
        => _connections.Exists(c =>
            (c.From.Card == a && c.To.Card == b) ||
            (c.From.Card == b && c.To.Card == a));

    /// <summary>
    /// Read-only view of all active connections — used by SaveSystem to snapshot the board.
    /// </summary>
    public IReadOnlyList<YarnConnection> GetAllConnections() => _connections;

    /// <summary>
    /// Silently restores a yarn connection between two cards without firing
    /// GameEvents.YarnConnected. Use during save-load restoration so the
    /// Minotaur Counter is not re-incremented and PropSwap effects don't re-trigger.
    /// </summary>
    public void RestoreConnection(CardBehaviour a, CardBehaviour b)
    {
        // Find the PinBehaviours that belong to these cards.
        PinBehaviour pinA = FindPinForCard(a);
        PinBehaviour pinB = FindPinForCard(b);

        if (pinA == null || pinB == null)
        {
            Debug.LogWarning($"[YarnSystem] RestoreConnection: could not find pins for " +
                             $"'{a?.cardTitle}' and/or '{b?.cardTitle}'. Skipping.");
            return;
        }

        // Guard: don't duplicate existing connections.
        if (_connections.Exists(c => c.Involves(pinA, pinB))) return;

        // Create the visual connection silently (no event fired).
        var go   = new GameObject("YarnConnection");
        go.transform.SetParent(transform);
        var conn = go.AddComponent<YarnConnection>();
        conn.Init(pinA, pinB, new Material(_yarnMat), yarnWidth, boardTransform);
        _connections.Add(conn);
    }

    PinBehaviour FindPinForCard(CardBehaviour card)
    {
        foreach (var pin in FindObjectsByType<PinBehaviour>(FindObjectsSortMode.None))
            if (pin.Card == card) return pin;
        return null;
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _cam = Camera.main;

        _yarnMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        _yarnMat.SetColor("_BaseColor", yarnColor);

        _preview = gameObject.AddComponent<LineRenderer>();
        _preview.material = _yarnMat;
        _preview.startWidth = yarnWidth;
        _preview.endWidth = yarnWidth;
        _preview.useWorldSpace = true;
        _preview.positionCount = 12;
        _preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _preview.enabled = false;
    }

    void Update()
    {
        if (_pendingFrom == null) return;
        UpdatePreview();
        if (Mouse.current.rightButton.wasPressedThisFrame)
            CancelYarn();
    }

    public void StartYarn(PinBehaviour from)
    {
        if (GetConnectionCount(from.Card) >= maxConnectionsPerCard) return;
        _pendingFrom = from;
        _preview.enabled = true;
    }

    public void TryCompleteYarn(PinBehaviour to)
    {
        if (_pendingFrom == null) return;
        if (to == _pendingFrom) { CancelYarn(); return; }
        if (to.Card == _pendingFrom.Card) { CancelYarn(); return; }
        if (_connections.Exists(c => c.Involves(_pendingFrom, to))) { CancelYarn(); return; }
        if (GetConnectionCount(to.Card) >= maxConnectionsPerCard) { CancelYarn(); return; }

        CreateConnection(_pendingFrom, to);
        CancelYarn();
    }

    void CreateConnection(PinBehaviour from, PinBehaviour to)
    {
        GameObject go = new GameObject("YarnConnection");
        go.transform.SetParent(transform);
        YarnConnection conn = go.AddComponent<YarnConnection>();
        conn.Init(from, to, new Material(_yarnMat), yarnWidth, boardTransform);
        _connections.Add(conn);
        GameEvents.YarnConnected(from.Card, to.Card);
    }

    public void RemoveConnectionsForCard(CardBehaviour card)
    {
        for (int i = _connections.Count - 1; i >= 0; i--)
        {
            if (_connections[i].From.Card == card || _connections[i].To.Card == card)
            {
                Destroy(_connections[i].gameObject);
                _connections.RemoveAt(i);
            }
        }
    }

    void UpdatePreview()
    {
        Vector3 start = _pendingFrom.transform.position;
        Vector3 outward = boardTransform.forward.normalized * boardOffset;

        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane boardPlane = new Plane(boardTransform.forward, _pendingFrom.transform.position);

        if (boardPlane.Raycast(ray, out float dist))
        {
            Vector3 end = ray.GetPoint(dist);
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                Vector3 point = Vector3.Lerp(start, end, t);
                point += outward;
                point += outward * Mathf.Sin(t * Mathf.PI) * previewSag;
                _preview.SetPosition(i, point);
            }
        }
    }

    void CancelYarn()
    {
        _pendingFrom = null;
        _preview.enabled = false;
    }

    int GetConnectionCount(CardBehaviour card)
    {
        int count = 0;
        foreach (var c in _connections)
            if (c.From.Card == card || c.To.Card == card) count++;
        return count;
    }
}