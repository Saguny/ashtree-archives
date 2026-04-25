using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Minimal subtitle display for tape narration.
/// Attach to a Canvas GameObject in the tape scene.
///
/// Setup:
///   1. Create a Canvas (Screen Space - Overlay) in the tape scene.
///   2. Add a TextMeshProUGUI child — anchor it to the bottom-center.
///   3. Add a CanvasGroup component to the same GameObject as this script,
///      or let the script create one automatically.
///   4. Assign subtitleText and optionally a CanvasGroup in the Inspector.
///
/// The TapeDirector calls Show(text) and Hide() — you don't need to drive it manually.
/// If you want a fade effect, set fadeDuration > 0.
/// </summary>
public class TapeSubtitleUI : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI subtitleText;

    [Tooltip("CanvasGroup used for fading. One will be added automatically if not assigned.")]
    [SerializeField] CanvasGroup canvasGroup;

    [Tooltip("How long (seconds) the fade-in / fade-out takes. Set to 0 for instant.")]
    [SerializeField] float fadeDuration = 0.25f;

    Coroutine _fadeCoroutine;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = 0f;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Show a subtitle line. Pass an empty string to show the panel without text.</summary>
    public void Show(string text)
    {
        if (subtitleText != null)
            subtitleText.text = text;

        gameObject.SetActive(true);
        FadeTo(1f);
    }

    /// <summary>Fade out and hide the subtitle panel.</summary>
    public void Hide()
    {
        FadeTo(0f, hideAfterFade: true);
    }

    // ── Private ────────────────────────────────────────────────────────────

    void FadeTo(float target, bool hideAfterFade = false)
    {
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);

        if (fadeDuration <= 0f)
        {
            canvasGroup.alpha = target;
            if (hideAfterFade) gameObject.SetActive(false);
        }
        else
        {
            _fadeCoroutine = StartCoroutine(FadeRoutine(target, hideAfterFade));
        }
    }

    IEnumerator FadeRoutine(float target, bool hideAfterFade)
    {
        float start = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = target;
        if (hideAfterFade && target == 0f) gameObject.SetActive(false);
    }
}
