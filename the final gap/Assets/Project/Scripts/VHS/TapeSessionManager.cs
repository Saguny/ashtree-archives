using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DontDestroyOnLoad singleton that bridges the tape scene and the main scene.
///
/// Flow:
///   1. VhsInsertSlot calls BeginTapeSession() when the tape is inserted.
///   2. In the tape scene, call GameEvents.TapeCompleted() when the story ends.
///   3. This manager loads the return scene, restores the player position,
///      re-enters VhsMode at the TV, and destroys the consumed tape so it
///      can't be picked up or used again.
/// </summary>
public class TapeSessionManager : MonoBehaviour
{
    public static TapeSessionManager Instance { get; private set; }

    string _returnScene;
    Vector3 _returnPosition;
    Quaternion _returnRotation;
    string _consumedTapeSceneName; // used to identify and destroy the tape on return
    bool _pendingReturn;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameEvents.OnTapeCompleted += OnTapeCompleted;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameEvents.OnTapeCompleted -= OnTapeCompleted;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void BeginTapeSession(string returnScene, Vector3 returnPosition, Quaternion returnRotation, string consumedTapeSceneName)
    {
        _returnScene = returnScene;
        _returnPosition = returnPosition;
        _returnRotation = returnRotation;
        _consumedTapeSceneName = consumedTapeSceneName;
        _pendingReturn = false;
    }

    // ── Private ────────────────────────────────────────────────────────────

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
        StartCoroutine(RestorePlayer());
    }

    IEnumerator RestorePlayer()
    {
        // Wait one frame so all Awake/Start calls in the new scene finish first
        yield return null;

        // Destroy the tape that was just watched so it can't be used again
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

        // Restore player world position
        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player != null)
            player.transform.SetPositionAndRotation(_returnPosition, _returnRotation);

        // Re-enter VhsMode so the player can press E to walk away from the TV
        VhsPlayerInteractable vhsPlayer = FindFirstObjectByType<VhsPlayerInteractable>();
        if (vhsPlayer != null)
            vhsPlayer.ReturnFromTape();
        else
            GameManager.Instance.SetState(GameState.Exploration);
    }
}
