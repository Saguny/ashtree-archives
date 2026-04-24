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
    public static event Action<CardBehaviour, SlotBehaviour> OnCardPinned;
    public static void CardPinned(CardBehaviour card, SlotBehaviour slot) => OnCardPinned?.Invoke(card, slot);

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
}
public enum GameState
{
    Exploration,    // player is walking around freely
    BoardMode,      // player is at the corkboard
    VhsMode,        // player is at the VHS player (camera locked to TV)
    Paused
}
public enum InteractKey { Either, LeftClick, UseKey }

// Set more states as need, e.g. DiaryMode, etc