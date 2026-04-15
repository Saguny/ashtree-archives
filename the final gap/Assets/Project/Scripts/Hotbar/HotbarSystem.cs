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

    public int SelectedIndex => _selectedIndex;
    public int MaxSlots => maxSlots;
    public CardBehaviour SelectedCard => _slots[_selectedIndex];

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
        else if (GameManager.Instance.CurrentState != GameState.BoardMode)
        {
            TryUnpocket();
        }
    }

    void TryPocket()
    {
        CardBehaviour card = PickupSystem.Instance.HeldCard;
        if (card == null) return;

        int emptySlot = FindEmptySlot();
        if (emptySlot == -1) return;

        PickupSystem.Instance.ForceRelease();
        _slots[emptySlot] = card;
        card.gameObject.SetActive(false);
        _selectedIndex = emptySlot;

        GameEvents.HotbarChanged(_slots, _selectedIndex);
    }

    void TryUnpocket()
    {
        if (_slots[_selectedIndex] == null) return;

        CardBehaviour card = _slots[_selectedIndex];
        _slots[_selectedIndex] = null;

        Camera cam = Camera.main;
        Vector3 dropPos = cam.transform.position + cam.transform.forward * unpocketDropDistance;

        card.gameObject.SetActive(true);
        card.ReturnToOrigin();
        card.Rigidbody.useGravity = true;
        card.Rigidbody.isKinematic = false;
        card.transform.position = dropPos;

        GameEvents.HotbarChanged(_slots, _selectedIndex);
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

}

