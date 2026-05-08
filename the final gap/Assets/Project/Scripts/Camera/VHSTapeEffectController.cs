using UnityEngine;

/// <summary>
/// Enables and disables the VHS tape effect by driving the _VHSIntensity global
/// shader property, which the VHSTapeEffects shader reads.
///
/// When _VHSIntensity = 0 the shader early-outs and outputs the original image —
/// no visual or performance cost when outside tape mode.
///
/// The MINOTAUR tape has a distortion multiplier since the design doc specifies
/// "extremely distorted" recording for that specific tape.
/// </summary>
public class VHSTapeEffectController : MonoBehaviour
{
    static readonly int VHSIntensityID  = Shader.PropertyToID("_VHSIntensity");
    static readonly int VHSScanlineYID  = Shader.PropertyToID("_VHSScanlineY");
    static readonly int VHSScanlineXID  = Shader.PropertyToID("_VHSScanlineX");
    static readonly int PSXPixelsXID    = Shader.PropertyToID("_PSXPixelsX");
    static readonly int PSXPixelsYID    = Shader.PropertyToID("_PSXPixelsY");

    [Header("Fade")]
    [Tooltip("How quickly the VHS effect fades in/out when entering or leaving tape mode.")]
    [SerializeField] float fadeSpeed = 3f;

    [Header("PSX Resolution (restored when leaving tape mode)")]
    [SerializeField] float psxPixelsX = 320f;
    [SerializeField] float psxPixelsY = 240f;

    [Header("Tape Distortion Multiplier")]
    [Tooltip("Normal tape playback intensity (0–1).")]
    [SerializeField] float normalTapeIntensity = 1f;

    [Tooltip("Intensity for the MINOTAUR tape — design doc specifies extreme distortion.")]
    [SerializeField] float minotaurTapeIntensity = 1f;  // same max visually; distortion comes from shader params

    [Header("Scanline Animation")]
    [SerializeField] float scanlineYSpeed = 0.28f;
    [SerializeField] float scanlineXSpeed = 0.11f;
    [Tooltip("Chance per frame of a random scanline jump — simulates VHS head seek.")]
    [SerializeField] float jumpChance = 0.004f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    float _currentIntensity;
    float _targetIntensity;
    bool  _inTapeMode;
    float _scanlineY;
    float _scanlineX;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        // Start fully off
        _currentIntensity = 0f;
        _targetIntensity  = 0f;
        Shader.SetGlobalFloat(VHSIntensityID, 0f);
    }

    void OnEnable()
    {
        GameEvents.OnGameStateChanged += OnGameStateChanged;
        GameEvents.OnTapeInserted     += OnTapeInserted;
        GameEvents.OnTapeCompleted    += OnTapeCompleted;
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= OnGameStateChanged;
        GameEvents.OnTapeInserted     -= OnTapeInserted;
        GameEvents.OnTapeCompleted    -= OnTapeCompleted;

        // Always clean up the global property when this component goes away
        Shader.SetGlobalFloat(VHSIntensityID, 0f);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        _inTapeMode      = state == GameState.TapeMode;
        _targetIntensity = _inTapeMode ? normalTapeIntensity : 0f;

        // Disable PSX pixelation during tape mode — analog video and pixel grid
        // don't coexist. Restore it when returning to exploration.
        if (_inTapeMode)
        {
            Shader.SetGlobalFloat(PSXPixelsXID, 99999f);
            Shader.SetGlobalFloat(PSXPixelsYID, 99999f);
        }
        else
        {
            Shader.SetGlobalFloat(PSXPixelsXID, psxPixelsX);
            Shader.SetGlobalFloat(PSXPixelsYID, psxPixelsY);
        }
    }

    void OnTapeInserted(VhsTape tape)
    {
        // If the MINOTAUR tape is inserted, flag the elevated distortion target.
        // Actual extra distortion (more noise, more glitching) can be added here
        // by also setting shader properties like _NoiseIntensity via Material refs
        // once we've had a chance to configure those materials.
        bool isMinotaurTape = tape != null && tape.name.ToUpper().Contains("MINOTAUR");
        if (_inTapeMode)
            _targetIntensity = isMinotaurTape ? minotaurTapeIntensity : normalTapeIntensity;
    }

    void OnTapeCompleted()
    {
        _targetIntensity = 0f;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        _currentIntensity = Mathf.MoveTowards(_currentIntensity, _targetIntensity, fadeSpeed * Time.deltaTime);
        Shader.SetGlobalFloat(VHSIntensityID, _currentIntensity);

        if (!_inTapeMode) return;

        // Animate scanline band positions — continuous scroll with occasional jumps.
        // _VHSScanlineY is the main distortion band; _VHSScanlineX is the secondary
        // "frozen line" that snaps nearby rows to its Y position.
        _scanlineY = (_scanlineY + scanlineYSpeed * Time.deltaTime) % 1f;
        _scanlineX = (_scanlineX + scanlineXSpeed * Time.deltaTime) % 1f;

        // Random head-seek jump — snaps the band to a new position
        if (Random.value < jumpChance)
            _scanlineY = Random.value;

        Shader.SetGlobalFloat(VHSScanlineYID, _scanlineY);
        Shader.SetGlobalFloat(VHSScanlineXID, _scanlineX);
    }
}
