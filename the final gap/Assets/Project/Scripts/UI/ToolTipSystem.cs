using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem Instance { get; private set; }

    [SerializeField] RectTransform centerContainer;
    [SerializeField] TextMeshProUGUI exitBoardHint;
    [SerializeField] GameObject hintPrefab;

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

        GameState state = GameManager.Instance.CurrentState;

        exitBoardHint.gameObject.SetActive(state == GameState.BoardMode);

        if (YarnSystem.Instance.IsPending)
        {
            AddHint("LMB", "Connect");
            AddHint("RMB", "Cancel");
            return;
        }

        if (state == GameState.BoardMode)
        {
            if (BoardDragSystem.Instance.IsDragging)
                AddHint("LMB", "Release");
            else if (_currentFocus is PinBehaviour)
                AddHint("LMB", "Connect Yarn");
            else if (_currentFocus is CardBehaviour c && c.IsPinned)
                AddHint("LMB", "Grab");
            return;
        }

        if (PickupSystem.Instance.IsHolding)
        {
            AddHint("LMB", "Throw");
            AddHint("F", "Pocket");
            return;
        }

        if (_currentFocus != null && !string.IsNullOrEmpty(_currentFocus.promptText))
        {
            string key = _currentFocus.interactKey == InteractKey.UseKey ? "E" : "LMB";
            AddHint(key, _currentFocus.promptText);
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
}