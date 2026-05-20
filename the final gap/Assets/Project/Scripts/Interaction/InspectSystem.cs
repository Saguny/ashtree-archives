using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// [Q] Inspect Mode
///
/// The inspected object is moved to a dedicated "Inspect" layer and rendered
/// by a post-process-free overlay camera stacked on top of the main camera.
/// The main camera runs Gaussian DOF on everything else — background gets
/// obliterated while the object stays perfectly sharp.
///
/// One-time manual setup:
///   1. Edit → Project Settings → Tags and Layers → add a layer named "Inspect".
///   2. Create a Volume (Global) with a Depth Of Field (Gaussian) override and
///      assign it to Inspect Volume below. Leave weight at 0.
///   (Vignette is procedural — no extra scene setup needed.)
/// </summary>
public class InspectSystem : MonoBehaviour
{
    public static InspectSystem Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Inspect View")]
    [SerializeField] float inspectDistance     = 0.28f;
    [SerializeField] float lerpSpeed           = 14f;
    [SerializeField] float rotationSensitivity = 150f;

    [Header("Blur (DOF on main camera)")]
    [Tooltip("Volume with a Gaussian Depth Of Field override. Weight starts at 0.")]
    [SerializeField] Volume inspectVolume;
    [Tooltip("Gap in metres between the object surface and where blur starts.")]
    [SerializeField] float blurStartOffset  = 0.06f;
    [Tooltip("Distance over which blur ramps to max (smaller = harder edge).")]
    [SerializeField] float blurFalloffRange = 0.10f;

    [Header("Vignette")]
    [SerializeField] float maxVignetteAlpha = 0.85f;
    [SerializeField] int   vignetteSortOrder = 50;

    [Header("Layer")]
    [Tooltip("Name of the layer reserved for the inspected object. Must exist in Project Settings.")]
    [SerializeField] string inspectLayerName = "Inspect";

    // ── Runtime ──────────────────────────────────────────────────────────────

    bool          _active;
    GameObject    _target;
    Rigidbody     _targetRb;
    bool          _targetFromHotbar;
    bool          _targetFromInventory;
    int           _inventoryReturnSlot;   // which hotbar slot to restore to on exit
    bool          _inventoryReturnIsTape; // true when the inventory item is a tape
    CardBehaviour _hotbarCard;
    VhsTape       _inventoryTape;

    float _inspectT;

    // Post-processing
    DepthOfField _dof;

    // Overlay camera (renders inspected object with NO post-processing)
    Camera _overlayCamera;
    int    _inspectLayer    = -1;
    int    _originalCullingMask;
    readonly Dictionary<Transform, int> _savedLayers = new Dictionary<Transform, int>();

    // Procedural vignette
    RawImage _vignetteImage;

    public bool IsActive => _active;

    // ── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        BuildVignetteOverlay();
    }

    void Start()
    {
        // Resolve inspect layer
        _inspectLayer = LayerMask.NameToLayer(inspectLayerName);
        if (_inspectLayer == -1)
            Debug.LogError($"[InspectSystem] Layer '{inspectLayerName}' not found — " +
                           "add it in Edit → Project Settings → Tags and Layers.");

        // Cache DOF from volume
        if (inspectVolume != null)
        {
            inspectVolume.profile.TryGet(out _dof);
            if (_dof == null)
                Debug.LogWarning("[InspectSystem] No Depth Of Field override in the inspect volume profile.");
        }
        else
        {
            Debug.LogWarning("[InspectSystem] inspectVolume not assigned — background blur disabled.");
        }

        // Build overlay camera parented to the main camera
        BuildOverlayCamera();
    }

    void Update()
    {
        if (!Keyboard.current.qKey.wasPressedThisFrame) return;

        if (_active)
            ExitInspect();
        else if (GameManager.Instance.CurrentState == GameState.Exploration)
            TryEnterInspect();
    }

    void LateUpdate()
    {
        float targetT = _active ? 1f : 0f;
        _inspectT = Mathf.Lerp(_inspectT, targetT, lerpSpeed * Time.deltaTime);

        if (_active && _target != null)
        {
            MaintainPosition();
            HandleRotation();
            UpdateDOF();
        }

        ApplyPostProcess(_inspectT);
    }

    // ── Enter / Exit ─────────────────────────────────────────────────────────

    void TryEnterInspect()
    {
        GameObject target = null;
        Rigidbody  rb     = null;

        if (PickupSystem.Instance.IsHolding)
        {
            rb     = PickupSystem.Instance.HeldRigidbody;
            target = rb != null ? rb.gameObject : null;
            if (target == null) return;

            PickupSystem.Instance.ForceRelease();
            // Zero velocities BEFORE going kinematic — kinematic bodies reject velocity writes.
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity      = false;
            rb.isKinematic     = true;
            // Kill interpolation: PickupSystem sets it to Interpolate, which fights
            // our LateUpdate transform writes and causes visible jitter.
            rb.interpolation   = RigidbodyInterpolation.None;
        }
        else
        {
            CardBehaviour card = HotbarSystem.Instance.SelectedCard;
            if (card == null) return;

            _hotbarCard       = card;
            _targetFromHotbar = true;
            card.gameObject.SetActive(true);
            rb     = card.Rigidbody;
            target = card.gameObject;
            // Same order: velocities first, then kinematic.
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity      = false;
            rb.isKinematic     = true;
            rb.interpolation   = RigidbodyInterpolation.None;
        }

        _target   = target;
        _targetRb = rb;
        _active   = true;

        // Configure DOF once on enter
        if (_dof != null)
        {
            _dof.mode.Override(DepthOfFieldMode.Gaussian);
            _dof.gaussianMaxRadius.Override(1.5f);
            _dof.highQualitySampling.Override(true);
        }

        // Move object to Inspect layer so the overlay camera owns it
        // and the main camera (with DOF) never sees it.
        if (_inspectLayer != -1)
        {
            SaveAndSetLayers(target, _inspectLayer);
            _originalCullingMask    = Camera.main.cullingMask;
            Camera.main.cullingMask = _originalCullingMask & ~(1 << _inspectLayer);
            if (_overlayCamera != null) _overlayCamera.enabled = true;
        }

        SnapToInspectPosition();
        GameManager.Instance.SetState(GameState.InspectMode);
    }

    void ExitInspect()
    {
        _active = false;

        // Restore layer and camera mask before we pocket/drop so physics is correct
        RestoreLayers();
        Camera.main.cullingMask = _originalCullingMask;
        if (_overlayCamera != null) _overlayCamera.enabled = false;

        if (_targetFromInventory)
        {
            // Return item to the exact inventory slot it came from, then reopen inventory.
            if (_inventoryReturnIsTape && _inventoryTape != null)
            {
                _inventoryTape.Rigidbody.isKinematic = true;
                _inventoryTape.gameObject.SetActive(false);
                HotbarSystem.Instance.TryPocketTapeDirect(_inventoryTape);
                _inventoryTape = null;
            }
            else if (_hotbarCard != null)
            {
                _hotbarCard.Rigidbody.isKinematic = true;
                _hotbarCard.gameObject.SetActive(false);
                HotbarSystem.Instance.RestoreToSlot(_hotbarCard, _inventoryReturnSlot);
                _hotbarCard = null;
            }

            _targetFromInventory    = false;
            _inventoryReturnIsTape  = false;
            _target   = null;
            _targetRb = null;

            GameManager.Instance.SetState(GameState.Exploration);
            // Reopen the inventory panel after a frame so the state change settles.
            StartCoroutine(ReopenInventory());
            return;
        }

        if (_targetFromHotbar && _hotbarCard != null)
        {
            _hotbarCard.Rigidbody.isKinematic = true;
            _hotbarCard.gameObject.SetActive(false);
            _hotbarCard       = null;
            _targetFromHotbar = false;
        }
        else if (_targetRb != null)
        {
            bool pocketed = false;

            CardBehaviour card = _target.GetComponent<CardBehaviour>();
            VhsTape       tape = _target.GetComponent<VhsTape>();

            if (card != null)       pocketed = HotbarSystem.Instance.TryPocketDirect(card);
            else if (tape != null)  pocketed = HotbarSystem.Instance.TryPocketTapeDirect(tape);

            if (pocketed)
                TooltipSystem.Instance.ShowNotification("Pocketed");
            else
            {
                _targetRb.isKinematic = false;
                _targetRb.useGravity  = true;
            }
        }

        _target   = null;
        _targetRb = null;

        GameManager.Instance.SetState(GameState.Exploration);
        // _inspectT drifts back to 0 — vignette + DOF ease out smoothly.
    }

    System.Collections.IEnumerator ReopenInventory()
    {
        yield return null; // wait one frame
        RunInventorySystem.Instance?.Open();
    }

    // ── Per-frame ────────────────────────────────────────────────────────────

    void MaintainPosition()
    {
        Camera  cam    = Camera.main;
        Vector3 anchor = cam.transform.position + cam.transform.forward * inspectDistance;
        _target.transform.position = Vector3.Lerp(
            _target.transform.position, anchor, lerpSpeed * Time.deltaTime);
    }

    void HandleRotation()
    {
        Vector2 delta = Mouse.current.delta.ReadValue();
        if (delta.sqrMagnitude < 0.01f) return;

        float  dt  = Time.deltaTime;
        Camera cam = Camera.main;
        _target.transform.Rotate(cam.transform.up,    -delta.x * rotationSensitivity * dt, Space.World);
        _target.transform.Rotate(cam.transform.right,  delta.y * rotationSensitivity * dt, Space.World);
    }

    void SnapToInspectPosition()
    {
        Camera cam = Camera.main;
        _target.transform.position = cam.transform.position + cam.transform.forward * inspectDistance;
    }

    // ── Post-processing ───────────────────────────────────────────────────────

    void UpdateDOF()
    {
        if (_dof == null || _target == null) return;

        // Track the object's actual distance so the sharp zone is always behind it.
        float d = Vector3.Distance(Camera.main.transform.position, _target.transform.position);
        _dof.gaussianStart.Override(d + blurStartOffset);
        _dof.gaussianEnd.Override(d   + blurStartOffset + blurFalloffRange);
    }

    void ApplyPostProcess(float t)
    {
        if (inspectVolume != null)
            inspectVolume.weight = t;

        if (_vignetteImage != null)
        {
            Color c = _vignetteImage.color;
            _vignetteImage.color = new Color(c.r, c.g, c.b, t * maxVignetteAlpha);
        }
    }

    // ── Layer helpers ─────────────────────────────────────────────────────────

    void SaveAndSetLayers(GameObject root, int layer)
    {
        _savedLayers.Clear();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            _savedLayers[t] = t.gameObject.layer;
            t.gameObject.layer = layer;
        }
    }

    void RestoreLayers()
    {
        foreach (var kvp in _savedLayers)
            if (kvp.Key != null)
                kvp.Key.gameObject.layer = kvp.Value;
        _savedLayers.Clear();
    }

    // ── Overlay camera ────────────────────────────────────────────────────────

    void BuildOverlayCamera()
    {
        if (_inspectLayer == -1) return;

        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[InspectSystem] No main camera found."); return; }

        var go = new GameObject("InspectOverlayCamera");
        go.transform.SetParent(cam.transform);
        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        _overlayCamera              = go.AddComponent<Camera>();
        _overlayCamera.cullingMask  = 1 << _inspectLayer;
        _overlayCamera.clearFlags   = CameraClearFlags.Depth;
        _overlayCamera.nearClipPlane = cam.nearClipPlane;
        _overlayCamera.farClipPlane  = cam.farClipPlane;
        _overlayCamera.enabled      = false;

        // URP camera stack: overlay renders on top, NO post-processing (no DOF).
        var overlayData = _overlayCamera.GetUniversalAdditionalCameraData();
        overlayData.renderType           = CameraRenderType.Overlay;
        overlayData.renderPostProcessing = false;

        var mainData = cam.GetUniversalAdditionalCameraData();
        mainData.cameraStack.Add(_overlayCamera);
    }

    // ── Procedural vignette ───────────────────────────────────────────────────

    void BuildVignetteOverlay()
    {
        var canvasGo = new GameObject("InspectVignetteOverlay");
        canvasGo.transform.SetParent(transform);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = vignetteSortOrder;
        canvasGo.AddComponent<CanvasScaler>();

        var imgGo = new GameObject("Vignette");
        imgGo.transform.SetParent(canvasGo.transform, false);

        _vignetteImage         = imgGo.AddComponent<RawImage>();
        _vignetteImage.texture = GenerateVignetteTexture(256);
        _vignetteImage.color   = new Color(0f, 0f, 0f, 0f);

        var rect       = imgGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static Texture2D GenerateVignetteTexture(int size)
    {
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = new Vector2(0.5f, 0.5f);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u     = (float)x / (size - 1);
            float v     = (float)y / (size - 1);
            float dist  = Vector2.Distance(new Vector2(u, v), center) * 2f;
            float alpha = Mathf.Clamp01(Mathf.Pow(dist, 2.2f));
            tex.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
        }

        tex.Apply();
        return tex;
    }

    // ── Inventory-examine entry points ────────────────────────────────────────

    /// <summary>
    /// Called by RunInventorySystem.ExamineSlot(). Begins an inspect session for a card
    /// that is currently pocketed in the hotbar. When Q is pressed to exit, the card
    /// is automatically returned to <paramref name="returnSlot"/> and the inventory panel
    /// is reopened.
    /// </summary>
    public void BeginInspectFromInventory(CardBehaviour card, int returnSlot)
    {
        if (_active || card == null) return;

        _targetFromInventory    = true;
        _inventoryReturnSlot    = returnSlot;
        _inventoryReturnIsTape  = false;
        _hotbarCard             = card;

        var rb = card.Rigidbody;
        card.gameObject.SetActive(true);
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity      = false;
        rb.isKinematic     = true;
        rb.interpolation   = RigidbodyInterpolation.None;

        _target   = card.gameObject;
        _targetRb = rb;
        _active   = true;

        SetupDOF();
        SetupInspectLayer(_target);
        SnapToInspectPosition();
        GameManager.Instance.SetState(GameState.InspectMode);
    }

    /// <summary>
    /// Called by RunInventorySystem.ExamineTape(). Same behaviour as BeginInspectFromInventory
    /// but for the tape slot.
    /// </summary>
    public void BeginInspectTapeFromInventory(VhsTape tape)
    {
        if (_active || tape == null) return;

        _targetFromInventory    = true;
        _inventoryReturnIsTape  = true;
        _inventoryTape          = tape;

        var rb = tape.Rigidbody;
        tape.gameObject.SetActive(true);
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity      = false;
        rb.isKinematic     = true;
        rb.interpolation   = RigidbodyInterpolation.None;

        _target   = tape.gameObject;
        _targetRb = rb;
        _active   = true;

        SetupDOF();
        SetupInspectLayer(_target);
        SnapToInspectPosition();
        GameManager.Instance.SetState(GameState.InspectMode);
    }

    // ── Shared inspect setup helpers ──────────────────────────────────────────

    void SetupDOF()
    {
        if (_dof == null) return;
        _dof.mode.Override(DepthOfFieldMode.Gaussian);
        _dof.gaussianMaxRadius.Override(1.5f);
        _dof.highQualitySampling.Override(true);
    }

    void SetupInspectLayer(GameObject target)
    {
        if (_inspectLayer == -1) return;
        SaveAndSetLayers(target, _inspectLayer);
        _originalCullingMask    = Camera.main.cullingMask;
        Camera.main.cullingMask = _originalCullingMask & ~(1 << _inspectLayer);
        if (_overlayCamera != null) _overlayCamera.enabled = true;
    }
}
