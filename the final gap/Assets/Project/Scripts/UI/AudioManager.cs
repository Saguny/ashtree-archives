using System.Collections;
using UnityEngine;

/// <summary>
/// DontDestroyOnLoad singleton that handles global audio fading during scene transitions.
///
/// Fades AudioListener.volume — a global 0–1 multiplier that sits entirely outside
/// the AudioMixer, so it never conflicts with your exposed mixer parameters or sliders.
///
/// USAGE
/// ─────
///   AudioManager.Instance.FadeOut();   // called automatically by LoadingScreenManager
///   AudioManager.Instance.FadeIn();    // called automatically by LoadingScreenManager
///
/// You can also call them manually for cutscenes, menus, etc.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Fade Durations")]
    [Tooltip("How long the fade-out takes when entering the loading screen.")]
    [SerializeField] private float fadeOutDuration = 0.4f;
    [Tooltip("How long the fade-in takes when the new scene is ready.")]
    [SerializeField] private float fadeInDuration  = 0.4f;

    [Header("UI Sounds")]
    [Tooltip("AudioSource used for all UI click sounds. Auto-created if left empty.")]
    [SerializeField] private AudioSource uiAudioSource;

    private Coroutine _fadeRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        AudioListener.volume = 1f;

        // Create a UI AudioSource if one wasn't assigned in the Inspector
        if (uiAudioSource == null)
        {
            uiAudioSource              = gameObject.AddComponent<AudioSource>();
            uiAudioSource.playOnAwake  = false;
            uiAudioSource.spatialBlend = 0f; // always 2D
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Fade global volume down to 0 over fadeOutDuration seconds.</summary>
    public void FadeOut() => Fade(0f, fadeOutDuration);

    /// <summary>Fade global volume back up to 1 over fadeInDuration seconds.</summary>
    public void FadeIn()  => Fade(1f, fadeInDuration);

    /// <summary>Fade to a specific target volume over a custom duration.</summary>
    public void FadeTo(float targetVolume, float duration) => Fade(targetVolume, duration);

    /// <summary>Silence immediately with no fade.</summary>
    public void MuteImmediate()  => SetImmediate(0f);

    /// <summary>Restore volume immediately with no fade.</summary>
    public void UnmuteImmediate() => SetImmediate(1f);

    /// <summary>Play a one-shot UI sound through the shared UI AudioSource.</summary>
    public void PlayUISound(AudioClip clip, float volume = 1f)
    {
        if (clip == null || uiAudioSource == null) return;
        uiAudioSource.PlayOneShot(clip, volume);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void Fade(float target, float duration)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine(target, duration));
    }

    private void SetImmediate(float value)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        AudioListener.volume = value;
    }

    private IEnumerator FadeRoutine(float target, float duration)
    {
        float start   = AudioListener.volume;
        float elapsed = 0f;

        if (duration <= 0f)
        {
            AudioListener.volume = target;
            yield break;
        }

        while (elapsed < duration)
        {
            elapsed              += Time.unscaledDeltaTime;
            AudioListener.volume  = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }

        AudioListener.volume = target;
        _fadeRoutine = null;
    }
}
