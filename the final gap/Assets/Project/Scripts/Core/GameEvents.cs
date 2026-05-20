using System;
using UnityEngine;

public static class GameEvents
{
    public static event Action<GameState> OnGameStateChanged;
    public static void ChangeGameState(GameState newState) => OnGameStateChanged?.Invoke(newState);

    public static event Action<Interactable> OnInteractableFocused;
    public static void FocusInteractable(Interactable target) => OnInteractableFocused?.Invoke(target);

    public static event Action<Interactable> OnInteractableTriggered;
    public static void TriggerInteractable(Interactable target) => OnInteractableTriggered?.Invoke(target);
    public static event Action<CardBehaviour> OnCardPinned;
    public static void CardPinned(CardBehaviour card) => OnCardPinned?.Invoke(card);

    public static event Action<CardBehaviour> OnCardUnpinned;
    public static void CardUnpinned(CardBehaviour card) => OnCardUnpinned?.Invoke(card);
    public static event Action<CardBehaviour[], int> OnHotbarChanged;
    public static void HotbarChanged(CardBehaviour[] slots, int selected)
        => OnHotbarChanged?.Invoke(slots, selected);

    public static event Action<CardBehaviour, CardBehaviour> OnYarnConnected;
    public static void YarnConnected(CardBehaviour a, CardBehaviour b)
        => OnYarnConnected?.Invoke(a, b);

    // Tag connection events
    /// <summary>
    /// Fired immediately after every yarn connection is evaluated.
    /// The TagConnectionResult carries polarity, effect type, and counter delta.
    /// Subscribe here to drive house-manipulation effects.
    /// </summary>
    public static event Action<TagConnectionResult, CardBehaviour, CardBehaviour> OnTagConnectionResolved;
    public static void TagConnectionResolved(TagConnectionResult result, CardBehaviour a, CardBehaviour b)
        => OnTagConnectionResolved?.Invoke(result, a, b);

    // Minotaur Counter events
    /// <summary>Fires whenever the Minotaur Counter value changes.</summary>
    public static event Action<int> OnMinotaurCounterChanged;
    public static void MinotaurCounterChanged(int count) => OnMinotaurCounterChanged?.Invoke(count);

    /// <summary>Fires when the counter crosses a new state threshold (0 → 1 → 2 → 3).</summary>
    public static event Action<int> OnMinotaurStateChanged;
    public static void MinotaurStateChanged(int state) => OnMinotaurStateChanged?.Invoke(state);

    // Character binding events
    /// <summary>Fires when a character's bound room changes, either via HouseBindsCharacter or CharacterSwapsWithCharacter.</summary>
    public static event Action<CardBehaviour, RoomConfig> OnCharacterBindingChanged;
    public static void CharacterBindingChanged(CardBehaviour character, RoomConfig newRoom)
        => OnCharacterBindingChanged?.Invoke(character, newRoom);

    // VHS / Tape events
    public static event Action<VhsTape> OnTapeInserted;
    public static void TapeInserted(VhsTape tape) => OnTapeInserted?.Invoke(tape);

    // Fire this from your tape scene when the narrated story is complete
    public static event Action OnTapeCompleted;
    public static void TapeCompleted() => OnTapeCompleted?.Invoke();

    // Fires when the item inventory changes (tape stored / removed)
    public static event Action<VhsTape> OnItemInventoryChanged;
    public static void ItemInventoryChanged(VhsTape tape) => OnItemInventoryChanged?.Invoke(tape);

    // ── Ending events ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fired by TapeSessionManager when a tape session completes.
    /// Carries the tape's scene name so EndingManager can tick off the watched set.
    /// </summary>
    public static event Action<string> OnTapeWatched;
    public static void TapeWatched(string tapeSceneName) => OnTapeWatched?.Invoke(tapeSceneName);

    /// <summary>
    /// Fired by TrashBin when a card is physically disposed of.
    /// EndingManager uses this to track whether the whole board has been cleared.
    /// </summary>
    public static event Action<CardBehaviour> OnCardTrashed;
    public static void CardTrashed(CardBehaviour card) => OnCardTrashed?.Invoke(card);

    /// <summary>
    /// Fired by EndingManager the moment an ending's trigger condition is satisfied.
    /// Subscribe here to start cutscenes, camera animations, fade-outs, etc.
    /// </summary>
    public static event Action<EndingType> OnEndingTriggered;
    public static void EndingTriggered(EndingType ending) => OnEndingTriggered?.Invoke(ending);

    // ── Save / Load events ────────────────────────────────────────────────────

    /// <summary>Fired by SaveSystem immediately after a save file is written to disk.</summary>
    public static event Action OnGameSaved;
    public static void GameSaved() => OnGameSaved?.Invoke();

    /// <summary>
    /// Fired by SaveSystem after it finishes loading save data and distributing it
    /// to all subsystems. Subscribe here if you need to react to a restored game state.
    /// </summary>
    public static event Action<SaveData> OnGameLoaded;
    public static void GameLoaded(SaveData data) => OnGameLoaded?.Invoke(data);

    // ── Inventory events ──────────────────────────────────────────────────────

    /// <summary>
    /// Fired by RunInventorySystem whenever the inventory panel is opened or closed.
    /// true = opened, false = closed.
    /// </summary>
    public static event Action<bool> OnInventoryToggled;
    public static void InventoryToggled(bool isOpen) => OnInventoryToggled?.Invoke(isOpen);
}
public enum GameState
{
    Exploration,    // player is walking around freely
    BoardMode,      // player is at the corkboard
    VhsMode,        // player is at the VHS player (camera locked to TV)
    TapeMode,       // player is inside a tape scene (TapeDirector controls movement/camera)
    InspectMode,    // player is examining a held or hotbar object up close
    InventoryMode,  // player has the run-inventory panel open (movement + look locked)
    Paused
}
public enum InteractKey { Either, LeftClick, UseKey }

public enum EndingType
{
    None,
    ThisIsNotForYou,      // all tapes + state 1/2 + board fully cleared and trashed
    Minotaur,             // minotaur state 3 + player enters house
    TheInfiniteDescent    // specific clue connections + player enters house
}

// Set more states as need, e.g. DiaryMode, etc