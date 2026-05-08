using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem Instance { get; private set; }

    [SerializeField] RectTransform centerContainer;
    [Tooltip("The shared bottom-left exit hint (your existing [E] Exit label). Text is overwritten per-state.")]
    [SerializeField] TextMeshProUGUI exitBoardHint;
    [SerializeField] GameObject hintPrefab;

    [Header("Card Name")]
    [Tooltip("Text element sitting above the hint container. Assign in Inspector.")]
    [SerializeField] TextMeshProUGUI cardNameLabel;

    [Header("Notification")]
    [Tooltip("Bottom-left TextMeshProUGUI used for transient messages like 'Pocketed'. " +
             "Place it near the exit hint. Starts hidden.")]
    [SerializeField] TextMeshProUGUI notificationLabel;

    List<GameObject> _activeHints = new List<GameObject>();
    Interactable _currentFocus;
    bool _wasHolding, _wasDragging, _wasYarnPending;
    Coroutine _notificationCoroutine;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        GameEvents.OnGameStateChanged  += _ => Refresh();
        GameEvents.OnInteractableFocused += OnFocusChanged;
        GameEvents.OnHotbarChanged     += OnHotbarChanged;   // re-evaluate [Q] Inspect on slot change
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged  -= _ => Refresh();
        GameEvents.OnInteractableFocused -= OnFocusChanged;
        GameEvents.OnHotbarChanged     -= OnHotbarChanged;
    }

    void OnHotbarChanged(CardBehaviour[] cards, int selected) => Refresh();

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

        // ── Bottom-left universal exit hint ───────────────────────────────
        // Reuses the same exitBoardHint GO for every mode; just swaps the text.
        if (exitBoardHint != null)
        {
            switch (state)
            {
                case GameState.BoardMode:
                case GameState.VhsMode:
                    exitBoardHint.text = "[E] Exit";
                    exitBoardHint.gameObject.SetActive(true);
                    break;
                case GameState.InspectMode:
                    exitBoardHint.text = "[Q] Exit";
                    exitBoardHint.gameObject.SetActive(true);
                    break;
                default:
                    exitBoardHint.gameObject.SetActive(false);
                    break;
            }
        }

        // ── InspectMode ───────────────────────────────────────────────────
        if (state == GameState.InspectMode)
        {
            // Hint container is hidden; the bottom-left exit hint handles it.
            return;
        }

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
            AddHint("Q", "Inspect");
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

        // ── Hotbar inspect hint ───────────────────────────────────────────
        // Only show when looking at nothing — avoids the hint feeling permanently stuck.
        if (_currentFocus == null && HotbarSystem.Instance.SelectedCard != null)
            AddHint("Q", "Inspect");
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

    // ── Notifications ─────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a transient message bottom-left (e.g. "Pocketed").
    /// Stays fully visible for 1 s, then fades out over 1.5 s.
    /// Calling again while a notification is running restarts it cleanly.
    /// </summary>
    public void ShowNotification(string text)
    {
        if (notificationLabel == null)
        {
            Debug.LogWarning("[TooltipSystem] notificationLabel is not assigned — assign a TextMeshProUGUI in the Inspector.");
            return;
        }
        if (_notificationCoroutine != null) StopCoroutine(_notificationCoroutine);
        _notificationCoroutine = StartCoroutine(NotificationRoutine(text));
    }

    IEnumerator NotificationRoutine(string text)
    {
        // Show fully opaque
        notificationLabel.text = text;
        Color c = notificationLabel.color;
        notificationLabel.color = new Color(c.r, c.g, c.b, 1f);
        notificationLabel.gameObject.SetActive(true);

        // Hold for 1 second
        yield return new WaitForSeconds(1f);

        // Fade out over 1.5 seconds
        float elapsed  = 0f;
        float fadeTime = 1.5f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            c = notificationLabel.color;
            notificationLabel.color = new Color(c.r, c.g, c.b, alpha);
            yield return null;
        }

        notificationLabel.gameObject.SetActive(false);
        _notificationCoroutine = null;
    }
}