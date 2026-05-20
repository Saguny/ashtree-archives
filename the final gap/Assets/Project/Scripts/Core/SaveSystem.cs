using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DontDestroyOnLoad singleton responsible for persisting the full game state
/// between sessions.
///
/// ── SAVE TRIGGERS ──────────────────────────────────────────────────────────
///   • Auto-save when any scene listed in <see cref="autoSaveScenes"/> finishes
///     loading. Defaults to "House" and "Office".
///   • <see cref="Save"/> can also be called explicitly (e.g. from a front-door
///     interactable before transitioning to the house, so the corkboard state is
///     captured while the office scene is still loaded).
///
/// ── LOAD FLOW ──────────────────────────────────────────────────────────────
///   1. SaveSystem.Awake  → reads JSON from disk into CurrentSaveData.
///   2. Each subsystem's Start() calls SaveSystem.Instance.ApplySaveData() or
///      reads CurrentSaveData directly.
///   3. After distributing data, SaveSystem fires GameEvents.GameLoaded so any
///      late subscribers can react.
///      BoardRestorer (office scene) listens and restores pinned cards + yarn.
///
/// ── BOARD STATE CAVEAT ─────────────────────────────────────────────────────
///   CardBehaviours live in the office scene. When the house scene is active
///   those objects don't exist, so Save() skips the board snapshot. The board
///   is always re-captured on "Office" scene load, which is the safe moment.
///
/// ── SAVE FORMAT ────────────────────────────────────────────────────────────
///   Pretty-printed JSON at Application.persistentDataPath / savegame.json.
///   Change <see cref="k_FileName"/> if you want a different name.
/// </summary>
public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    // ── Configuration ──────────────────────────────────────────────────────────
    [Header("Auto-Save Scenes")]
    [Tooltip("Scene names that trigger an auto-save when they finish loading.")]
    [SerializeField] string[] autoSaveScenes = { "House", "Office" };

    // ── Constants ──────────────────────────────────────────────────────────────
    const string k_FileName    = "savegame.json";
    const string k_SaveVersion = "1.0";

    // ── State ──────────────────────────────────────────────────────────────────
    /// <summary>The most recently loaded (or saved) data. Null until first save or load.</summary>
    public SaveData CurrentSaveData { get; private set; }

    public bool HasSaveFile => File.Exists(SavePath);
    static string SavePath   => Path.Combine(Application.persistentDataPath, k_FileName);

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);

        if (HasSaveFile)
        {
            CurrentSaveData = ReadFromDisk();
            Debug.Log($"[SaveSystem] Existing save loaded from {SavePath}");
        }
        else
        {
            Debug.Log("[SaveSystem] No save file found — starting fresh.");
        }
    }

    void OnEnable()  => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // ── Scene hook ─────────────────────────────────────────────────────────────

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsAutoSaveScene(scene.name)) return;
        StartCoroutine(AutoSaveAfterFrame(scene.name));
    }

    // Wait one frame so all Awake/Start calls (incl. BoardRestorer) complete first.
    IEnumerator AutoSaveAfterFrame(string sceneName)
    {
        yield return null;
        yield return null; // second frame — BoardRestorer may take one frame to restore
        Save();
    }

    bool IsAutoSaveScene(string sceneName)
    {
        foreach (var s in autoSaveScenes)
            if (s == sceneName) return true;
        return false;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot all systems and write to disk immediately.
    /// Safe to call from any scene — skips board capture when no CardBehaviours exist.
    /// </summary>
    public void Save()
    {
        var data           = new SaveData();
        data.saveVersion   = k_SaveVersion;
        data.saveTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        data.lastScene     = SceneManager.GetActiveScene().name;

        // Collect data from each subsystem
        EndingManager.Instance?.PopulateSaveData(data);
        MinotaurCounter.Instance?.PopulateSaveData(data);
        CharacterBindingHandler.Instance?.PopulateSaveData(data);
        RunInventorySystem.Instance?.PopulateSaveData(data);
        CaptureBoardState(data);

        // Keep a copy in memory
        CurrentSaveData = data;

        WriteToDisk(data);
        GameEvents.GameSaved();

        Debug.Log($"[SaveSystem] ✓ Auto-saved  ({data.pinnedCards.Count} pinned cards, " +
                  $"{data.watchedTapes.Count} tapes, counter={data.minotaurCounter})");
    }

    /// <summary>
    /// Apply CurrentSaveData to all subsystems and fire GameEvents.GameLoaded.
    /// Called automatically by each subsystem's Start() via CurrentSaveData,
    /// but can also be called explicitly after a manual scene load.
    /// </summary>
    public void ApplySaveData()
    {
        if (CurrentSaveData == null) return;

        EndingManager.Instance?.ApplySaveData(CurrentSaveData);
        MinotaurCounter.Instance?.ApplySaveData(CurrentSaveData);
        CharacterBindingHandler.Instance?.ApplySaveData(CurrentSaveData);
        RunInventorySystem.Instance?.ApplySaveData(CurrentSaveData);

        GameEvents.GameLoaded(CurrentSaveData);
        Debug.Log("[SaveSystem] Save data applied to all systems.");
    }

    /// <summary>
    /// Permanently delete the save file and clear in-memory data.
    /// Use from a "New Game" flow.
    /// </summary>
    public void DeleteSave()
    {
        if (File.Exists(SavePath))
            File.Delete(SavePath);

        CurrentSaveData = null;
        Debug.Log("[SaveSystem] Save file deleted.");
    }

    // ── Board state snapshot ───────────────────────────────────────────────────

    void CaptureBoardState(SaveData data)
    {
        data.pinnedCards.Clear();
        data.yarnConnections.Clear();

        // Cards — only present in the office scene
        var allCards = FindObjectsByType<CardBehaviour>(FindObjectsSortMode.None);
        if (allCards.Length == 0) return; // in house scene — skip gracefully

        foreach (var card in allCards)
        {
            if (!card.IsPinned) continue;
            var p = card.transform.position;
            var r = card.transform.rotation;
            data.pinnedCards.Add(new PinnedCardSave
            {
                cardTitle = card.cardTitle,
                posX = p.x, posY = p.y, posZ = p.z,
                rotX = r.x, rotY = r.y, rotZ = r.z, rotW = r.w,
            });
        }

        // Yarn connections
        if (YarnSystem.Instance != null)
        {
            foreach (var conn in YarnSystem.Instance.GetAllConnections())
            {
                if (conn.From?.Card == null || conn.To?.Card == null) continue;
                data.yarnConnections.Add(new YarnConnectionSave
                {
                    cardTitleA = conn.From.Card.cardTitle,
                    cardTitleB = conn.To.Card.cardTitle,
                });
            }
        }
    }

    // ── Disk I/O ───────────────────────────────────────────────────────────────

    static void WriteToDisk(SaveData data)
    {
        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SavePath, json);
    }

    static SaveData ReadFromDisk()
    {
        try
        {
            string json = File.ReadAllText(SavePath);
            return JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Failed to read save file: {e.Message}");
            return null;
        }
    }

    // ── Dev API ────────────────────────────────────────────────────────────────

    /// <summary>Force a save right now regardless of scene. Dev / test only.</summary>
    public void DevForceSave() => Save();

    /// <summary>Delete save and reload the current scene. Dev / test only.</summary>
    public void DevDeleteAndReload()
    {
        DeleteSave();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
