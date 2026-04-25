using UnityEngine;
using UnityEngine.InputSystem;

public class HotbarSystem : MonoBehaviour
{
    public static HotbarSystem Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] int maxSlots = 3;
    [SerializeField] float unpocketDropDistance = 1.2f;

    CardBehaviour[] _slots;
    int _selectedIndex = 0;

    // Tape lives in its own slot alongside cards — same F key, same pocket feel
    VhsTape _storedTape;

    public int SelectedIndex => _selectedIndex;
    public int MaxSlots => maxSlots;
    public CardBehaviour SelectedCard => _slots[_selectedIndex];
    public VhsTape StoredTape => _storedTape;
    public bool HasTape => _storedTape != null;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _slots = new CardBehaviour[maxSlots];
    }

    void Update()
    {
        HandleCycling();

        if (Keyboard.current.fKey.wasPressedThisFrame)
            HandlePocketToggle();
    }

    void HandleCycling()
    {
        var scroll = UnityEngine.InputSystem.InputSystem.actions.FindAction("UI/ScrollWheel");
        if (scroll == null) return;

        float y = scroll.ReadValue<Vector2>().y;
        if (y > 0f)
        {
            _selectedIndex = (_selectedIndex - 1 + maxSlots) % maxSlots;
            GameEvents.HotbarChanged(_slots, _selectedIndex);
        }
        else if (y < 0f)
        {
            _selectedIndex = (_selectedIndex + 1) % maxSlots;
            GameEvents.HotbarChanged(_slots, _selectedIndex);

        }
    }

    void HandlePocketToggle()
    {
        if (PickupSystem.Instance.IsHolding)
        {
            TryPocket();
        }
        else if (GameManager.Instance.CurrentState != GameState.BoardMode
              && GameManager.Instance.CurrentState != GameState.VhsMode)
        {
            TryUnpocket();
        }
    }

    void TryPocket()
    {
        // Pocket a card if held
        CardBehaviour card = PickupSystem.Instance.HeldCard;
        if (card != null)
        {
            int emptySlot = FindEmptySlot();
            if (emptySlot == -1) return;

            PickupSystem.Instance.ForceRelease();
            _slots[emptySlot] = card;
            card.gameObject.SetActive(false);
            _selectedIndex = emptySlot;

            GameEvents.HotbarChanged(_slots, _selectedIndex);
            return;
        }

        // Pocket a tape if held (single tape slot, separate from cards)
        VhsTape tape = PickupSystem.Instance.HeldTape;
        if (tape != null && _storedTape == null)
        {
            PickupSystem.Instance.ForceRelease();
            _storedTape = tape;
            tape.gameObject.SetActive(false);

            GameEvents.HotbarChanged(_slots, _selectedIndex);
        }
    }

    void TryUnpocket()
    {
        // Unpocket card from selected slot — hold it in-hand via PickupSystem
        if (_slots[_selectedIndex] != null)
        {
            CardBehaviour card = _slots[_selectedIndex];
            _slots[_selectedIndex] = null;

            Camera cam = Camera.main;
            Vector3 spawnPos = cam.transform.position + cam.transform.forward * unpocketDropDistance;

            card.gameObject.SetActive(true);
            card.Rigidbody.isKinematic = false;
            card.Rigidbody.linearVelocity = Vector3.zero;
            card.Rigidbody.angularVelocity = Vector3.zero;
            card.transform.position = spawnPos;

            PickupSystem.Instance.PickUp(card);
            GameEvents.HotbarChanged(_slots, _selectedIndex);
            return;
        }

        // Unpocket tape — in VhsMode spawn right in front of the machine
        if (_storedTape != null)
        {
            Camera cam = Camera.main;
            // In VhsMode the camera is parked at the TV; use a short distance so
            // the tape materialises right in front of the machine face.
            float dist = GameManager.Instance.CurrentState == GameState.VhsMode
                ? 0.25f
                : unpocketDropDistance;
            Vector3 spawnPos = cam.transform.position + cam.transform.forward * dist;

            _storedTape.gameObject.SetActive(true);
            _storedTape.Rigidbody.isKinematic = false;
            _storedTape.Rigidbody.linearVelocity = Vector3.zero;
            _storedTape.Rigidbody.angularVelocity = Vector3.zero;
            _storedTape.transform.position = spawnPos;

            VhsTape t = _storedTape;
            _storedTape = null;
            PickupSystem.Instance.PickUp(t);

            GameEvents.HotbarChanged(_slots, _selectedIndex);
        }
    }

    /// <summary>Called by VhsInsertSlot to consume the stored tape.</summary>
    public VhsTape RemoveTape()
    {
        VhsTape tape = _storedTape;
        _storedTape = null;
        GameEvents.HotbarChanged(_slots, _selectedIndex);
        return tape;
    }

    public CardBehaviour GetSlot(int index) => _slots[index];

    public void RemoveSelected()
    {
        _slots[_selectedIndex] = null;
        GameEvents.HotbarChanged(_slots, _selectedIndex);
    }

    int FindEmptySlot()
    {
        for (int i = 0; i < maxSlots; i++)
            if (_slots[i] == null) return i;
        return -1;
    }

    // ── Dev / Debug ───────────────────────────────────────────────────────────

    /// <summary>
    /// Clears all card slots and the tape slot, re-activating any hidden GameObjects.
    /// Dev tool only — does not fire pickup/drop events.
    /// </summary>
    public void DevClearAllSlots()
    {
        for (int i = 0; i < maxSlots; i++)
        {
            if (_slots[i] != null)
            {
                _slots[i].gameObject.SetActive(true);
                _slots[i] = null;
            }
        }

        if (_storedTape != null)
        {
            _storedTape.gameObject.SetActive(true);
            _storedTape = null;
        }

        GameEvents.HotbarChanged(_slots, _selectedIndex);
    }

}

