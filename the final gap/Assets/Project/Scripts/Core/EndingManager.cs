using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that owns all ending prerequisite tracking and fires
/// GameEvents.EndingTriggered when a trigger condition is satisfied.
///
/// Three endings (per design doc):
///
///   THIS IS NOT FOR YOU
///     Prerequisites  : all 9 tapes watched  +  Minotaur State 1 or 2
///     Trigger        : every card that was ever pinned is now trashed
///                      AND the board has zero connections remaining
///
///   MINOTAUR
///     Prerequisites  : Minotaur State 3
///     Trigger        : call TryTriggerOnHouseEntry() from the front-door interaction
///
///   THE INFINITE DESCENT  (two possible paths, either satisfies)
///     Path A — Will Navidson in Living Room +
///               Indeterminacy and Missing both yarn-connected to Will
///     Path B — Karen + Kids + Will all on board with specific connections +
///               Concession and Indeterminacy both connected to Will +
///               Concession NOT connected to Living Room
///     Trigger: call TryTriggerOnHouseEntry() from the front-door interaction
///
/// HOW TO HOOK UP THE HOUSE ENTRY TRIGGER
///   On the script / interactable that loads the House scene, call:
///       EndingManager.Instance.TryTriggerOnHouseEntry();
///   If it returns true, suppress the normal scene load — the ending takes over.
///
/// CARD TITLE CONVENTIONS (must match CardBehaviour.cardTitle exactly in the Inspector):
///   Character cards : "Will Navidson"  |  "Karen Green"  |  "Chad and Daisy Navidson"
///   Manipulation    : "Indeterminacy"  |  "Missing"      |  "Concession"
///   House           : "Living Room"
/// </summary>
public class EndingManager : MonoBehaviour
{
    public static EndingManager Instance { get; private set; }

    // ── All 9 tape scene names (must match VhsTape.tapeSceneName in the Inspector) ──
    static readonly HashSet<string> k_AllTapeScenes = new HashSet<string>
    {
        "Tape_Measurements",
        "Tape_HalfMinuteHallway",
        "Tape_Exploration",
        "Tape_DaisyChad",
        "Tape_Expedition",
        "Tape_Uncanny",
        "Tape_Photography",
        "Tape_Minotaur",
        "Tape_Goodbye"
    };

    // ── Runtime tracking ──────────────────────────────────────────────────────
    // Note: everPinnedCards and trashedCards are keyed by cardTitle (string)
    // so they survive card destruction and serialise cleanly to SaveData.
    readonly HashSet<string> _watchedTapes    = new HashSet<string>();
    readonly HashSet<string> _everPinnedCards = new HashSet<string>();
    readonly HashSet<string> _trashedCards    = new HashSet<string>();

    bool _endingTriggered;

    // ── Public read-only state (used by the dev tool) ─────────────────────────

    public int  WatchedTapeCount  => _watchedTapes.Count;
    public int  TotalTapeCount    => k_AllTapeScenes.Count;
    public bool AllTapesWatched   => _watchedTapes.IsSupersetOf(k_AllTapeScenes);

    /// <summary>How many distinct card titles have ever been pinned to the board.</summary>
    public int EverPinnedCount    => _everPinnedCards.Count;

    /// <summary>How many of those cards have been trashed so far.</summary>
    public int TrashedCount       => _trashedCards.Count;

    /// <summary>
    /// True when every card that was ever pinned has since been trashed
    /// (and at least one card was pinned in the first place).
    /// </summary>
    public bool AllEverPinnedTrashed =>
        _everPinnedCards.Count > 0 && _everPinnedCards.IsSubsetOf(_trashedCards);

