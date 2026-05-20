using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays a brief "AUTO SAVE" overlay whenever the game is saved to disk.
///
/// SETUP — two options:
///
///   A) Self-contained (no Canvas needed in scene)
///      Attach to any DontDestroyOnLoad GameObject.
///      The indicator builds its own Screen-Space-Overlay canvas at runtime.
///      Configure position / text / colors in the Inspector.
///
///   B) Pre-built Canvas
///      Create a UI Image + TextMeshProUGUI in your existing HUD canvas,
///      assign them to <see cref="manualGroup"/> (their parent CanvasGroup)
///      and leave <see cref="useBuiltInCanvas"/> unchecked.
///      The component will drive only that CanvasGroup.
///
/// The indicator fades in over <see cref="fadeInDuration"/>, stays for
/// <see cref="holdDuration"/>, then fades out over <see cref="fadeOutDuration"/>.
/// </summary>
public class AutoSaveIndicator : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────
    [Header("Mode")]
    [Tooltip("When true, builds its own Canvas at runtime. Disable to supply your own.")]
    [SerializeField] bool useBuiltInCanvas = true;

    [Tooltip("Assign a CanvasGroup from your existing HUD if useBuiltInCanvas is false.")]
    [SerializeField] CanvasGroup manualGroup;

    [Header("Content")]
    [SerializeField] string labelText   = "AUTO SAVE";
    [SerializeField] Color  textColor   = new Color(1f, 1f, 1f, 1f);
    [SerializeField] Color  bgColor     = new Color(0f, 0f, 0f, 0.45f);

    [Header("Position  (built-in canvas only)")]
    [Tooltip("Anchor as a fraction of screen size, e.g. (0.92, 0.05) = bottom-right.")]
    [SerializeField] Vector2 anchorPosition = new Vector2(0.92f, 0.05f);
    [SerializeField] Vector2 panelSize      = new Vector2(180f, 40f);

    [Header("Timing (seconds)")]
    [SerializeField] float fadeInDuration  = 0.25f;
    [SerializeField] float holdDuration    = 1.6f;
    [SerializeField] float fadeOutDuration = 0.6f;

    // ── Private ────────────────────────────────────────────────────────────────
    CanvasGroup _group;
    Coroutine   _routine;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        if (useBuiltInCanvas)
            BuildCanvas();
        else
            _group = manualGroup;

        if (_group != null)
            _group.alpha = 0f;
    }

    void OnEnable()  => GameEvents.OnGameSaved += OnGameSaved;
    void OnDisable() => GameEvents.OnGameSaved -= OnGameSaved;

    // ── Event handler ──────────────────────────────────────────────────────────

    void OnGameSaved()
    {
        if (_group == null) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShowRoutine());
    }

    // ── Animation ──────────────────────────────────────────────────────────────

    IEnumerator ShowRoutine()
    {
        // Fade in
        yield return Fade(0f, 1f, fadeInDuration);

        // Hold
        yield return new WaitForSecondsRealtime(holdDuration);

        // Fade out
        yield return Fade(1f, 0f, fadeOutDuration);

        _routine = null;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        _group.alpha = from;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed      += Time.unscaledDeltaTime;
            _group.alpha  = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        _group.alpha = to;
    }

    // ── Built-in canvas construction ───────────────────────────────────────────

    void BuildCanvas()
    {
        // Canvas
        var canvasGO = new GameObject("AutoSaveCanvas");
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 998; // just below SceneSwitcher's fade canvas
        canvasGO.AddComponent<CanvasScaler>();

        // Panel (background)
        var panelGO = new GameObject("AutoSavePanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        var rt = panelGO.AddComponent<RectTransform>();
        rt.anchorMin        = anchorPosition;
        rt.anchorMax        = anchorPosition;
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = panelSize;

        var bg       = panelGO.AddComponent<Image>();
        bg.color     = bgColor;
        bg.raycastTarget = false;

        _group              = panelGO.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        // Label — try TextMeshPro first, fall back to legacy Text
        BuildLabel(panelGO, rt);
    }

    void BuildLabel(GameObject parent, RectTransform parentRT)
    {
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(parent.transform, false);

        var rt        = labelGO.AddComponent<RectTransform>();
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.sizeDelta  = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        // TextMeshPro (preferred)
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text           = labelText;
        tmp.color          = textColor;
        tmp.alignment      = TextAlignmentOptions.Center;
        tmp.fontSize       = 14f;
        tmp.fontStyle      = FontStyles.Bold;
        tmp.raycastTarget  = false;
    }
}
