using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the VHS player GameObject (must be on the Interactable layer).
/// Interaction is purely raycast-driven — no proximity aura.
/// Point crosshair at the device and press E, exactly like any other interactable.
///
///   First E press  → VhsMode, camera lerps to cameraTarget (front of TV)
///   E any time in VhsMode → exit back to Exploration (if tape not yet inserted)
///
/// Inspector setup:
///   cameraTarget      → empty Transform placed in front of the TV screen
///   screenZoomTarget  → empty Transform close to the TV screen surface (zoom target)
///   deviceLabel       → text shown in the [E] tooltip, e.g. "Shiny EV-DT 1"
/// </summary>
public class VhsPlayerInteractable : Interactable
{
    [Header("Display")]
    [Tooltip("Shown in the tooltip as '[E] <deviceLabel>'")]
    [SerializeField] string deviceLabel = "Shiny EV-DT 1";

    [Header("Camera Targets")]
    [Tooltip("Camera lerps here when entering VhsMode.")]
    [SerializeField] Transform cameraTarget;
    [Tooltip("Camera lerps here after the tape is inserted (screen zoom).")]
    [SerializeField] Transform screenZoomTarget;

    [Header("Desk")]
    [Tooltip("Sticky notes from completed tapes are spawned here.")]
    [SerializeField] Transform stickyNoteSpawnPoint;

    bool _tapeInserted;
    bool _exitedThisFrame;       // prevents same-frame re-entry via InteractionSystem
    GameState _stateLastFrame;   // used to ignore E presses on the frame we enter VhsMode

    public Transform ScreenZoomTarget     => screenZoomTarget;
    public Transform StickyNoteSpawnPoint => stickyNoteSpawnPoint;

    // ── Interactable overrides ─────────────────────────────────────────────

    public override void OnFocus()
    {
        // Only show the label in Exploration. In VhsMode the bottom-left
        // canvas hint already covers Exit — no need for a second one.
        promptText = GameManager.Instance.CurrentState == GameState.Exploration
            ? deviceLabel
            : "";
    }

    public override void OnLoseFocus() => promptText = "";

    public override void OnInteract()
    {
        // If Update() already handled E this frame, skip — otherwise
        // InteractionSystem would call EnterVhsMode() on the same frame we exited.
        if (_exitedThisFrame) return;

        GameState state = GameManager.Instance.CurrentState;

        if (state == GameState.VhsMode && !_tapeInserted)
        {
            ExitVhsMode();
            return;
        }

        if (state != GameState.Exploration) return;

        EnterVhsMode();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Called by VhsInsertSlot once the tape has been accepted.</summary>
    public void OnTapeInserted() => _tapeInserted = true;

    /// <summary>
    /// Called by TapeSessionManager after returning from the tape scene.
    /// Re-enters VhsMode so the player can press E to walk away.
    /// </summary>
    public void ReturnFromTape()
    {
        _tapeInserted = false;
        GameManager.Instance.SetState(GameState.VhsMode);
        CameraSystem.Instance.TransitionTo(cameraTarget);
    }

    // ── Private ────────────────────────────────────────────────────────────

    void EnterVhsMode()
    {
        // Clear prompt and force a tooltip refresh before the state changes,
        // otherwise the [E] label lingers until the cursor moves away.
        promptText = "";
        GameEvents.FocusInteractable(null);

        GameManager.Instance.SetState(GameState.VhsMode);
        CameraSystem.Instance.TransitionTo(cameraTarget);
    }

    void ExitVhsMode()
    {
        // CameraSystem listens to OnGameStateChanged → Exploration → ReturnToPlayer()
        GameManager.Instance.SetState(GameState.Exploration);
    }

    void Update()
    {
        // Reset the guard every frame
        _exitedThisFrame = false;

        GameState currentState = GameManager.Instance.CurrentState;

        // Direct E-key listener — needed because in VhsMode the cursor is often
        // hovering the insert slot, so the raycast won't land on this object.
        // Guard: only exit if we were ALREADY in VhsMode last frame.
        // This prevents the race where InteractionSystem enters VhsMode and
        // this Update() immediately exits it on the very same frame.
        if (_stateLastFrame == GameState.VhsMode
            && currentState == GameState.VhsMode
            && !_tapeInserted
            && Keyboard.current.eKey.wasPressedThisFrame)
        {
            _exitedThisFrame = true;
            ExitVhsMode();
        }

        _stateLastFrame = currentState;
    }
}
