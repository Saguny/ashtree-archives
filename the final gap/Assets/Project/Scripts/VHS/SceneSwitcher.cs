using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// DontDestroyOnLoad singleton for scene transitions with colour fades.
///
/// Two usage patterns:
///
///   1. Standard (fade out → load → fade in, all automatic):
///        SceneSwitcher.LoadScene("MyScene");
///
///   2. Caller-controlled (e.g. VhsInsertSlot wants to drive timing):
///        yield return SceneSwitcher.FadeOut(color, duration);  // waits until fully opaque
///        SceneSwitcher.LoadSceneFaded("MyScene");              // loads then fades back in
///
/// The fade canvas is created automatically — no manual setup needed.
/// </summary>
public class SceneSwitcher : MonoBehaviour
{
    public static SceneSwitcher Instance { get; private set; }

    [Header("Fade")]
    [Tooltip("Default duration used by LoadScene(). FadeOut() accepts its own duration.")]
    [SerializeField] float fadeDuration = 0.6f;

    CanvasGroup _fadeGroup;
    Image _fadeImage;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);
        BuildFadeCanvas();
    }

    void BuildFadeCanvas()
    {
        var canvasGO = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var imageGO = new GameObject("FadeImage");
        imageGO.transform.SetParent(canvasGO.transform, false);

        var rt = imageGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        _fadeImage = imageGO.AddComponent<Image>();
        _fadeImage.color = Color.black;

        _fadeGroup = imageGO.AddComponent<CanvasGroup>();
        _fadeGroup.alpha = 0f;
        _fadeGroup.blocksRaycasts = false;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Standard transition: fade to black → load scene → fade back in.
    /// </summary>
    public static void LoadScene(string sceneName)
    {
        if (Instance != null)
            Instance.StartCoroutine(Instance.FadeAndLoad(sceneName, Color.black, Instance.fadeDuration));
        else
            SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// Fades the screen to <paramref name="color"/> over <paramref name="duration"/> seconds.
    /// Returns a Coroutine so the caller can <c>yield return</c> it and wait until fully opaque.
    /// </summary>
    public static Coroutine FadeOut(Color color, float duration)
    {
        if (Instance == null) return null;
        Instance._fadeImage.color = color;
        Instance._fadeGroup.blocksRaycasts = true;
        return Instance.StartCoroutine(Instance.Fade(0f, 1f, duration));
    }

    /// <summary>
    /// Assumes the screen is already fully opaque (caller used FadeOut first).
    /// Loads the scene then fades back in using the default fadeDuration.
    /// </summary>
    public static void LoadSceneFaded(string sceneName)
    {
        if (Instance != null)
            Instance.StartCoroutine(Instance.LoadAndFadeIn(sceneName));
        else
            SceneManager.LoadScene(sceneName);
    }

    // ── Private ────────────────────────────────────────────────────────────

    IEnumerator FadeAndLoad(string sceneName, Color color, float duration)
    {
        _fadeImage.color = color;
        _fadeGroup.blocksRaycasts = true;

        yield return StartCoroutine(Fade(0f, 1f, duration));

        SceneManager.LoadScene(sceneName);
        yield return null; // one frame for the new scene to initialise

        yield return StartCoroutine(Fade(1f, 0f, duration));
        _fadeGroup.blocksRaycasts = false;
    }

    IEnumerator LoadAndFadeIn(string sceneName)
    {
        // Screen is already opaque — load immediately
        SceneManager.LoadScene(sceneName);
        yield return null;

        yield return StartCoroutine(Fade(1f, 0f, fadeDuration));
        _fadeGroup.blocksRaycasts = false;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        _fadeGroup.alpha = from;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _fadeGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        _fadeGroup.alpha = to;
    }

}
