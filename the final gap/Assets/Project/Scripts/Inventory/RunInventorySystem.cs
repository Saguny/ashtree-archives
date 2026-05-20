using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the run inventory — a slide-in half-screen panel the player can open
/// mid-exploration to review picked-up items, drop them, or examine them.
///
/// DESIGN RULES
///   • Per trip : max 1 tape + 3 clue/evidence items.
///   • Items persist back to the office between trips.
///   • Once an item is pocketed its ID is recorded; it will NOT respawn on future house visits.
///   • Trip counts reset whenever the Office scene loads.
///
/// TOGGLE  : Tab (during Exploration or InventoryMode only)
///
/// CONTEXT MENU ACTIONS (called from RunInventoryUI)
///   TakeOutSlot(int)   — removes card from hotbar slot, spawns it on the ground, closes panel.
///   TakeOutTape()      — same for the tape slot.
///   ExamineSlot(int)   — closes panel, hands item to InspectSystem; on Q-exit item returns to slot.
///   ExamineTape()      — same for the tape slot.
///
/// SAVE / LOAD
///   SaveSystem calls PopulateSaveData / ApplySaveData as part of its normal save cycle.
/// </summary>
public class RunInventorySystem : MonoBehaviour
{
    public static RunInventorySystem Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Trip Limits")]
    [Tooltip("Maximum number of tape items carried per house trip. Reset when Office scene loads.")]
    [SerializeField] int maxTapesPerTrip = 1;
    [Tooltip("Maximum number of clue / evidence items carried per house trip.")]
    [SerializeField] int maxCluesPerTrip = 3;

    [Header("Scene Names")]
    [Tooltip("Exact name of the office scene. Trip counts reset here.")]
    [SerializeField] string officeSceneName = "Office";

    [Header("Take-Out Spawn")]
    [Tooltip("How far in front of the camera a taken-out item appears.")]
    [SerializeField] float takeOutDistance = 1.4f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    bool _isOpen;

    int _tripTapeCount;   // tapes pocketed this trip
    int _tripClueCount;   // clues pocketed this trip

    // Persisted: item IDs collected across all trips (keyed by prefab name or cardTitle)
    readonly HashSet<string> _collectedIds = new HashSet<string>();

    // ── Public read-only state ────────────────────────────────────────────────

    public bool IsOpen       => _isOpen;
    public int  TapeCount    => _tripTapeCount;
    public int  ClueCount    => _tripClueCount;
    public int  MaxTapes     => maxTapesPerTrip;
    public int  MaxClues     => maxCluesPerTrip;
    public bool CanTakeTape  => _tripTapeCount < maxTapesPerTrip;
    public bool CanTakeClue  => _tripClueCount < maxCluesPerTrip;

