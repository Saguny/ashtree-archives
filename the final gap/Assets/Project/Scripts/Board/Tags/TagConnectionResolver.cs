/// <summary>
/// Pure static evaluator for tag-based card connections.
/// No MonoBehaviour — no scene setup required, no allocation overhead.
///
/// Connection direction does NOT matter (per design doc),
/// so all checks are order-independent.
///
/// Full matrix:
///   Positive  │ Env+House   Env+Char   Env+Manip
///             │ House+Manip House+Char
///             │ Char+Manip  Char+Char
///   ──────────┼───────────────────────────────────
///   Negative  │ House+House (+1)
///             │ Manip+Manip (+2)
///             │ Env+Env     (+1)
/// </summary>
public static class TagConnectionResolver
{
    /// <summary>
    /// Evaluates a connection between two cards and returns the full result.
    /// Call this every time a yarn connection is made on the corkboard.
    /// </summary>
    public static TagConnectionResult Evaluate(CardTag a, CardTag b)
    {
        // ── Negative combos (same-tag pairings) ─────────────────────────────

        if (a == CardTag.House         && b == CardTag.House)
            return Negative(NegativeEffect.HouseHouse,              delta: 1);

        if (a == CardTag.Manipulation  && b == CardTag.Manipulation)
            return Negative(NegativeEffect.ManipulationManipulation, delta: 2);

        if (a == CardTag.Environment   && b == CardTag.Environment)
            return Negative(NegativeEffect.EnvironmentEnvironment,   delta: 1);

        // ── Positive combos (order-independent) ─────────────────────────────

        if (Match(a, b, CardTag.Environment, CardTag.House))
            return Positive(PositiveEffect.EnvironmentInHouse);

        if (Match(a, b, CardTag.Environment, CardTag.Character))
            return Positive(PositiveEffect.EnvironmentBelongsToCharacter);

        if (Match(a, b, CardTag.Environment, CardTag.Manipulation))
            return Positive(PositiveEffect.EnvironmentChangedByManipulation);

        if (Match(a, b, CardTag.House, CardTag.Manipulation))
            return Positive(PositiveEffect.HouseChangedByManipulation);

        if (Match(a, b, CardTag.House, CardTag.Character))
            return Positive(PositiveEffect.HouseBindsCharacter);

        if (Match(a, b, CardTag.Character, CardTag.Manipulation))
            return Positive(PositiveEffect.CharacterRoomChangedByManipulation);

        if (a == CardTag.Character     && b == CardTag.Character)
            return Positive(PositiveEffect.CharacterSwapsWithCharacter);

        // ── Fallback (None tag or any future unhandled combo) ────────────────
        return new TagConnectionResult
        {
            Polarity       = ConnectionPolarity.Positive,
            PositiveEffect = PositiveEffect.None,
            NegativeEffect = NegativeEffect.None,
            CounterDelta   = 0,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>True when {a,b} == {x,y} regardless of order.</summary>
    static bool Match(CardTag a, CardTag b, CardTag x, CardTag y)
        => (a == x && b == y) || (a == y && b == x);

    static TagConnectionResult Positive(PositiveEffect effect) => new TagConnectionResult
    {
        Polarity       = ConnectionPolarity.Positive,
        PositiveEffect = effect,
        NegativeEffect = NegativeEffect.None,
        CounterDelta   = 0,
    };

    static TagConnectionResult Negative(NegativeEffect effect, int delta) => new TagConnectionResult
    {
        Polarity       = ConnectionPolarity.Negative,
        PositiveEffect = PositiveEffect.None,
        NegativeEffect = effect,
        CounterDelta   = delta,
    };
}
