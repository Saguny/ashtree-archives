using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach to the cassette-slot child of the VHS player. Needs a collider on the Interactable layer.
///
/// The tape scene name lives on the VhsTape asset itself.
/// On insert: camera zooms to the screen while the screen simultaneously fades to
/// fadeColor over zoomDuration. Once fully opaque the tape scene loads, then fades in.
///
/// Inspector setup:
///   vhsPlayer    → the parent VhsPlayerInteractable
///   snapPoint    → empty Transform inside the slot (tape snaps here)
///   fadeColor    → colour to fade to (default black; try white for a "burnt-in" feel)
///   zoomDuration → how long the zoom + fade-out takes before the scene loads
/// </summary>
public class VhsInsertSlot : Interactable
{
    [Header("References")]
    [SerializeField] VhsPlayerInteractable vhsPlayer;
    [Tooltip("Tape snaps to this transform when inserted.")]
    [SerializeField] Transform snapPoint;

    [Header("Transition")]
    [Tooltip("Colour the screen fades to while zooming in.")]
    [SerializeField] Color fadeColor = Color.black;
    [Tooltip("How long (seconds) the zoom + fade-out lasts before the scene loads.")]
    [SerializeField] float zoomDuration = 1.2f;

    public bool IsOccupied { get; private set; }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        promptText = "Insert Tape";
        interactKey = InteractKey.LeftClick;
    }

    // ── Interactable overrides ─────────────────────────────────────────────

    public override bool IsWithinInteractDistance(Vector3 playerPos)
        => GameManager.Instance.CurrentState == GameState.VhsMode;

    public override void OnFocus()
    {
        if (IsOccupied)
            promptText = "";
        else if (PickupSystem.Instance.HeldTape != null || HotbarSystem.Instance.HasTape)
            promptText = "Insert Tape";
        else
            promptText = "No Tape";
    }

    public override void OnInteract()
    {
        if (IsOccupied) return;
        if (GameManager.Instance.CurrentState != GameState.VhsMode) return;

        // Priority: held tape first, then hotbar
        VhsTape tape = PickupSystem.Instance.HeldTape;
        if (tape != null)
        {
            PickupSystem.Instance.ForceRelease();
        }
        else if (HotbarSystem.Instance.HasTape)
        {
            tape = HotbarSystem.Instance.RemoveTape();
            tape.gameObject.SetActive(true);
        }
        else return;

        InsertTape(tape);
    }

    // ── Public API (also called by VhsTape.OnDropped) ─────────────────────

    public void InsertTape(VhsTape tape)
    {
        if (IsOccupied) return;
        IsOccupied = true;

        // Snap tape into the slot
        Transform target = snapPoint != null ? snapPoint : transform;
        tape.Rigidbody.isKinematic = true;
        tape.Rigidbody.useGravity = false;
        tape.transform.SetPositionAndRotation(target.position, target.rotation);

        vhsPlayer.OnTapeInserted();
        GameEvents.TapeInserted(tape);

        StartCoroutine(ZoomAndFade(tape));
    }

    // ── Private ────────────────────────────────────────────────────────────

    IEnumerator ZoomAndFade(VhsTape tape)
    {
        // Start camera zoom toward the screen
        CameraSystem.Instance.TransitionTo(vhsPlayer.ScreenZoomTarget);

        // Simultaneously fade to colour over the same duration.
        // yield return waits until the screen is fully opaque.
        yield return SceneSwitcher.FadeOut(fadeColor, zoomDuration);

        // Record return data now (before scene is destroyed)
        PlayerController player = FindFirstObjectByType<PlayerController>();
        TapeSessionManager.Instance.BeginTapeSession(
            SceneManager.GetActiveScene().name,
            player != null ? player.transform.position : Vector3.zero,
            player != null ? player.transform.rotation : Quaternion.identity,
            tape.tapeSceneName
        );

        // Unlock player movement for the tape scene
        GameManager.Instance.SetState(GameState.Exploration);

        // Screen is already fully opaque — load immediately then fade back in
        SceneSwitcher.LoadSceneFaded(tape.tapeSceneName);
    }
}
