using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// DontDestroyOnLoad singleton that handles all scene transitions.
///
/// SETUP
/// ─────
/// 1. Create a Canvas (Screen Space – Overlay, sort order 999) as a child of this GameObject.
/// 2. Add a full-screen black Image as the background.
/// 3. Add a TextMeshProUGUI "Loading" label anchored to the bottom-right.
/// 4. Add a UI Image for the sprite animation, anchored wherever you like.
/// 5. Slice your sprite sheet in the Sprite Editor (4 equal frames, left → right).
/// 6. Assign all references in the Inspector.
///
/// USAGE
/// ─────
///   LoadingScreenManager.Instance.LoadScene("House");
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    public static LoadingScreenManager Instance { get; private set; }

    // ── References ──────────────────────────────────────────────────────────────
    [Header("Canvas & Overlay")]
    [SerializeField] private CanvasGroup loadingCanvasGroup;

    [Header("Sprite Animation")]
    [Tooltip("The UI Image that will display the animated sprite.")]
    [SerializeField] private Image    animationImage;
    [Tooltip("All 4 sliced sprites from your sprite sheet, in order.")]
    [SerializeField] private Sprite[] frames;
    [Tooltip("Frames per second for the sprite animation.")]
    [SerializeField] private float    fps = 8f;

    [Header("Timing")]
    [SerializeField] private float fadeDuration    = 0.4f;
    [SerializeField] private float minDisplayTime  = 1.5f;

    // ── Private ─────────────────────────────────────────────────────────────────
    private Coroutine _animRoutine;
    private bool      _isLoading = false;

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Start hidden
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.alpha          = 0f;
            loadingCanvasGroup.interactable   = false;
            loadingCanvasGroup.blocksRaycasts = false;
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fade in the loading screen, load <paramref name="sceneName"/> asynchronously,
    /// wait for the minimum display time, then fade out.
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;
        StartCoroutine(LoadRoutine(sceneName));
    }

    // ── Core Routine ─────────────────────────────────────────────────────────────

    private IEnumerator LoadRoutine(string sceneName)
    {
        _isLoading = true;

        // Show canvas & start sprite animation
        SetCanvas(true);
        _animRoutine = StartCoroutine(AnimateSprites());

        // Fade audio out in parallel with the canvas fade-in
        AudioManager.Instance?.FadeOut();
        yield return StartCoroutine(Fade(0f, 1f));

        // Begin async load (don't activate yet)
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        // Track both: scene readiness and minimum display time
        float elapsed = 0f;

        while (!op.isDone)
        {
            elapsed += Time.deltaTime;

            // op.progress caps at 0.9 until allowSceneActivation = true
            bool sceneReady   = op.progress >= 0.9f;
            bool timerElapsed = elapsed >= minDisplayTime;

            if (sceneReady && timerElapsed)
            {
                op.allowSceneActivation = true;
            }

            yield return null;
        }

        // Fade audio back in in parallel with the canvas fade-out
        AudioManager.Instance?.FadeIn();
        yield return StartCoroutine(Fade(1f, 0f));

        // Clean up
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        SetCanvas(false);

        _isLoading = false;
    }

    // ── Sprite Animation ─────────────────────────────────────────────────────────

    private IEnumerator AnimateSprites()
    {
        if (frames == null || frames.Length == 0 || animationImage == null)
            yield break;

        float delay = 1f / Mathf.Max(fps, 0.1f);
        int   index = 0;

        while (true)
        {
            animationImage.sprite = frames[index];
            index = (index + 1) % frames.Length;
            yield return new WaitForSecondsRealtime(delay);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private IEnumerator Fade(float from, float to)
    {
        float elapsed = 0f;
        loadingCanvasGroup.alpha = from;

        while (elapsed < fadeDuration)
        {
            elapsed                  += Time.unscaledDeltaTime;
            loadingCanvasGroup.alpha  = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }

        loadingCanvasGroup.alpha = to;
    }

    private void SetCanvas(bool active)
    {
        loadingCanvasGroup.interactable   = active;
        loadingCanvasGroup.blocksRaycasts = active;
    }

    // ── Dev API ───────────────────────────────────────────────────────────────

    /// <summary>Fade in the loading screen without triggering a scene load. Editor / testing only.</summary>
    public void DevShow()
    {
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        SetCanvas(true);
        loadingCanvasGroup.alpha = 0f;
        _animRoutine = StartCoroutine(AnimateSprites());
        StartCoroutine(Fade(0f, 1f));
    }

    /// <summary>Fade out and hide the loading screen. Editor / testing only.</summary>
    public void DevHide()
    {
        StartCoroutine(DevHideRoutine());
    }

    private IEnumerator DevHideRoutine()
    {
        yield return StartCoroutine(Fade(1f, 0f));
        if (_animRoutine != null) { StopCoroutine(_animRoutine); _animRoutine = null; }
        SetCanvas(false);
    }
}
