using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Place one TapeDirector in each tape scene.
///
/// The director runs a flat list of TapeActions in order. Each action can be set
/// to "runConcurrent" -- meaning the sequence immediately moves to the next action
/// without waiting for that one to finish. Use this for fire-and-forget effects
/// (start an animation while narration begins playing, etc).
///
/// Quick reference -- action types:
///
///   NARRATION / AUDIO
///     PlayNarration        -- play AudioClip + optional subtitle; blocks until clip ends
///     ShowSubtitle         -- show subtitle text (no audio); instant
///     HideSubtitle         -- hide subtitle panel; instant
///     StopNarration        -- stop narrator mid-clip; instant
///     PlaySound            -- fire-and-forget sound on an AudioSource; instant
///
///   TIMING
///     Wait                 -- pause N seconds
///
///   PLAYER CONTROL
///     LockMovement         -- freeze WASD; instant
///     UnlockMovement       -- restore WASD; instant
///     LockLook             -- freeze mouse look; instant
///     UnlockLook           -- restore mouse look; instant
///
///   CAMERA
///     LockCameraOn         -- detach camera from player, lerp and track a Transform; instant start
///     LookAtTarget         -- keep camera in place but rotate it to face a Transform (locks move+look)
///     UnlockCamera         -- return camera to player; instant
///
///   DIALOGUE
///     PlayDialogue         -- lock movement, look at speaker, play clip + subtitle; auto-restores when done
///     StopDialogue         -- cut audio mid-line and hide subtitle; call EndDialogue to fully restore
///     EndDialogue          -- stop audio, hide subtitle, return camera, unlock movement
///
///   ANIMATION
///     SetAnimatorTrigger   -- animator.SetTrigger(param); instant
///     SetAnimatorBool      -- animator.SetBool(param, value); instant
///     SetAnimatorFloat     -- animator.SetFloat(param, value); instant
///     SetAnimatorInt       -- animator.SetInteger(param, value); instant
///     WaitForAnimationState -- block until animator reaches named state (optional timeout via Duration)
///
///   PLAYER INPUT GATES
///     WaitForPlayerAt      -- block until player steps into a TapeAdvanceTrigger
///     WaitForPlayerInteract -- block until player interacts with a specific Interactable
///
///   ZONES
///     UnlockZone           -- enable a TapeBoundaryZone's walls; instant
///     LockZone             -- disable a TapeBoundaryZone's walls; instant
///
///   SCENE OBJECTS
///     SetGameObjectActive  -- enable/disable any GameObject; instant
///
///   RECORDING CUTS
///     RecordingCut         -- flash screen to a colour, hold, fade back; simulates tape cutting out
///     TeleportPlayer       -- instant positional jump; optionally wraps in a RecordingCut
///
///   CUSTOM HOOKS
///     FireUnityEvent       -- invoke a designer-defined UnityEvent; instant
///
///   END
///     EndTape              -- fire GameEvents.TapeCompleted() and stop the sequence
/// </summary>
public class TapeDirector : MonoBehaviour
{
    // =========================================================================
    // Action type
    // =========================================================================

    public enum ActionType
    {
        PlayNarration,
        ShowSubtitle,
        HideSubtitle,
        StopNarration,
        PlaySound,

        PlayDialogue,
        StopDialogue,
        EndDialogue,

        Wait,

        LockMovement,
        UnlockMovement,
        LockLook,
        UnlockLook,

        LockCameraOn,
        LookAtTarget,
        UnlockCamera,

        SetAnimatorTrigger,
        SetAnimatorBool,
        SetAnimatorFloat,
        SetAnimatorInt,
        WaitForAnimationState,

        WaitForPlayerAt,
        WaitForPlayerInteract,

        UnlockZone,
        LockZone,

        SetGameObjectActive,

        RecordingCut,
        TeleportPlayer,

        FireUnityEvent,

        EndTape,
    }

    // =========================================================================
    // TapeAction
    // =========================================================================

    [Serializable]
    public class TapeAction
    {
        [Tooltip("What this action does.")]
        public ActionType type;

        [Tooltip("If true the sequence moves to the next action immediately without waiting for this one.")]
        public bool runConcurrent;

        [Header("Narration / Audio")]
        public AudioClip narrationClip;

        [TextArea(2, 5)]
        public string subtitle;

        public AudioClip soundClip;
        public AudioSource soundSource;