    public bool EndingAlreadyTriggered => _endingTriggered;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);
    }

    void OnEnable()
    {
        GameEvents.OnTapeWatched  += HandleTapeWatched;
        GameEvents.OnCardPinned   += HandleCardPinned;
        GameEvents.OnCardTrashed  += HandleCardTrashed;
    }

    void OnDisable()
    {
        GameEvents.OnTapeWatched  -= HandleTapeWatched;
        GameEvents.OnCardPinned   -= HandleCardPinned;
        GameEvents.OnCardTrashed  -= HandleCardTrashed;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    void HandleTapeWatched(string sceneName)
    {
        _watchedTapes.Add(sceneName);
        Debug.Log($"[EndingManager] Tape watched: {sceneName}  ({_watchedTapes.Count}/{k_AllTapeScenes.Count})");
    }

    void HandleCardPinned(CardBehaviour card)
    {
        _everPinnedCards.Add(card.cardTitle);
    }

    void HandleCardTrashed(CardBehaviour card)
    {
        _trashedCards.Add(card.cardTitle);
        TryTriggerThisIsNotForYou();
    }

    // =========================================================================
    // THIS IS NOT FOR YOU
    // =========================================================================

    /// <summary>Prerequisites are satisfied (tapes + minotaur state), but the
    /// trigger hasn't fired yet — used by the devtool display.</summary>
    public bool IsThisIsNotForYouAvailable()
    {
        if (!AllTapesWatched) return false;
        var mc = MinotaurCounter.Instance;
        return mc != null && mc.CurrentState >= 1 && mc.CurrentState <= 2;
    }

    /// <summary>True when the board has no pinned cards and no yarn connections.</summary>
    public bool IsBoardFullyCleared()
    {
        // Check all CardBehaviours in scene
        foreach (var c in Object.FindObjectsByType<CardBehaviour>(FindObjectsSortMode.None))
            if (c.IsPinned) return false;

        // Check yarn connections
        if (YarnSystem.Instance != null && YarnSystem.Instance.ConnectionCount > 0)
            return false;

        return true;
    }

    void TryTriggerThisIsNotForYou()
    {
        if (_endingTriggered) return;
        if (!IsThisIsNotForYouAvailable()) return;
        if (!AllEverPinnedTrashed) return;
        if (!IsBoardFullyCleared()) return;

        TriggerEnding(EndingType.ThisIsNotForYou);
    }

    // =========================================================================
    // MINOTAUR
    // =========================================================================

    public bool IsMinotaurAvailable()
    {
        var mc = MinotaurCounter.Instance;
        return mc != null && mc.CurrentState >= 3;
    }

    // =========================================================================
    // THE INFINITE DESCENT
    // =========================================================================

    public bool IsInfiniteDescentAvailable() =>
        IsInfiniteDescentPathAMet() || IsInfiniteDescentPathBMet();

    /// <summary>
    /// Path A:
    ///   - "Indeterminacy" and "Missing" both on the board and yarn-connected to "Will Navidson"
    ///   - Will Navidson is bound to the Living Room
    /// </summary>
    public bool IsInfiniteDescentPathAMet()
    {
        var cbh  = CharacterBindingHandler.Instance;
        var yarn = YarnSystem.Instance;
        if (cbh == null || yarn == null) return false;

        CardBehaviour will          = FindPinnedCard("Will Navidson");
        CardBehaviour indeterminacy = FindPinnedCard("Indeterminacy");
        CardBehaviour missing       = FindPinnedCard("Missing");
        if (will == null || indeterminacy == null || missing == null) return false;

        // Will must be bound to Living Room
        RoomConfig willRoom = cbh.GetRoom(will);
        if (willRoom == null || willRoom.roomName != "Living Room") return false;

        // Both manipulation clues must be yarn-connected to Will
        if (!yarn.AreConnected(will, indeterminacy)) return false;
        if (!yarn.AreConnected(will, missing)) return false;

        return true;
    }

    /// <summary>
    /// Path B:
    ///   - Karen, Kids, Will all on board
    ///   - Karen connected to Kids and to Will
    ///   - Will connected to Concession and Indeterminacy
    ///   - Concession NOT connected to Living Room
    ///   (Entrance Door clue is COMING SOON — skipped for now)
    /// </summary>
    public bool IsInfiniteDescentPathBMet()
    {
        var yarn = YarnSystem.Instance;
        if (yarn == null) return false;

        CardBehaviour will          = FindPinnedCard("Will Navidson");
        CardBehaviour karen         = FindPinnedCard("Karen Green");
        CardBehaviour kids          = FindPinnedCard("Chad and Daisy Navidson");
        CardBehaviour concession    = FindPinnedCard("Concession");
        CardBehaviour indeterminacy = FindPinnedCard("Indeterminacy");

        // All required cards must be on the board
        if (will == null || karen == null || kids == null
            || concession == null || indeterminacy == null) return false;

        // Required connections
        if (!yarn.AreConnected(karen, kids))          return false; // Karen ↔ Kids
        if (!yarn.AreConnected(karen, will))          return false; // Karen ↔ Will
        if (!yarn.AreConnected(will, concession))     return false; // Will ↔ Concession
        if (!yarn.AreConnected(will, indeterminacy))  return false; // Will ↔ Indeterminacy

        // Concession must NOT be connected to Living Room
        CardBehaviour livingRoom = FindPinnedCard("Living Room");
        if (livingRoom != null && yarn.AreConnected(concession, livingRoom)) return false;

        return true;
    }

    // =========================================================================
    // House Entry Trigger  (call this from the front-door interactable)
    // =========================================================================

    /// <summary>
    /// Call from the front-door interaction (or wherever the house scene is loaded).
    /// Checks Infinite Descent first (higher specificity), then Minotaur.
    /// Returns true if an ending was triggered — in that case, suppress the normal
    /// scene load and let the ending take over.
    /// </summary>
    public bool TryTriggerOnHouseEntry()
    {
        if (_endingTriggered) return false;

        // Finalize the Minotaur counter before any ending check.
        // This is the only moment the volume tax (floor(connections / N)) is applied —
        // based on what's actually committed to the board right now, not connection history.
        MinotaurCounter.Instance?.FinalizeForDeparture();

        if (IsInfiniteDescentAvailable())
        {
            TriggerEnding(EndingType.TheInfiniteDescent);
            return true;
        }

        if (IsMinotaurAvailable())
        {
            TriggerEnding(EndingType.Minotaur);
            return true;
        }

        return false;
    }

    // =========================================================================
    // Shared trigger
    // =========================================================================

    void TriggerEnding(EndingType ending)
    {
        if (_endingTriggered) return;
        _endingTriggered = true;
        Debug.Log($"[EndingManager] ★ Ending triggered: {ending}");
        GameEvents.EndingTriggered(ending);
    }

    // =========================================================================
    // Save / Load
    // =========================================================================

    /// <summary>Write EndingManager state into a SaveData snapshot.</summary>
    public void PopulateSaveData(SaveData data)
    {
        data.watchedTapes.Clear();
        foreach (var t in _watchedTapes) data.watchedTapes.Add(t);

        data.everPinnedCardTitles.Clear();
        foreach (var t in _everPinnedCards) data.everPinnedCardTitles.Add(t);

        data.trashedCardTitles.Clear();
        foreach (var t in _trashedCards) data.trashedCardTitles.Add(t);
    }

    /// <summary>Restore EndingManager state from a SaveData snapshot.</summary>
    public void ApplySaveData(SaveData data)
    {
        foreach (var t in data.watchedTapes)        _watchedTapes.Add(t);
        foreach (var t in data.everPinnedCardTitles) _everPinnedCards.Add(t);
        foreach (var t in data.trashedCardTitles)    _trashedCards.Add(t);

        Debug.Log($"[EndingManager] Loaded — {_watchedTapes.Count} tapes, " +
                  $"{_everPinnedCards.Count} ever-pinned, {_trashedCards.Count} trashed.");
    }

    // =========================================================================
    // Dev / Editor API
    // =========================================================================

    /// <summary>Force-trigger any ending immediately, bypassing all prerequisites.</summary>
    public void DevTriggerEnding(EndingType ending)
    {
        _endingTriggered = false; // allow re-trigger for testing
        TriggerEnding(ending);
    }

    /// <summary>Mark all 9 tapes as watched without actually playing them.</summary>
    public void DevMarkAllTapesWatched()
    {
        foreach (var s in k_AllTapeScenes)
            _watchedTapes.Add(s);
        Debug.Log("[EndingManager] DEV — all tapes marked as watched.");
    }

    /// <summary>Resets the triggered flag so a second ending can be tested in the same session.</summary>
    public void DevResetEndingState()
    {
        _endingTriggered = false;
        Debug.Log("[EndingManager] DEV — ending state reset.");
    }

    /// <summary>Clears all tracked state (tapes, pinned cards, trashed cards).</summary>
    public void DevFullReset()
    {
        _endingTriggered = false;
        _watchedTapes.Clear();
        _everPinnedCards.Clear();
        _trashedCards.Clear();
        Debug.Log("[EndingManager] DEV — full reset.");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Returns the first currently-pinned CardBehaviour whose cardTitle matches,
    /// or null if not found / not on the board.</summary>
    static CardBehaviour FindPinnedCard(string title)
    {
        foreach (var c in Object.FindObjectsByType<CardBehaviour>(FindObjectsSortMode.None))
            if (c.IsPinned && c.cardTitle == title) return c;
        return null;
    }
}
