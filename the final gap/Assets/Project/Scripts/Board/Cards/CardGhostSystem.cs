using UnityEngine;
using UnityEngine.InputSystem;

public class CardGhostSystem : MonoBehaviour
{
    public static CardGhostSystem Instance { get; private set; }

    [Header("Ghost Settings")]
    [SerializeField] float ghostAlpha = 0.4f;
    [SerializeField] Transform boardTransform;

    GameObject _ghost;
    Renderer _ghostRenderer;
    Material _ghostMat;
    Camera _cam;
    Plane _boardPlane;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _cam = Camera.main;
    }

    void OnEnable()
    {
        GameEvents.OnGameStateChanged += OnStateChanged;
        GameEvents.OnHotbarChanged += OnHotbarChanged;
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= OnStateChanged;
        GameEvents.OnHotbarChanged -= OnHotbarChanged;
    }

    void OnHotbarChanged(CardBehaviour[] slots, int selected)
    {
        if (GameManager.Instance.CurrentState != GameState.BoardMode) return;
        HideGhost();
        ShowGhost();
    }

    void OnStateChanged(GameState state)
    {
        if (state == GameState.BoardMode)
            ShowGhost();
        else
            HideGhost();
    }

    void Update()
    {
        if (GameManager.Instance.CurrentState != GameState.BoardMode) return;
        if (_ghost == null) return;

        UpdateGhostPosition();

        if (Mouse.current.leftButton.wasPressedThisFrame)
            TryPin();
    }

    void ShowGhost()
    {
        CardBehaviour selected = HotbarSystem.Instance.SelectedCard;
        if (selected == null) return;

        _ghost = Instantiate(selected.gameObject);
        _ghost.SetActive(true);

        foreach (var col in _ghost.GetComponents<Collider>())
            col.enabled = false;

        Destroy(_ghost.GetComponent<CardBehaviour>());
        Destroy(_ghost.GetComponent<Rigidbody>());

        _ghostRenderer = _ghost.GetComponent<Renderer>();
        _ghostMat = new Material(_ghostRenderer.sharedMaterial);
        _ghostMat.SetFloat("_Surface", 1);
        _ghostMat.SetFloat("_Blend", 0);
        Color c = _ghostMat.color;
        _ghostMat.color = new Color(c.r, c.g, c.b, ghostAlpha);
        _ghostRenderer.material = _ghostMat;

        _boardPlane = new Plane(-boardTransform.forward,
        boardTransform.position + boardTransform.forward * 0.1f);
    }

    void HideGhost()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
    }

    void UpdateGhostPosition()
    {
        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (_boardPlane.Raycast(ray, out float dist))
        {
            Vector3 pos = ray.GetPoint(dist);
            _ghost.transform.position = pos;
            _ghost.transform.rotation = boardTransform.rotation * Quaternion.Euler(90f, 0f, 0f);
        }
    }

    void TryPin()
    {
        CardBehaviour selected = HotbarSystem.Instance.SelectedCard;
        if (selected == null) return;

        SlotBehaviour nearest = FindNearestSlot();
        if (nearest == null || nearest.IsOccupied) return;

        selected.gameObject.SetActive(true);
        selected.transform.position = _ghost.transform.position;
        selected.transform.rotation = _ghost.transform.rotation;

        nearest.TryOccupy(selected);        // pin first
        HotbarSystem.Instance.RemoveSelected(); // remove after

        HideGhost();
        ShowGhost();
    }

    SlotBehaviour FindNearestSlot()
    {
        SlotBehaviour nearest = null;
        float nearestDist = 0.3f;

        foreach (var slot in FindObjectsByType<SlotBehaviour>(FindObjectsSortMode.None))
        {
            float dist = Vector3.Distance(_ghost.transform.position, slot.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = slot;
            }
        }
        return nearest;
    }


}