        [Header("Dialogue")]
        public AudioSource dialogueSource;
        public AudioClip dialogueClip;
        public Transform dialogueCameraTarget;

        [Header("Timing")]
        [Min(0f)] public float duration;

        [Header("Camera")]
        public Transform cameraTarget;

        [Header("Animation")]
        public Animator animator;
        public string animatorParam;
        public bool animatorBoolValue;
        public float animatorFloatValue;
        public int animatorIntValue;
        public int animatorLayer;

        [Header("Player Input Gates")]
        public TapeAdvanceTrigger advanceTrigger;
        public Interactable interactable;

        [Header("Zones")]
        public TapeBoundaryZone zone;

        [Header("Scene Objects")]
        public GameObject targetObject;
        public bool activeState;

        [Header("Recording Cut / Teleport")]
        public Color cutColor;
        [Min(0f)] public float cutFadeSpeed;
        public Transform teleportTarget;
        public bool teleportWithCut;
        public TapeBoundaryZone[] teleportZonesToActivate;
        public TapeBoundaryZone[] teleportZonesToDeactivate;

        [Header("Custom Hook")]
        public UnityEvent unityEvent;
    }

    // =========================================================================
    // Inspector fields
    // =========================================================================

    [Header("References")]
    [SerializeField] AudioSource narratorSource;
    [SerializeField] TapeSubtitleUI subtitleUI;

    [Header("VHS Cut Effect")]
    [Tooltip("Sound played at the moment of every cut-out (RecordingCut / TeleportPlayer with cut). " +
             "Assign a short tape-glitch SFX here.")]
    [SerializeField] AudioClip cutOutSFX;

    [Header("Starting Zones")]
    [SerializeField] TapeBoundaryZone[] startingZones;

    [Header("Sequence")]
    [SerializeField] TapeAction[] sequence;

    // =========================================================================
    // Runtime state
    // =========================================================================

    PlayerController _player;
    AudioSource _currentDialogueSource;

    int _currentSequenceIndex = -1;
    /// <summary>The index of the action currently executing. -1 before the sequence starts.</summary>
    public int CurrentActionIndex => _currentSequenceIndex;

    CanvasGroup _cutOverlay;
    Image _cutImage;

    // Dedicated source for the cut SFX -- ignores AudioListener.volume so it
    // still fires even though we silence everything else during the cut.
    AudioSource _cutSFXSource;
    float _preCutListenerVolume = 1f;

    // =========================================================================
    // Lifecycle
    // =========================================================================

    void Awake()
    {
        _player = FindFirstObjectByType<PlayerController>();

        if (_player == null)
            Debug.LogError("[TapeDirector] No PlayerController found in scene.");

        BuildCutOverlay();
    }