    /// <summary>Returns true if this item ID has been collected (and should not respawn).</summary>
    public bool IsCollected(string itemId) =>
        !string.IsNullOrEmpty(itemId) && _collectedIds.Contains(itemId);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded      += OnSceneLoaded;
        GameEvents.OnGameLoaded       += OnGameLoaded;
        GameEvents.OnGameStateChanged += OnGameStateChanged;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded      -= OnSceneLoaded;
        GameEvents.OnGameLoaded       -= OnGameLoaded;
        GameEvents.OnGameStateChanged -= OnGameStateChanged;
    }

    void Update()
    {
        var state = GameManager.Instance?.CurrentState;
        if (state != GameState.Exploration && state != GameState.InventoryMode) return;

        if (Keyboard.current.tabKey.wasPressedThisFrame)
            Toggle();
    }

    // ── Open / Close ──────────────────────────────────────────────────────────

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;
        GameManager.Instance.SetState(GameState.InventoryMode);
        GameEvents.InventoryToggled(true);
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        // Only reset to Exploration if we're still in InventoryMode
        // (InspectSystem may have already changed state)
        if (GameManager.Instance.CurrentState == GameState.InventoryMode)
            GameManager.Instance.SetState(GameState.Exploration);
        GameEvents.InventoryToggled(false);
    }

    public void Toggle() { if (_isOpen) Close(); else Open(); }

    // ── Trip limit API (called by HotbarSystem before pocketing) ─────────────

    /// <summary>
    /// Call this before pocketing a clue card. Registers the item as collected and
    /// increments the trip count. Returns false if the clue cap is already reached.
    /// </summary>
    public bool TryRegisterCluePickup(string itemId = "")
    {
        if (_tripClueCount >= maxCluesPerTrip)
        {
            Debug.Log($"[RunInventory] Clue slot full ({_tripClueCount}/{maxCluesPerTrip}) — cannot pocket.");
            return false;
        }
        _tripClueCount++;
        if (!string.IsNullOrEmpty(itemId))
            _collectedIds.Add(itemId);
        Debug.Log($"[RunInventory] Clue pocketed ('{itemId}'). Trip total: {_tripClueCount}/{maxCluesPerTrip}.");
        return true;
    }

    /// <summary>
    /// Call this before pocketing a tape. Returns false if the tape cap is reached.
    /// </summary>
    public bool TryRegisterTapePickup(string itemId = "")
    {
        if (_tripTapeCount >= maxTapesPerTrip)
        {
            Debug.Log($"[RunInventory] Tape slot full ({_tripTapeCount}/{maxTapesPerTrip}) — cannot pocket.");
            return false;
        }
        _tripTapeCount++;
        if (!string.IsNullOrEmpty(itemId))
            _collectedIds.Add(itemId);
        Debug.Log($"[RunInventory] Tape pocketed ('{itemId}'). Trip total: {_tripTapeCount}/{maxTapesPerTrip}.");
        return true;
    }

    /// <summary>Decrement the clue counter (e.g. on Take Out).</summary>
    public void UnregisterClue() => _tripClueCount = Mathf.Max(0, _tripClueCount - 1);

    /// <summary>Decrement the tape counter (e.g. on Take Out).</summary>
    public void UnregisterTape() => _tripTapeCount = Mathf.Max(0, _tripTapeCount - 1);

    // ── Context menu: Take Out ────────────────────────────────────────────────

    /// <summary>
    /// Drops the card in hotbar slot <paramref name="slotIndex"/> on the ground
    /// in front of the camera and closes the inventory panel.
    /// </summary>
    public void TakeOutSlot(int slotIndex)
    {
        CardBehaviour card = HotbarSystem.Instance?.GetSlot(slotIndex);
        if (card == null) return;

        HotbarSystem.Instance.RemoveSlot(slotIndex);
        SpawnInFrontOfCamera(card.gameObject, card.Rigidbody);
        UnregisterClue();

        Close();
    }

    /// <summary>Drops the stored tape on the ground and closes the inventory panel.</summary>
    public void TakeOutTape()
    {
        VhsTape tape = HotbarSystem.Instance?.StoredTape;
        if (tape == null) return;

        HotbarSystem.Instance.RemoveTape();
        SpawnInFrontOfCamera(tape.gameObject, tape.Rigidbody);
        UnregisterTape();

        Close();
    }

    void SpawnInFrontOfCamera(GameObject go, Rigidbody rb)
    {
        Camera cam   = Camera.main;
        Vector3 pos  = cam.transform.position + cam.transform.forward * takeOutDistance;

        go.SetActive(true);
        rb.isKinematic     = false;
        rb.useGravity      = true;
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        go.transform.position = pos;
    }

    // ── Context menu: Examine ─────────────────────────────────────────────────

    /// <summary>
    /// Hides the inventory panel and starts an inspect session for the card
    /// in hotbar slot <paramref name="slotIndex"/>. InspectSystem will reopen
    /// the inventory and return the card to the same slot when Q is pressed.
    /// </summary>
    public void ExamineSlot(int slotIndex)
    {
        CardBehaviour card = HotbarSystem.Instance?.GetSlot(slotIndex);
        if (card == null) return;

        // Close without resetting game state — InspectSystem will take it to InspectMode
        _isOpen = false;
        GameEvents.InventoryToggled(false);

        InspectSystem.Instance.BeginInspectFromInventory(card, slotIndex);
    }

    /// <summary>Same as ExamineSlot but for the tape slot.</summary>
    public void ExamineTape()
    {
        VhsTape tape = HotbarSystem.Instance?.StoredTape;
        if (tape == null) return;

        _isOpen = false;
        GameEvents.InventoryToggled(false);

        InspectSystem.Instance.BeginInspectTapeFromInventory(tape);
    }

    // ── Scene / State hooks ───────────────────────────────────────────────────

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Close inventory on any scene transition
        if (_isOpen)
        {
            _isOpen = false;
            GameEvents.InventoryToggled(false);
        }

        // Reset trip counts on every office visit
        if (scene.name == officeSceneName)
            ResetTripCounts();
    }

    void OnGameStateChanged(GameState state)
    {
        // If something else (e.g. BoardMode, InspectMode) grabs the game state
        // while inventory is logically open, snap inventory closed silently.
        if (_isOpen && state != GameState.InventoryMode)
        {
            _isOpen = false;
            GameEvents.InventoryToggled(false);
        }
    }

    public void ResetTripCounts()
    {
        _tripTapeCount = 0;
        _tripClueCount = 0;
        Debug.Log("[RunInventory] Trip counts reset.");
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    public void PopulateSaveData(SaveData data)
    {
        data.collectedItemIds.Clear();
        foreach (var id in _collectedIds)
            data.collectedItemIds.Add(id);
    }

    public void ApplySaveData(SaveData data)
    {
        _collectedIds.Clear();
        foreach (var id in data.collectedItemIds)
            if (!string.IsNullOrEmpty(id))
                _collectedIds.Add(id);

        Debug.Log($"[RunInventory] Loaded {_collectedIds.Count} collected item IDs.");
    }

    void OnGameLoaded(SaveData data) => ApplySaveData(data);

    // ── Dev API ───────────────────────────────────────────────────────────────

    /// <summary>Clears all collected-item records (dev only).</summary>
    public void DevClearCollected()
    {
        _collectedIds.Clear();
        Debug.Log("[RunInventory] DEV — collected IDs cleared.");
    }

    /// <summary>Returns a copy of all collected item IDs (for dev tool display).</summary>
    public IReadOnlyCollection<string> DevGetCollectedIds() => _collectedIds;
}
