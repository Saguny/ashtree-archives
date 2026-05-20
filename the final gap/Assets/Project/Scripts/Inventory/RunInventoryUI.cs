using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedurally-built, self-contained inventory panel UI.
///
/// LAYOUT (right half of screen, slides in from off-screen right)
/// ┌──────────────────────────────┐
/// │  INVENTORY            [Tab]  │  ← header
/// ├──────────────────────────────┤
/// │  ▌ TAPE ─────────────────── │  ← tape slot (tall)
/// │    (empty)                   │
/// ├──────────────────────────────┤
/// │  ▌ CLUE 1 ─────────────────  │  ← clue slot 0
/// │  ▌ CLUE 2 ─────────────────  │  ← clue slot 1
/// │  ▌ CLUE 3 ─────────────────  │  ← clue slot 2
/// └──────────────────────────────┘
///
/// RIGHT-CLICK on any filled slot → context menu appears with:
///   • Take out   — drops the item on the ground, closes inventory
///   • Examine    — opens InspectSystem for that item, re-pockets on exit
///
/// HOTBAR: HotbarUI is found by tag "HotbarUI" and hidden while the panel is open.
/// If you don't use that tag, assign the hotbarRoot reference in the Inspector instead.
///
/// HOW TO INSTALL
///   Add this component to a persistent GameObject in your scene (e.g. the UI manager).
///   No prefab or canvas setup needed — all UI is created at runtime.
/// </summary>
public class RunInventoryUI : MonoBehaviour
{
    // ── Inspector (all optional — UI is built in code) ────────────────────────

    [Header("Panel Dimensions (normalised screen fractions)")]
    [SerializeField] [Range(0.3f, 0.7f)] float panelWidthFraction  = 0.45f;
    [SerializeField] [Range(0.5f, 1.0f)] float panelHeightFraction = 0.85f;

    [Header("Colors")]
    [SerializeField] Color panelBg          = new Color(0.06f, 0.06f, 0.08f, 0.93f);
    [SerializeField] Color slotEmpty        = new Color(0.12f, 0.12f, 0.15f, 0.85f);
    [SerializeField] Color slotFilled       = new Color(0.18f, 0.28f, 0.38f, 0.90f);
    [SerializeField] Color slotHovered      = new Color(0.25f, 0.38f, 0.50f, 0.95f);
    [SerializeField] Color tapeSlotEmpty    = new Color(0.14f, 0.10f, 0.08f, 0.85f);
    [SerializeField] Color tapeSlotFilled   = new Color(0.35f, 0.18f, 0.10f, 0.90f);
    [SerializeField] Color accentBar        = new Color(0.55f, 0.40f, 0.25f, 1.00f);
    [SerializeField] Color headerBg         = new Color(0.03f, 0.03f, 0.05f, 0.98f);
    [SerializeField] Color contextBg        = new Color(0.08f, 0.08f, 0.10f, 0.97f);

    [Header("Animation")]
    [SerializeField] float slideSpeed = 8f;

    [Header("Hotbar (optional — assign to auto-hide)")]
    [Tooltip("Root GameObject of the HotbarUI. If null, 'RunInventoryUI' searches by tag 'HotbarUI'.")]
    [SerializeField] GameObject hotbarRoot;

    // ── Runtime ───────────────────────────────────────────────────────────────

    // Panel
    Canvas          _canvas;
    RectTransform   _panel;
    float           _panelWidth;
    float           _offX;          // hidden x (off-screen right)
    float           _onX;           // visible x (0 = flush to right edge)

    // Slot data
    const int  k_ClueCount = 3;
    SlotWidget _tapeSlot;
    SlotWidget[] _clueSlots = new SlotWidget[k_ClueCount];

    // Context menu
    GameObject _contextMenu;
    int        _contextSlotIndex;  // -1 = tape
    bool       _contextMenuVisible;

    // Tracking
    int  _hoveredSlot       = -2;  // -2 = none, -1 = tape, 0-2 = clue
    bool _rightClickHandled;

    // Hotbar root (auto-discovered if not set)
    GameObject _hotbarRootCached;

    // ── Slot helper ───────────────────────────────────────────────────────────

    struct SlotWidget
    {
        public RectTransform Rect;
        public Image         Bg;
        public Image         Accent;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI ItemName;
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        BuildCanvas();
        BuildPanel();
        HideInstant();
    }

    void Start()
    {
        // Discover hotbar
        _hotbarRootCached = hotbarRoot;
        if (_hotbarRootCached == null)
        {
            var found = GameObject.FindWithTag("HotbarUI");
            if (found != null) _hotbarRootCached = found;
        }
    }

    void OnEnable()
    {
        GameEvents.OnInventoryToggled += OnInventoryToggled;
        GameEvents.OnHotbarChanged    += OnHotbarChanged;
    }