    void BuildCutOverlay()
    {
        var go = new GameObject("[TapeDirector] CutOverlay");
        go.transform.SetParent(transform, false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 998;
        go.AddComponent<CanvasScaler>();

        var imgGO = new GameObject("Image");
        imgGO.transform.SetParent(go.transform, false);

        var rt = imgGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        _cutImage = imgGO.AddComponent<Image>();
        _cutImage.color = Color.black;
        _cutOverlay = imgGO.AddComponent<CanvasGroup>();
        _cutOverlay.alpha = 0f;
        _cutOverlay.blocksRaycasts = false;

        // Dedicated AudioSource for cut SFX -- bypasses listener volume.
        _cutSFXSource = go.AddComponent<AudioSource>();
        _cutSFXSource.playOnAwake = false;
        _cutSFXSource.ignoreListenerVolume = true;
        _cutSFXSource.ignoreListenerPause  = true;
    }

    void Start()
    {
        foreach (var z in FindObjectsByType<TapeBoundaryZone>(FindObjectsSortMode.None))
            z.SetActive(false);

        if (startingZones != null)
            foreach (var z in startingZones)
                z?.SetActive(true);

        if (sequence != null && sequence.Length > 0)
            StartCoroutine(RunSequence());
        else
        {
            Debug.LogWarning("[TapeDirector] Sequence is empty -- completing tape immediately.");
            GameEvents.TapeCompleted();
        }
    }

    // =========================================================================
    // Sequence runner
    // =========================================================================

    IEnumerator RunSequence()
    {
        yield return StartCoroutine(RunSequenceFrom(0));
    }

    IEnumerator RunSequenceFrom(int startIndex)
    {
        for (_currentSequenceIndex = startIndex; _currentSequenceIndex < sequence.Length; _currentSequenceIndex++)
        {
            var action = sequence[_currentSequenceIndex];
            if (action.runConcurrent)
                StartCoroutine(ExecuteAction(action));
            else
                yield return StartCoroutine(ExecuteAction(action));
        }
    }

    // =========================================================================
    // Action execution
    // =========================================================================

    IEnumerator ExecuteAction(TapeAction a)
    {
        switch (a.type)
        {
            case ActionType.PlayNarration:
            {
                if (a.narrationClip != null && narratorSource != null)
                {
                    narratorSource.clip = a.narrationClip;
                    narratorSource.Play();
                }
                if (!string.IsNullOrWhiteSpace(a.subtitle))
                    subtitleUI?.Show(a.subtitle);

                float clipLen = a.narrationClip != null ? a.narrationClip.length : 0f;
                yield return new WaitForSeconds(clipLen);

                if (!string.IsNullOrWhiteSpace(a.subtitle))
                    subtitleUI?.Hide();
                break;
            }

            case ActionType.ShowSubtitle:
                subtitleUI?.Show(a.subtitle);
                break;

            case ActionType.HideSubtitle:
                subtitleUI?.Hide();
                break;

            case ActionType.StopNarration:
                narratorSource?.Stop();
                subtitleUI?.Hide();
                break;

            case ActionType.PlaySound:
                if (a.soundClip != null)
                {
                    if (a.soundSource != null)
                        a.soundSource.PlayOneShot(a.soundClip);
                    else
                        AudioSource.PlayClipAtPoint(a.soundClip, Camera.main.transform.position);
                }
                break;

            case ActionType.PlayDialogue:
            {
                if (a.dialogueSource == null)
                {
                    Debug.LogWarning("[TapeDirector] PlayDialogue: no dialogueSource assigned.");
                    break;
                }

                _player?.LockMovement(true);
                _player?.LockLook(true);

                if (a.dialogueCameraTarget != null)
                    CameraSystem.Instance.TransitionTo(a.dialogueCameraTarget);
                else
                    CameraSystem.Instance.LookAt(a.dialogueSource.transform);

                _currentDialogueSource = a.dialogueSource;
                AudioClip clip = a.dialogueClip != null ? a.dialogueClip : a.dialogueSource.clip;
                float lineLength = 0f;
                if (clip != null)
                {
                    _currentDialogueSource.clip = clip;
                    _currentDialogueSource.Play();
                    lineLength = clip.length;
                }

                if (!string.IsNullOrWhiteSpace(a.subtitle))
                    subtitleUI?.Show(a.subtitle);

                yield return new WaitForSeconds(lineLength);

                subtitleUI?.Hide();
                _player?.LockMovement(false);
                _player?.LockLook(false);
                CameraSystem.Instance.ReturnToPlayer();
                _currentDialogueSource = null;
                break;
            }

            case ActionType.StopDialogue:
                _currentDialogueSource?.Stop();
                subtitleUI?.Hide();
                _currentDialogueSource = null;
                break;

            case ActionType.EndDialogue:
                _currentDialogueSource?.Stop();
                subtitleUI?.Hide();
                _player?.LockMovement(false);
                _player?.LockLook(false);
                CameraSystem.Instance.ReturnToPlayer();
                _currentDialogueSource = null;
                break;

            case ActionType.Wait:
                yield return new WaitForSeconds(a.duration);
                break;

            case ActionType.LockMovement:
                _player?.LockMovement(true);
                break;

            case ActionType.UnlockMovement:
                _player?.LockMovement(false);
                break;

            case ActionType.LockLook:
                _player?.LockLook(true);
                break;

            case ActionType.UnlockLook:
                _player?.LockLook(false);
                break;

            case ActionType.LockCameraOn:
                if (a.cameraTarget != null)
                    CameraSystem.Instance.TransitionTo(a.cameraTarget);
                else
                    Debug.LogWarning("[TapeDirector] LockCameraOn: no cameraTarget assigned.");
                break;

            case ActionType.LookAtTarget:
                if (a.cameraTarget != null)
                {
                    _player?.LockMovement(true);
                    _player?.LockLook(true);
                    CameraSystem.Instance.LookAt(a.cameraTarget);
                }
                else
                    Debug.LogWarning("[TapeDirector] LookAtTarget: no cameraTarget assigned.");
                break;

            case ActionType.UnlockCamera:
                CameraSystem.Instance.ReturnToPlayer();
                break;

            case ActionType.SetAnimatorTrigger:
                if (a.animator != null) a.animator.SetTrigger(a.animatorParam);
                break;

            case ActionType.SetAnimatorBool:
                if (a.animator != null) a.animator.SetBool(a.animatorParam, a.animatorBoolValue);
                break;

            case ActionType.SetAnimatorFloat:
                if (a.animator != null) a.animator.SetFloat(a.animatorParam, a.animatorFloatValue);
                break;

            case ActionType.SetAnimatorInt:
                if (a.animator != null) a.animator.SetInteger(a.animatorParam, a.animatorIntValue);
                break;

            case ActionType.WaitForAnimationState:
            {
                if (a.animator == null || string.IsNullOrEmpty(a.animatorParam)) break;
                float timeout = a.duration > 0f ? a.duration : float.MaxValue;
                float elapsed = 0f;
                yield return new WaitUntil(() =>
                {
                    elapsed += Time.deltaTime;
                    return elapsed >= timeout
                        || a.animator.GetCurrentAnimatorStateInfo(a.animatorLayer).IsName(a.animatorParam);
                });
                break;
            }

            case ActionType.WaitForPlayerAt:
                if (a.advanceTrigger != null)
                {
                    a.advanceTrigger.Arm();
                    yield return new WaitUntil(() => a.advanceTrigger.HasBeenTriggered);
                }
                else
                    Debug.LogWarning("[TapeDirector] WaitForPlayerAt: no advanceTrigger assigned.");
                break;

            case ActionType.WaitForPlayerInteract:
            {
                if (a.interactable == null)
                {
                    Debug.LogWarning("[TapeDirector] WaitForPlayerInteract: no interactable assigned.");
                    break;
                }
                bool triggered = false;
                Interactable capturedTarget = a.interactable;
                Action<Interactable> handler = hit => { if (hit == capturedTarget) triggered = true; };
                GameEvents.OnInteractableTriggered += handler;
                yield return new WaitUntil(() => triggered);
                GameEvents.OnInteractableTriggered -= handler;
                break;
            }

            case ActionType.UnlockZone:
                if (a.zone != null) a.zone.SetActive(true);
                break;

            case ActionType.LockZone:
                if (a.zone != null) a.zone.SetActive(false);
                break;

            case ActionType.SetGameObjectActive:
                if (a.targetObject != null) a.targetObject.SetActive(a.activeState);
                break;

            case ActionType.RecordingCut:
            {
                Color col = a.cutColor; col.a = 1f;
                float spd = a.cutFadeSpeed > 0f ? a.cutFadeSpeed : 0.15f;
                yield return StartCoroutine(VhsCutOut(col, spd));
                if (a.duration > 0f)
                    yield return new WaitForSeconds(a.duration);
                yield return StartCoroutine(VhsCutIn(col, spd));
                break;
            }

            case ActionType.TeleportPlayer:
            {
                if (a.teleportTarget == null)
                {
                    Debug.LogWarning("[TapeDirector] TeleportPlayer: no teleportTarget assigned.");
                    break;
                }

                float spd = a.cutFadeSpeed > 0f ? a.cutFadeSpeed : 0.15f;
                Color col = a.cutColor; col.a = 1f;

                if (a.teleportWithCut)
                    yield return StartCoroutine(VhsCutOut(col, spd));

                _player?.TeleportTo(a.teleportTarget.position, a.teleportTarget.rotation);

                if (a.teleportZonesToDeactivate != null)
                    foreach (var z in a.teleportZonesToDeactivate) z?.SetActive(false);
                if (a.teleportZonesToActivate != null)
                    foreach (var z in a.teleportZonesToActivate) z?.SetActive(true);

                if (a.teleportWithCut)
                {
                    if (a.duration > 0f)
                        yield return new WaitForSeconds(a.duration);
                    yield return StartCoroutine(VhsCutIn(col, spd));
                }
                break;
            }

            case ActionType.FireUnityEvent:
                a.unityEvent?.Invoke();
                break;

            case ActionType.EndTape:
                StopAllCoroutines();
                subtitleUI?.Hide();
                GameEvents.TapeCompleted();
                yield break;
        }
    }

    // =========================================================================
    // VHS cut helpers
    // =========================================================================

    /// <summary>
    /// Plays the cut SFX, silences all other audio, runs a VHS glitch flash,
    /// then fades to solid <paramref name="holdColor"/>.
    /// AudioListener.volume is left at 0 -- call VhsCutIn to restore it.
    /// </summary>
    IEnumerator VhsCutOut(Color holdColor, float fadeSpeed)
    {
        holdColor.a = 1f;
        _cutOverlay.blocksRaycasts = true;

        // Fire the cut SFX before silencing (the source ignores listener volume anyway).
        if (cutOutSFX != null)
            _cutSFXSource.PlayOneShot(cutOutSFX);

        // Silence everything except our dedicated SFX source.
        // Store the current volume so VhsCutIn can restore it exactly.
        _preCutListenerVolume = AudioListener.volume;
        AudioListener.volume = 0f;

        // -- Glitch flash sequence (pure UI, no shader required) --

        // Frame 1: hard white flash
        _cutImage.color = Color.white;
        _cutOverlay.alpha = 1f;
        yield return null;

        // Frame 2: chromatic smear (cyan-ish offset feel)
        _cutImage.color = new Color(0.2f, 1f, 0.9f, 1f);
        _cutOverlay.alpha = 0.85f;
        yield return null;

        // Frame 3: brief drop
        _cutOverlay.alpha = 0.4f;
        yield return null;

        // Frame 4-5: settle to hold colour
        _cutImage.color = holdColor;
        _cutOverlay.alpha = 0.8f;
        yield return null;

        yield return StartCoroutine(OverlayFade(holdColor, _cutOverlay.alpha, 1f, fadeSpeed * 0.5f));
    }

    /// <summary>
    /// Fades the overlay back out from solid and restores AudioListener.volume.
    /// </summary>
    IEnumerator VhsCutIn(Color holdColor, float fadeSpeed)
    {
        holdColor.a = 1f;
        yield return StartCoroutine(OverlayFade(holdColor, 1f, 0f, fadeSpeed));
        AudioListener.volume = _preCutListenerVolume;
    }

    /// <summary>
    /// Raw alpha lerp on the overlay -- used internally by VhsCutOut/In.
    /// Does NOT touch AudioListener.
    /// </summary>
    IEnumerator OverlayFade(Color color, float from, float to, float duration)
    {
        _cutImage.color = color;
        _cutOverlay.alpha = from;
        _cutOverlay.blocksRaycasts = to > 0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _cutOverlay.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        _cutOverlay.alpha = to;
        _cutOverlay.blocksRaycasts = to > 0.5f;
    }

    // =========================================================================
    // Dev / Debug API  (used by HouseOfLeavesDevTool — safe to call at runtime)
    // =========================================================================

    /// <summary>
    /// Immediately stops the tape sequence, resets all locks and overlays,
    /// and fires TapeCompleted so the game can continue normally.
    /// </summary>
    public void DevForceEnd()
    {
        StopAllCoroutines();
        ResetState();
        GameEvents.TapeCompleted();
    }

    /// <summary>
    /// Skips the currently executing action and continues from the next one.
    /// </summary>
    public void DevSkipCurrentAction()
    {
        int next = _currentSequenceIndex + 1;
        if (sequence == null || next >= sequence.Length)
        {
            DevForceEnd();
            return;
        }
        DevJumpToAction(next);
    }

    /// <summary>
    /// Stops the current sequence, resets state, then restarts from the given action index.
    /// Clamps the index to valid range automatically.
    /// </summary>
    public void DevJumpToAction(int index)
    {
        if (sequence == null || sequence.Length == 0) { DevForceEnd(); return; }

        StopAllCoroutines();
        ResetState();

        int clamped = Mathf.Clamp(index, 0, sequence.Length - 1);
        StartCoroutine(RunSequenceFrom(clamped));
    }

    /// <summary>
    /// Resets player locks, camera, overlays, and audio to a clean state.
    /// Called before every dev jump so you never get stuck.
    /// </summary>
    void ResetState()
    {
        // Hide subtitle and stop narration
        subtitleUI?.Hide();
        narratorSource?.Stop();
        _currentDialogueSource?.Stop();
        _currentDialogueSource = null;

        // Clear cut overlay
        if (_cutOverlay != null)
        {
            _cutOverlay.alpha = 0f;
            _cutOverlay.blocksRaycasts = false;
        }

        // Restore audio listener volume in case a cut left it at 0
        AudioListener.volume = _preCutListenerVolume > 0f ? _preCutListenerVolume : 1f;

        // Restore player controls and camera
        _player?.LockMovement(false);
        _player?.LockLook(false);
        CameraSystem.Instance?.ReturnToPlayer();
    }
}
