using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages which room each Character card is currently bound to.
/// Pre-wire default bindings in the Inspector; the runtime Dictionary
/// is built from those defaults on Start and then updated live by the
/// tag connection system.
///
/// Handles two positive effects:
///   HouseBindsCharacter          — House card connects with Character card
///   CharacterSwapsWithCharacter  — two Character cards connect
/// </summary>
public class CharacterBindingHandler : MonoBehaviour
{
    public static CharacterBindingHandler Instance { get; private set; }

    [Header("Default Bindings")]
    [Tooltip("Wire up every Character card and the room it starts in.")]
    public CharacterBinding[] defaultBindings = new CharacterBinding[0];

    // ── Runtime state ─────────────────────────────────────────────────────────
    readonly Dictionary<CardBehaviour, RoomConfig> _bindings
        = new Dictionary<CardBehaviour, RoomConfig>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Populate runtime dictionary from Inspector-set defaults
        foreach (var b in defaultBindings)
        {
            if (b.characterCard != null)
                _bindings[b.characterCard] = b.defaultRoom;
        }
    }

    void OnEnable()  => GameEvents.OnTagConnectionResolved += OnTagConnectionResolved;
    void OnDisable() => GameEvents.OnTagConnectionResolved -= OnTagConnectionResolved;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns the room this character card is currently bound to, or null.</summary>
    public RoomConfig GetRoom(CardBehaviour characterCard)
    {
        _bindings.TryGetValue(characterCard, out var room);
        return room;
    }

    /// <summary>Forcibly sets a character's bound room — also fires GameEvents.</summary>
    public void SetRoom(CardBehaviour characterCard, RoomConfig room)
    {
        if (characterCard == null) return;
        _bindings[characterCard] = room;
        GameEvents.CharacterBindingChanged(characterCard, room);
        Debug.Log($"[CharacterBinding] {characterCard.cardTitle} → {(room != null ? room.roomName : "none")}");
    }

    /// <summary>Returns a read-only snapshot of all current bindings (for dev tool display).</summary>
    public IReadOnlyDictionary<CardBehaviour, RoomConfig> AllBindings => _bindings;

    // ── Event handler ─────────────────────────────────────────────────────────

    void OnTagConnectionResolved(TagConnectionResult result, CardBehaviour a, CardBehaviour b)
    {
        if (result.Polarity != ConnectionPolarity.Positive) return;

        switch (result.PositiveEffect)
        {
            case PositiveEffect.HouseBindsCharacter:
                HandleHouseBindsCharacter(a, b);
                break;

            case PositiveEffect.CharacterSwapsWithCharacter:
                HandleCharacterSwap(a, b);
                break;
        }
    }

    // ── Effect implementations ────────────────────────────────────────────────

    /// <summary>
    /// One card is a House card, the other is a Character card.
    /// The Character is bound to whichever room the House card represents.
    /// </summary>
    void HandleHouseBindsCharacter(CardBehaviour a, CardBehaviour b)
    {
        CardBehaviour houseCard     = a.CardTag == CardTag.House      ? a : b;
        CardBehaviour characterCard = a.CardTag == CardTag.Character  ? a : b;

        if (houseCard == null || characterCard == null) return;

        RoomConfig room = RoomConfig.FindByCard(houseCard);
        if (room == null)
        {
            Debug.LogWarning($"[CharacterBinding] HouseBindsCharacter: no RoomConfig found for House card '{houseCard.cardTitle}'.");
            return;
        }

        SetRoom(characterCard, room);
    }

    /// <summary>
    /// Two Character cards connected — swap their room bindings.
    /// </summary>
    void HandleCharacterSwap(CardBehaviour a, CardBehaviour b)
    {
        _bindings.TryGetValue(a, out var roomA);
        _bindings.TryGetValue(b, out var roomB);

        // Swap
        _bindings[a] = roomB;
        _bindings[b] = roomA;

        GameEvents.CharacterBindingChanged(a, roomB);
        GameEvents.CharacterBindingChanged(b, roomA);

        Debug.Log($"[CharacterBinding] Swapped: {a.cardTitle} ↔ {b.cardTitle}");
    }

    // ── Dev / Debug ───────────────────────────────────────────────────────────

    [ContextMenu("Log All Bindings")]
    void LogAllBindings()
    {
        if (_bindings.Count == 0) { Debug.Log("[CharacterBinding] No bindings registered."); return; }
        foreach (var kvp in _bindings)
            Debug.Log($"  {kvp.Key?.cardTitle ?? "null"} → {kvp.Value?.roomName ?? "none"}");
    }
}

// ── CharacterBinding (serializable data) ─────────────────────────────────────

[System.Serializable]
public class CharacterBinding
{
    [Tooltip("The Character-tagged card for this person.")]
    public CardBehaviour characterCard;

    [Tooltip("The room this character starts in (can be null if they start unbound).")]
    public RoomConfig defaultRoom;
}