    void OnDisable()
    {
        GameEvents.OnInventoryToggled -= OnInventoryToggled;
        GameEvents.OnHotbarChanged    -= OnHotbarChanged;
    }

    void Update()
    {
        if (!_panel.gameObject.activeSelf) return;

        UpdateHover();
        UpdateContextMenuInput();
    }

    // ── Panel construction ────────────────────────────────────────────────────

    void BuildCanvas()
    {
        var go = new GameObject("RunInventoryCanvas");
        go.transform.SetParent(transform, false);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 90;

        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
    }

    void BuildPanel()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        _panelWidth = sw * panelWidthFraction;
        float panelH = sh * panelHeightFraction;
        _offX = _panelWidth;       // starts off-screen right
        _onX  = 0f;

        // Panel root
        var panelGo = new GameObject("InventoryPanel");
        panelGo.transform.SetParent(_canvas.transform, false);

        _panel = panelGo.AddComponent<RectTransform>();
        // Anchor to right edge
        _panel.anchorMin        = new Vector2(1f, 0.5f);
        _panel.anchorMax        = new Vector2(1f, 0.5f);
        _panel.pivot            = new Vector2(1f, 0.5f);
        _panel.sizeDelta        = new Vector2(_panelWidth, panelH);
        _panel.anchoredPosition = new Vector2(_offX, 0f);

        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = panelBg;

        // ── Header ────────────────────────────────────────────────────────────
        float headerH = 50f;
        var header    = MakeRect("Header", _panel, 0f, panelH * 0.5f - headerH * 0.5f,
                                 _panelWidth, headerH, headerBg);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot     = new Vector2(0.5f, 1f);
        header.anchoredPosition = Vector2.zero;
        header.sizeDelta        = new Vector2(0f, headerH);

        var headerTxt = MakeText("HeaderLabel", header, "INVENTORY",
                                 20, TextAlignmentOptions.Center,
                                 new Color(0.85f, 0.75f, 0.55f, 1f));
        headerTxt.anchorMin = Vector2.zero;
        headerTxt.anchorMax = Vector2.one;
        headerTxt.offsetMin = Vector2.zero;
        headerTxt.offsetMax = Vector2.zero;

        var hintTxt = MakeText("HintLabel", header, "[Tab] close",
                                13, TextAlignmentOptions.Right,
                                new Color(0.55f, 0.55f, 0.55f, 0.85f));
        hintTxt.anchorMin = new Vector2(0f, 0f);
        hintTxt.anchorMax = new Vector2(1f, 1f);
        hintTxt.offsetMin = new Vector2(0f, 0f);
        hintTxt.offsetMax = new Vector2(-12f, 0f);

        // ── Content area (below header) ────────────────────────────────────────
        float contentTop  = panelH - headerH - 12f;
        float padding     = 10f;
        float tapeSlotH   = 80f;
        float clueSlotH   = 68f;
        float gap         = 8f;

        // TAPE slot
        float tapeY = contentTop - tapeSlotH * 0.5f - padding;
        _tapeSlot = BuildSlot("TapeSlot", _panel, tapeY - panelH * 0.5f, tapeSlotH,
                              "TAPE", tapeSlotEmpty, accentBar);

        // Clue slots
        float clueTop = tapeY - tapeSlotH * 0.5f - gap;
        for (int i = 0; i < k_ClueCount; i++)
        {
            float slotY = clueTop - clueSlotH * 0.5f - i * (clueSlotH + gap);
            _clueSlots[i] = BuildSlot($"ClueSlot{i}", _panel, slotY - panelH * 0.5f, clueSlotH,
                                      $"CLUE {i + 1}", slotEmpty,
                                      new Color(0.35f, 0.45f, 0.55f, 1f));
        }

