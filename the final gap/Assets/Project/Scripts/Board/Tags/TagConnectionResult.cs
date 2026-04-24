/// <summary>
/// The resolved outcome of connecting two cards on the corkboard.
/// Produced by TagConnectionResolver and broadcast via GameEvents.OnTagConnectionResolved.
/// </summary>
public struct TagConnectionResult
{
    public ConnectionPolarity Polarity;
    public PositiveEffect     PositiveEffect;
    public NegativeEffect     NegativeEffect;

    /// <summary>
    /// How much this connection adds to the Minotaur Counter.
    /// 0 for all positive combos; 1 or 2 for negative combos.
    /// Does NOT include the per-five-connections bonus — MinotaurCounter handles that separately.
    /// </summary>
    public int CounterDelta;
}

// ─── Polarity ────────────────────────────────────────────────────────────────

public enum ConnectionPolarity
{
    Positive,   // Valid, meaningful connection — triggers a house effect
    Negative,   // Contradictory / illogical — increments the Minotaur Counter
}

// ─── Positive effect types ───────────────────────────────────────────────────

/// <summary>
/// What kind of house change a positive connection should trigger.
/// The actual execution is handled by a separate house-manipulation system
/// that listens to GameEvents.OnTagConnectionResolved.
/// </summary>
public enum PositiveEffect
{
    None,

    /// <summary>Environment + House  →  props swap inside that specific room.</summary>
    EnvironmentInHouse,

    /// <summary>Environment + Character  →  props swap inside the character's bound room.</summary>
    EnvironmentBelongsToCharacter,

    /// <summary>House + Manipulation  →  room geometry is altered (swap, resize, extend…).</summary>
    HouseChangedByManipulation,

    /// <summary>House + Character  →  changes which room the character is bound to.</summary>
    HouseBindsCharacter,

    /// <summary>Character + Manipulation  →  the character's currently bound room is manipulated.</summary>
    CharacterRoomChangedByManipulation,

    /// <summary>Character + Character  →  the two characters swap their bound rooms.</summary>
    CharacterSwapsWithCharacter,

    /// <summary>Environment + Manipulation  →  specific props are distorted/altered.</summary>
    EnvironmentChangedByManipulation,
}

// ─── Negative effect types ───────────────────────────────────────────────────

/// <summary>
/// Which invalid combo triggered the Minotaur Counter increment.
/// </summary>
public enum NegativeEffect
{
    None,

    /// <summary>House + House  →  +1 to Minotaur Counter.</summary>
    HouseHouse,

    /// <summary>Manipulation + Manipulation  →  +2 to Minotaur Counter.</summary>
    ManipulationManipulation,

    /// <summary>Environment + Environment  →  +1 to Minotaur Counter.</summary>
    EnvironmentEnvironment,
}
