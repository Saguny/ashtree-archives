using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives subtle atmospheric camera changes as the Minotaur Counter escalates.
///
/// Requires a Global Volume in the scene whose Profile contains:
///   - Vignette  (override enabled)
///   - Color Adjustments  (override enabled)
///
/// State targets are intentionally subtle — the player should feel uneasy
/// before they can name what changed.
/// </summary>
public class MinotaurCameraController : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("The Global Volume that holds Vignette and ColorAdjustments overrides.")]
    [SerializeField] Volume atmosphereVolume;

    [Header("Vignette Intensity per State")]
    [SerializeField] float vignetteState0 = 0f;
    [SerializeField] float vignetteState1 = 0.22f;   // barely noticeable at screen edges
    [SerializeField] float vignetteState2 = 0.36f;
    [SerializeField] float vignetteState3 = 0.50f;   // unlocks MINOTAUR ending — earns drama

    [Header("Saturation Shift per State  (0 = unchanged, negative = desaturate)")]
    [SerializeField] float saturationState0 = 0f;
    [SerializeField] float saturationState1 = -6f;
    [SerializeField] float saturationState2 = -18f;
    [SerializeField] float saturationState3 = -40f;

    [Header("Transition")]
    [Tooltip("Units per second for vignette intensity (0–1 range).")]
    [SerializeField] float vignetteSpeed   = 0.04f;
    [Tooltip("Saturation units per second (-100 to 100 range).")]
    [SerializeField] float saturationSpeed = 3f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    Vignette         _vignette;
    ColorAdjustments _colorAdjustments;
    float            _targetVignette;
    float            _targetSaturation;
    bool             _inTapeMode;   // true while TapeMode is active — Minotaur effects suspended

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (atmosphereVolume == null)
        {
            Debug.LogError("[MinotaurCameraController] No Volume assigned — atmospheric effects will not work.");
            enabled = false;
            return;
        }

        bool gotVignette = atmosphereVolume.profile.TryGet(out _vignette);
        bool gotColor    = atmosphereVolume.profile.TryGet(out _colorAdjustments);

        if (!gotVignette)
            Debug.LogWarning("[MinotaurCameraController] Volume profile is missing a Vignette override.");
        if (!gotColor)
            Debug.LogWarning("[MinotaurCameraController] Volume profile is missing a Color Adjustments override.");
    }

    void OnEnable()
    {
        GameEvents.OnMinotaurStateChanged += OnStateChanged;
        GameEvents.OnGameStateChanged     += OnGameStateChanged;
    }

    void OnDisable()
    {
        GameEvents.OnMinotaurStateChanged -= OnStateChanged;
        GameEvents.OnGameStateChanged     -= OnGameStateChanged;
    }

    // ── State handling ────────────────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        _inTapeMode = state == GameState.TapeMode;

        // Returning from tape — nudge MoveTowards back to the correct Minotaur targets
        // by doing nothing: Update() will naturally drift back to _targetVignette/Saturation.
        // The tape scene's own Volume handles its own look while _inTapeMode is true.
    }

    void OnStateChanged(int state)
    {
        _targetVignette    = state switch
        {
            1 => vignetteState1,
            2 => vignetteState2,
            3 => vignetteState3,
            _ => vignetteState0
        };

        _targetSaturation = state switch
        {
            1 => saturationState1,
            2 => saturationState2,
            3 => saturationState3,
            _ => saturationState0
        };
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        // Tape scenes own their own Volume setup — don't fight them.
        if (_inTapeMode) return;

        // MoveTowards gives a fixed rate of change regardless of current value,
        // so transitions feel gradual and even rather than snapping at the end.
        if (_vignette != null)
            _vignette.intensity.value = Mathf.MoveTowards(
                _vignette.intensity.value, _targetVignette, vignetteSpeed * Time.deltaTime);

        if (_colorAdjustments != null)
            _colorAdjustments.saturation.value = Mathf.MoveTowards(
                _colorAdjustments.saturation.value, _targetSaturation, saturationSpeed * Time.deltaTime);
    }
}
