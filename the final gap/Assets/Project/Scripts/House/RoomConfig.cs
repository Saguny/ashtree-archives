using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Place this on every room's root GameObject.
/// Holds all swappable prop slots and swap/distort fraction settings.
/// Registers itself in a static list so other systems can find rooms
/// without needing direct references — purely plug-and-play.
/// </summary>
public class RoomConfig : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Display name used by the dev tool and binding system.")]
    public string roomName = "Room";

    [Tooltip("All House-tagged CardBehaviours on the corkboard that represent this room. " +
             "A room can have multiple cards (e.g. one per angle/clue). " +
             "Any matching card will resolve to this room.")]
    public CardBehaviour[] roomCards = new CardBehaviour[0];

    [Header("Prop Slots")]
    [Tooltip("Each slot represents one prop position in the scene. " +
             "Drag the prop that is already placed in the scene into Default Prop, " +
             "then optionally add variant prefabs to swap to.")]
    public PropSlot[] propSlots = new PropSlot[0];

    [Header("Swap Settings")]
    [Tooltip("Minimum fraction of swappable slots affected per swap trigger.")]
    [Range(0f, 1f)] public float minSwapFraction = 0.25f;

    [Tooltip("Maximum fraction of swappable slots affected per swap trigger.")]
    [Range(0f, 1f)] public float maxSwapFraction = 0.6f;

    [Header("Distort Settings")]
    [Tooltip("Minimum fraction of distortable slots jittered per distort trigger.")]
    [Range(0f, 1f)] public float minDistortFraction = 0.2f;

    [Tooltip("Maximum fraction of distortable slots jittered per distort trigger.")]
    [Range(0f, 1f)] public float maxDistortFraction = 0.5f;

    // ── Static registry ───────────────────────────────────────────────────────

    static readonly List<RoomConfig> _all = new List<RoomConfig>();
    public static IReadOnlyList<RoomConfig> All => _all;

    void OnEnable()  => _all.Add(this);
    void OnDisable() => _all.Remove(this);

    void Start()
    {
        // Capture each slot's original transform so Reset can fully undo distortions.
        // Nothing is spawned — the scene already has the default props placed.
        foreach (var slot in propSlots)
            slot.Initialize();
    }

    // ── Static finders ────────────────────────────────────────────────────────

    /// <summary>Find the room that contains the given card in its roomCards list.</summary>
    public static RoomConfig FindByCard(CardBehaviour card)
    {
        foreach (var r in _all)
            foreach (var c in r.roomCards)
                if (c == card) return r;
        return null;
    }

    /// <summary>Find a room by its display name (case-insensitive).</summary>
    public static RoomConfig FindByName(string name)
    {
        foreach (var r in _all)
            if (string.Equals(r.roomName, name, System.StringComparison.OrdinalIgnoreCase))
                return r;
        return null;
    }

    // ── Slot selection ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a random subset of slots that HAVE variant prefabs (swap candidates only).
    /// Fraction is taken from the filtered list, not the full slot array.
    /// </summary>
    public List<PropSlot> GetRandomSwapTargets()
    {
        // Only slots with at least one variant prefab can be swapped
        var eligible = new List<PropSlot>();
        foreach (var s in propSlots)
            if (s.HasVariants) eligible.Add(s);

        return PickRandomSubset(eligible, minSwapFraction, maxSwapFraction);
    }

    /// <summary>
    /// Returns a random subset of distortable slots (any slot with distortable = true,
    /// regardless of whether it has variant prefabs).
    /// </summary>
    public List<PropSlot> GetRandomDistortTargets()
    {
        var eligible = new List<PropSlot>();
        foreach (var s in propSlots)
            if (s.distortable && s.GetActive() != null) eligible.Add(s);

        return PickRandomSubset(eligible, minDistortFraction, maxDistortFraction);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    static List<PropSlot> PickRandomSubset(List<PropSlot> list, float minFrac, float maxFrac)
    {
        if (list.Count == 0) return list;

        // Fisher-Yates shuffle
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        int min   = Mathf.Max(1, Mathf.FloorToInt(list.Count * minFrac));
        int max   = Mathf.Min(list.Count, Mathf.CeilToInt(list.Count * maxFrac));
        int count = Random.Range(min, max + 1);

        return list.GetRange(0, count);
    }
}

