/// <summary>
/// The four invisible clue categories from the design document.
/// Each CardBehaviour is assigned exactly one tag in the Inspector.
///
/// Connection outcomes (direction-independent):
///   Positive  →  Environment+House, Environment+Character, Environment+Manipulation,
///                House+Manipulation, House+Character,
///                Character+Manipulation, Character+Character
///   Negative  →  House+House (+1), Manipulation+Manipulation (+2), Environment+Environment (+1)
/// </summary>
public enum CardTag
{
    None,

    /// <summary>Props and non-interactable décor (flower pots, lamps, kitchen utilities…).</summary>
    Environment,

    /// <summary>Named locations inside the house (kitchen, bedroom, living room…).</summary>
    House,

    /// <summary>Spatial alterations: size changes, room swaps, infinite hallways, portals…</summary>
    Manipulation,

    /// <summary>People tied to the house (Will Navidson, Karen Green, Chad & Daisy, Tom).</summary>
    Character,
}