        // ── Context menu (hidden by default) ──────────────────────────────────
        BuildContextMenu();
    }

    SlotWidget BuildSlot(string name, RectTransform parent, float anchoredY, float height,
                         string label, Color bgColor, Color accentColor)
    {
        float margin   = 14f;
        float accentW  = 5f;

        var slotGo = new GameObject(name);
        slotGo.transform.SetParent(parent, false);

        var rt = slotGo.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(1f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, anchoredY);
        rt.sizeDelta        = new Vector2(-margin * 2f, height);

        var bg = slotGo.AddComponent<Image>();
        bg.color = bgColor;

        // Left accent bar
        var accent = MakeRect("Accent", rt, -(rt.sizeDelta.x * 0.5f) + accentW * 0.5f, 0f,
                              accentW, height, accentColor);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot     = new Vector2(0f, 0.5f);
        accent.anchoredPosition = Vector2.zero;
        accent.sizeDelta        = new Vector2(accentW, 0f);
        accent.GetComponent<Image>().color = accentColor;

        // Label (top-left inside slot)
        var labelTxt = MakeText("Label", rt, label, 11,
                                TextAlignmentOptions.TopLeft,
                                new Color(0.65f, 0.65f, 0.65f, 1f));
        labelTxt.anchorMin        = new Vector2(0f, 0.5f);
        labelTxt.anchorMax        = new Vector2(1f, 1f);
        labelTxt.offsetMin        = new Vector2(accentW + 8f, 0f);
        labelTxt.offsetMax        = new Vector2(-8f, -4f);

        // Item name (center of slot)
        var nameTxt = MakeText("ItemName", rt, "—", 15,
                               TextAlignmentOptions.Left,
                               new Color(0.88f, 0.88f, 0.88f, 1f));
        nameTxt.anchorMin  = new Vector2(0f, 0f);
        nameTxt.anchorMax  = new Vector2(1f, 0.5f);
        nameTxt.offsetMin  = new Vector2(accentW + 8f, 4f);
        nameTxt.offsetMax  = new Vector2(-8f, 0f);

        return new SlotWidget
        {
            Rect     = rt,
            Bg       = bg,
            Accent   = accent.GetComponent<Image>(),
            Label    = labelTxt.GetComponent<TextMeshProUGUI>(),
            ItemName = nameTxt.GetComponent<TextMeshProUGUI>()
        };
    }

    void BuildContextMenu()
    {
        _contextMenu = new GameObject("ContextMenu");
        _contextMenu.transform.SetParent(_canvas.transform, false);

        var rt = _contextMenu.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160f, 80f);
        rt.pivot     = new Vector2(0f, 1f);

        var bg = _contextMenu.AddComponent<Image>();
        bg.color = contextBg;

        MakeContextButton("TakeOut",  _contextMenu.transform, 0f,    "Take out",  OnContextTakeOut);
        MakeContextButton("Examine",  _contextMenu.transform, -40f,  "Examine",   OnContextExamine);

        _contextMenu.SetActive(false);
    }

    void MakeContextButton(string name, Transform parent, float localY, string text,
                           UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, localY);
        rt.sizeDelta        = new Vector2(0f, 40f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.14f, 0.95f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var colors = btn.colors;
        colors.normalColor      = new Color(0.12f, 0.12f, 0.14f, 0.95f);
        colors.highlightedColor = new Color(0.22f, 0.28f, 0.36f, 1.00f);
        colors.pressedColor     = new Color(0.30f, 0.38f, 0.48f, 1.00f);
        btn.colors = colors;

        var lbl = MakeText(name + "Label", rt, text, 14,
                           TextAlignmentOptions.Center,
                           new Color(0.85f, 0.85f, 0.85f, 1f));
        lbl.anchorMin = Vector2.zero;
        lbl.anchorMax = Vector2.one;
        lbl.offsetMin = new Vector2(8f, 0f);
        lbl.offsetMax = new Vector2(-8f, 0f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    RectTransform MakeRect(string name, RectTransform parent, float x, float y,
                           float w, float h, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta        = new Vector2(w, h);
        return rt;
    }

    RectTransform MakeText(string name, RectTransform parent, string text,
                           int size, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text                   = text;
        tmp.fontSize               = size;
        tmp.alignment              = align;
        tmp.color                  = color;
        tmp.overflowMode           = TextOverflowModes.Ellipsis;
        tmp.textWrappingMode       = TextWrappingModes.NoWrap;
        return rt;
    }

    // ── Show / Hide ───────────────────────────────────────────────────────────

    void OnInventoryToggled(bool isOpen)
    {
        HideContextMenu();

        if (_hotbarRootCached != null)
            _hotbarRootCached.SetActive(!isOpen);

        if (isOpen) SlideIn(); else SlideOut();
    }

    void HideInstant()
    {
        _panel.gameObject.SetActive(false);
        _panel.anchoredPosition = new Vector2(_offX, 0f);
    }

    void SlideIn()
    {
        _panel.gameObject.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(SlideRoutine(_onX));
    }

    void SlideOut()
    {
        StopAllCoroutines();
        StartCoroutine(SlideRoutine(_offX, deactivateAfter: true));
    }

    IEnumerator SlideRoutine(float targetX, bool deactivateAfter = false)
    {
        while (Mathf.Abs(_panel.anchoredPosition.x - targetX) > 0.5f)
        {
            float newX = Mathf.Lerp(_panel.anchoredPosition.x, targetX,
                                    slideSpeed * Time.unscaledDeltaTime);
            _panel.anchoredPosition = new Vector2(newX, _panel.anchoredPosition.y);
            yield return null;
        }
        _panel.anchoredPosition = new Vector2(targetX, _panel.anchoredPosition.y);
        if (deactivateAfter) _panel.gameObject.SetActive(false);
    }

    // ── Slot refresh ──────────────────────────────────────────────────────────

    void OnHotbarChanged(CardBehaviour[] slots, int selected)
    {
        // Refresh clue slots from hotbar
        for (int i = 0; i < k_ClueCount; i++)
        {
            bool filled = i < slots.Length && slots[i] != null;
            UpdateSlotVisual(ref _clueSlots[i], filled,
                             filled ? slots[i].cardTitle : "—",
                             slotEmpty, slotFilled, i == _hoveredSlot);
        }

        // Refresh tape slot
        bool tapeIn = HotbarSystem.Instance != null && HotbarSystem.Instance.HasTape;
        string tapeName = tapeIn
            ? (HotbarSystem.Instance.StoredTape != null
               ? HotbarSystem.Instance.StoredTape.name : "Tape")
            : "—";
        UpdateSlotVisual(ref _tapeSlot, tapeIn, tapeName,
                         tapeSlotEmpty, tapeSlotFilled, _hoveredSlot == -1);
    }

    void UpdateSlotVisual(ref SlotWidget slot, bool filled, string displayName,
                          Color emptyColor, Color filledColor, bool hovered)
    {
        slot.Bg.color       = hovered ? slotHovered : (filled ? filledColor : emptyColor);
        slot.ItemName.text  = displayName;
        slot.ItemName.color = filled
            ? new Color(0.90f, 0.90f, 0.88f, 1f)
            : new Color(0.40f, 0.40f, 0.42f, 1f);
    }

    // ── Hover + context menu ──────────────────────────────────────────────────

    void UpdateHover()
    {
        int prev = _hoveredSlot;
        _hoveredSlot = GetHoveredSlotIndex();
        if (_hoveredSlot != prev)
            RefreshHoverVisuals();
    }

    int GetHoveredSlotIndex()
    {
        Vector2 mp = Input.mousePosition;

        if (IsMouseOverRect(_tapeSlot.Rect, mp)) return -1;
        for (int i = 0; i < k_ClueCount; i++)
            if (IsMouseOverRect(_clueSlots[i].Rect, mp)) return i;

        return -2;
    }

    bool IsMouseOverRect(RectTransform rt, Vector2 screenPoint)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);
    }

    void RefreshHoverVisuals()
    {
        // Re-sync colors with updated hover state
        if (HotbarSystem.Instance == null) return;
        var slots = new CardBehaviour[HotbarSystem.Instance.MaxSlots];
        for (int i = 0; i < slots.Length; i++) slots[i] = HotbarSystem.Instance.GetSlot(i);
        OnHotbarChanged(slots, HotbarSystem.Instance.SelectedIndex);
    }

    void UpdateContextMenuInput()
    {
        if (Input.GetMouseButtonDown(1))  // right click
        {
            if (_hoveredSlot >= -1)   // -1 = tape, 0-2 = clue, -2 = none
            {
                bool filled = _hoveredSlot == -1
                    ? (HotbarSystem.Instance != null && HotbarSystem.Instance.HasTape)
                    : (HotbarSystem.Instance != null && HotbarSystem.Instance.GetSlot(_hoveredSlot) != null);

                if (filled)
                {
                    _contextSlotIndex = _hoveredSlot;
                    ShowContextMenu(Input.mousePosition);
                    return;
                }
            }
            // Click outside a filled slot → hide context menu
            HideContextMenu();
        }

        // Any left-click dismisses the context menu if it's not on a button
        if (Input.GetMouseButtonDown(0) && _contextMenuVisible)
            HideContextMenu();
    }

    void ShowContextMenu(Vector2 screenPos)
    {
        _contextMenuVisible = true;
        _contextMenu.SetActive(true);

        var rt = _contextMenu.GetComponent<RectTransform>();
        // Convert screen position to canvas local position
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(), screenPos, null, out Vector2 localPos);
        rt.anchoredPosition = localPos;
    }

    void HideContextMenu()
    {
        _contextMenuVisible = false;
        if (_contextMenu != null) _contextMenu.SetActive(false);
    }

    // ── Context button callbacks ──────────────────────────────────────────────

    void OnContextTakeOut()
    {
        HideContextMenu();
        var sys = RunInventorySystem.Instance;
        if (sys == null) return;

        if (_contextSlotIndex == -1)
            sys.TakeOutTape();
        else
            sys.TakeOutSlot(_contextSlotIndex);
    }

    void OnContextExamine()
    {
        HideContextMenu();
        var sys = RunInventorySystem.Instance;
        if (sys == null) return;

        if (_contextSlotIndex == -1)
            sys.ExamineTape();
        else
            sys.ExamineSlot(_contextSlotIndex);
    }
}