// ── PropSlot ─────────────────────────────────────────────────────────────────

[System.Serializable]
public class PropSlot
{
    [Tooltip("The prop already placed in the scene. This IS the default state — " +
             "no spawning happens on load. On swap it deactivates; on reset it comes back.")]
    public GameObject defaultProp;

    [Tooltip("Prefab variants to randomly swap to. Leave empty if this prop has no variants " +
             "(it can still be distorted if distortable is ticked).")]
    public GameObject[] variantPrefabs = new GameObject[0];

    [Tooltip("If true, Environment + Manipulation connections can nudge this prop's transform.")]
    public bool distortable = true;

    // ── Runtime state ─────────────────────────────────────────────────────────

    [System.NonSerialized] GameObject _swappedInstance;

    // Original transform of defaultProp — captured in Initialize() for clean Reset
    [System.NonSerialized] Vector3    _origLocalPos;
    [System.NonSerialized] Quaternion _origLocalRot;
    [System.NonSerialized] Vector3    _origLocalScale;
    [System.NonSerialized] bool       _initialized;

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>True if this slot has at least one variant prefab to swap to.</summary>
    public bool HasVariants => variantPrefabs != null && variantPrefabs.Length > 0;

    /// <summary>Returns the currently active GameObject (swapped instance or defaultProp).</summary>
    public GameObject GetActive() => _swappedInstance != null ? _swappedInstance : defaultProp;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the defaultProp's original transform. Called once by RoomConfig.Start().
    /// Does NOT spawn anything — the scene is already dressed.
    /// </summary>
    public void Initialize()
    {
        if (_initialized || defaultProp == null) return;
        _origLocalPos   = defaultProp.transform.localPosition;
        _origLocalRot   = defaultProp.transform.localRotation;
        _origLocalScale = defaultProp.transform.localScale;
        _initialized    = true;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deactivates the current prop and instantiates a random variant prefab in its place.
    /// The variant is placed at the defaultProp's world position / rotation / scale.
    /// No-op if no variant prefabs are configured.
    /// </summary>
    public void SwapToRandom()
    {
        if (!HasVariants) return;

        // Pick a random variant (avoid repeating the same one if possible)
        int chosen = Random.Range(0, variantPrefabs.Length);
        // Simple re-roll: if we happen to have the same prefab as current, move to next
        // (only meaningful once multiple swaps have occurred)
        if (variantPrefabs.Length > 1 && _swappedInstance != null)
            chosen = (chosen + 1) % variantPrefabs.Length;

        // Hide whatever is currently showing
        SetCurrentInactive();

        // Instantiate the chosen variant as a sibling of defaultProp
        Transform parent = defaultProp != null ? defaultProp.transform.parent : null;
        _swappedInstance = Object.Instantiate(
            variantPrefabs[chosen],
            defaultProp != null ? defaultProp.transform.position : Vector3.zero,
            defaultProp != null ? defaultProp.transform.rotation : Quaternion.identity,
            parent
        );
        // Match the default prop's local scale
        if (defaultProp != null)
            _swappedInstance.transform.localScale = defaultProp.transform.localScale;
    }

    /// <summary>
    /// Destroys any swapped instance, re-activates defaultProp,
    /// and restores its original transform (undoes distortion too).
    /// </summary>
    public void ResetToDefault()
    {
        if (_swappedInstance != null)
        {
            Object.Destroy(_swappedInstance);
            _swappedInstance = null;
        }

        if (defaultProp == null) return;

        defaultProp.SetActive(true);

        if (_initialized)
        {
            defaultProp.transform.localPosition = _origLocalPos;
            defaultProp.transform.localRotation = _origLocalRot;
            defaultProp.transform.localScale    = _origLocalScale;
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    void SetCurrentInactive()
    {
        if (_swappedInstance != null)
        {
            Object.Destroy(_swappedInstance);
            _swappedInstance = null;
        }
        if (defaultProp != null)
            defaultProp.SetActive(false);
    }
}
