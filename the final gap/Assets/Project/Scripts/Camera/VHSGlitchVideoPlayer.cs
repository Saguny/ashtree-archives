using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Plays a looping VHS glitch video into a RenderTexture and feeds it to the
/// VHSTapeEffects shader as _GlitchTex. Starts/stops with TapeMode.
///
/// Setup:
///   1. Import your glitch mp4 into the Unity project (drag into Project window).
///   2. Assign it to the Glitch Clip field in the Inspector.
///   3. Add this component to the same DDOL GameObject as VHSTapeEffectController.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VHSGlitchVideoPlayer : MonoBehaviour
{
    static readonly int GlitchTexID = Shader.PropertyToID("_GlitchTex");

    [Header("Video")]
    [Tooltip("The imported VHS glitch mp4 VideoClip asset.")]
    [SerializeField] VideoClip glitchClip;

    [Header("Render Texture")]
    [Tooltip("Match your glitch video's actual resolution.")]
    [SerializeField] int rtWidth  = 1920;
    [SerializeField] int rtHeight = 1080;

    // ── Runtime ───────────────────────────────────────────────────────────────

    VideoPlayer   _player;
    RenderTexture _rt;
    bool          _wantPlay; // play as soon as prepare completes

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _rt          = new RenderTexture(rtWidth, rtHeight, 0, RenderTextureFormat.ARGB32);
        _rt.name     = "VHSGlitchRT";
        _rt.wrapMode = TextureWrapMode.Repeat;
        _rt.Create();

        _player                 = GetComponent<VideoPlayer>();
        _player.clip            = glitchClip;
        _player.renderMode      = VideoRenderMode.RenderTexture;
        _player.targetTexture   = _rt;
        _player.isLooping       = true;
        _player.playOnAwake     = false;
        _player.audioOutputMode = VideoAudioOutputMode.None;
        _player.skipOnDrop      = true;

        // Fires when Prepare() finishes — safe to call Play() here
        _player.prepareCompleted += OnPrepareCompleted;

        Shader.SetGlobalTexture(GlitchTexID, _rt);

        // Prepare immediately so the video is ready the moment TapeMode starts
        if (glitchClip != null)
            _player.Prepare();
    }

    void OnEnable()
    {
        GameEvents.OnGameStateChanged += OnGameStateChanged;

        // If we're already in TapeMode when this component enables, start playing
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.TapeMode)
            StartPlayback();
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= OnGameStateChanged;
        _player?.Stop();
    }

    void OnDestroy()
    {
        if (_player != null)
            _player.prepareCompleted -= OnPrepareCompleted;

        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
        }
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    void StartPlayback()
    {
        if (glitchClip == null)
        {
            Debug.LogWarning("[VHSGlitchVideoPlayer] No glitch clip assigned.");
            return;
        }

        if (_player.isPrepared)
            _player.Play();
        else
        {
            // Not ready yet — set flag so OnPrepareCompleted plays it
            _wantPlay = true;
            _player.Prepare();
        }
    }

    void OnPrepareCompleted(VideoPlayer vp)
    {
        if (_wantPlay)
        {
            _wantPlay = false;
            vp.Play();
        }
    }

    // ── State handling ────────────────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        if (state == GameState.TapeMode)
            StartPlayback();
        else
        {
            _wantPlay = false;
            _player.Stop();
        }
    }
}
