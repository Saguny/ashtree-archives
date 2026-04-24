using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem Instance { get; private set; }

    [SerializeField] RectTransform centerContainer;
    [SerializeField] TextMeshProUGUI exitBoardHint;
    [SerializeField] GameObject hintPrefab;

    [Header("Card Name")]
    [Tooltip("Text element sitting above the hint container. Assign in Inspector.")]
    [SerializeField] TextMeshProUGUI cardNameLabel;

    List<GameObject> _activeHints = new List<GameObject>();
    Interactable _currentFocus;
    bool _wasHolding, _wasDragging, _wasYarnPending;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        GameEvents.OnGameStateChanged += _ => Refresh();
        GameEvents.OnInteractableFocused += OnFocusChanged;
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= _ => Refresh();
        GameEvents.OnInteractableFocused -= OnFocusChanged;
    }

    void OnFocusChanged(Interactable target)
    {
        _currentFocus = target;
        Refresh();
    }

    void Update()
    {
        bool holding = PickupSystem.Instance.IsHolding;
        bool dragging = BoardDragSystem.Instance.IsDragging;
        bool yarnPending = YarnSystem.Instance.IsPending;
        if (holding != _wasHolding || dragging != _wasDragging || yarnPending != _wasYarnPending)
        {
            _wasHolding = holding;
            _wasDragging = dragging;
            _wasYarnPending = yarnPending;
            Refresh();
        }
    }

    void Refresh()
    {
        ClearHints();
        ClearCardName();
        GameState state = GameManager.Instance.CurrentState;
        // Show the exit hint canvas element for both board and TV modes
        exitBoardHint.gameObject.SetActive(state == GameState.BoardMode || state == GameState.VhsMode);

        if (YarnSystem.Instance.IsPending)
        {
            AddHint("LMB", "Connect");
            AddHint("RMB", "Cancel");
            return;
        }

        if (state == GameState.BoardMode)
        {
            if (BoardDragSystem.Instance.IsDragging)
            {
                AddHint("LMB", "Release");
            }
            else if (_currentFocus is PinBehaviour pin)
            {
                ShowCardName(pin.Card);
                AddHint("LMB", "Connect Yarn");
            }
            else if (_currentFocus is CardBehaviour c && c.IsPinned)
            {
                ShowCardName(c);
                AddHint("LMB", "Grab");
            }
            return;
        }

        // ── VhsMode ───────────────────────────────────────────────────────
        if (state == GameState.VhsMode)
        {
            // Only show hints for focused interactables (e.g. [LMB] Insert Tape).
            // No unpocket or hold hints — insert slot pulls from hotbar automatically.
            if (_currentFocus != null && !string.IsNullOrEmpty(_currentFocus.promptText)
                && _currentFocus.IsWithinInteractDistance(Camera.main.transform.position))
            {
                string vhsKey = _currentFocus.interactKey == InteractKey.LeftClick ? "LMB" : "E";
                AddHint(vhsKey, _currentFocus.promptText);
            }

            return;
        }

        // ── Exploration: holding something ────────────────────────────────
        if (PickupSystem.Instance.IsHolding)
        {
            AddHint("LMB", "Throw");
            AddHint("F", "Pocket");
            return;
        }

        // ── Focus hint ────────────────────────────────────────────────────
        if (_currentFocus != null && !string.IsNullOrEmpty(_currentFocus.promptText))
        {
            // Pinned cards can't be picked up outside BoardMode — suppress the hint
            if (_currentFocus is CardBehaviour pinnedCheck && pinnedCheck.IsPinned) return;

            Vector3 playerPos = Camera.main.transform.position;
            if (_currentFocus.IsWithinInteractDistance(playerPos))
            {
                if (_currentFocus is CardBehaviour card)
                    ShowCardName(card);

                string key = _currentFocus.interactKey == InteractKey.UseKey ? "E" : "LMB";
                AddHint(key, _currentFocus.promptText);
            }
        }
    }

    void AddHint(string key, string action)
    {
        GameObject hint = Instantiate(hintPrefab, centerContainer);
        hint.SetActive(true);
        _activeHints.Add(hint);

        TextMeshProUGUI text = hint.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
            text.text = $"[{key}] {action}";
    }

    void ClearHints()
    {
        foreach (var h in _activeHints) Destroy(h);
        _activeHints.Clear();
    }

    void ShowCardName(CardBehaviour card)
    {
        if (cardNameLabel == null || string.IsNullOrEmpty(card.cardTitle)) return;
        cardNameLabel.text = card.cardTitle;
        cardNameLabel.gameObject.SetActive(true);
    }

    void ClearCardName()
    {
        if (cardNameLabel == null) return;
        cardNameLabel.text = string.Empty;
        cardNameLabel.gameObject.SetActive(false);
    }
}