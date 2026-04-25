using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DontDestroyOnLoad singleton that bridges the tape scene and the main scene.
///
/// Flow:
///   1. VhsInsertSlot calls BeginTapeSession() when the tape is inserted.
///   2. In the tape scene, TapeDirector fires GameEvents.TapeCompleted() when the story ends.
///   3. This manager loads the return scene, restores the player position,
///      positions the camera at the TV screen so it zooms back out to the TV view,
///      destroys the consumed tape, and spawns the tape's sticky note on the desk.
/// </summary>
public class TapeSessionManager : MonoBehaviour
{
    public static TapeSessionManager Instance { get; private set; }

    // -- Session data set by BeginTapeSession --------------------------------
    string _returnScene;
    Vector3 _returnPosition;
    Quaternion _returnRotation;
    string _consumedTapeSceneName;
    Vector3 _screenZoomPosition;    // world position of the TV screen zoom target
    GameObject _stickyNotePrefab;   // instantiated on the desk after the tape finishes

    bool _pendingReturn;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded    += OnSceneLoaded;
        GameEvents.OnTapeCompleted  += OnTapeCompleted;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded    -= OnSceneLoaded;
        GameEvents.OnTapeCompleted  -= OnTapeCompleted;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by VhsInsertSlot just before loading the tape scene.
    /// </summary>
    public void BeginTapeSession(
        string returnScene,
        Vector3 returnPosition,
        Quaternion returnRotation,
        string consumedTapeSceneName,
        Vector3 screenZoomPosition,
        GameObject stickyNotePrefab)
    {
        _returnScene            = returnScene;
        _returnPosition         = returnPosition;
        _returnRotation         = returnRotation;
        _consumedTapeSceneName  = consumedTapeSceneName;
        _screenZoomPosition     = screenZoomPosition;
        _stickyNotePrefab       = stickyNotePrefab;
        _pendingReturn          = false;
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    void OnTapeCompleted()
    {
        if (string.IsNullOrEmpty(_returnScene)) return;
        _pendingReturn = true;
        SceneSwitcher.LoadScene(_returnScene);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_pendingReturn) return;
        if (scene.name != _returnScene) return;

        _pendingReturn = false;
        StartCoroutine(RestoreSession());
    }

    IEnumerator RestoreSession()
    {
        // Wait one frame so all Awake/Start calls in the new scene finish first.
        yield return null;

        // -- Destroy the tape that was just watched ----------------------------
        if (!string.IsNullOrEmpty(_consumedTapeSceneName))
        {
            foreach (var tape in FindObjectsByType<VhsTape>(FindObjectsSortMode.None))
            {
                if (tape.tapeSceneName == _consumedTapeSceneName)
                {
                    Destroy(tape.gameObject);
                    break;
                }
            }
            _consumedTapeSceneName = "";
        }

        // -- Restore player world position ------------------------------------
        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player != null)
            player.transform.SetPositionAndRotation(_returnPosition, _returnRotation);

        // -- Place the camera at the TV screen position -----------------------
        // This makes the zoom-out visible: when the SceneSwitcher fade-in plays,
        // the camera is already at the screen and lerps back to the TV-front view.
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.transform.SetParent(null);
            mainCam.transform.position = _screenZoomPosition;
        }

        // -- Re-enter VhsMode and start the zoom-out lerp ---------------------
        // ReturnFromTape() calls CameraSystem.TransitionTo(cameraTarget), which
        // will lerp from the screen position we just set to the TV-front target.
        VhsPlayerInteractable vhsPlayer = FindFirstObjectByType<VhsPlayerInteractable>();
        if (vhsPlayer != null)
            vhsPlayer.ReturnFromTape();
        else
            GameManager.Instance.SetState(GameState.Exploration);

        // -- Spawn sticky note on the desk ------------------------------------
        if (_stickyNotePrefab != null && vhsPlayer != null && vhsPlayer.StickyNoteSpawnPoint != null)
        {
            Instantiate(
                _stickyNotePrefab,
                vhsPlayer.StickyNoteSpawnPoint.position,
                vhsPlayer.StickyNoteSpawnPoint.rotation
            );
        }

        _stickyNotePrefab = null;
    }
}
