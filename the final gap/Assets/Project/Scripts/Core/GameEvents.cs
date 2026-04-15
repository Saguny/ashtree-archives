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
}
public enum GameState
{
    Exploration,    // player is walking around freely
    BoardMode,      // player is at the corkboard
    Paused
}
public enum InteractKey { Either, LeftClick, UseKey }

// Set more states as need, e.g. TapeMode, DiaryMode, etc