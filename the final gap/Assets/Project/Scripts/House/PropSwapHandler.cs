using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Listens to OnTagConnectionResolved and fires prop swaps / distortions
/// on the relevant room whenever a positive Environment connection is made.
///
/// Handles three positive effects:
///   EnvironmentInHouse                — Environment + House
///   EnvironmentBelongsToCharacter     — Environment + Character (swap in character's bound room)
///   EnvironmentChangedByManipulation  — Environment + Manipulation (distort random subset, all rooms)
/// </summary>
public class PropSwapHandler : MonoBehaviour
{
    public static PropSwapHandler Instance { get; private set; }

    [Header("Distortion Settings")]
    [Tooltip("Max positional nudge per axis (world units). Keep small — props stay near their origin.")]
    public float distortPositionRange = 0.15f;

    [Tooltip("Max rotation offset per axis (degrees).")]
    public float distortRotationRange = 12f;

    [Tooltip("Scale variance — props scale between (1 - range) and (1 + range).")]
    [Range(0f, 0.3f)] public float distortScaleRange = 0.08f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()  => GameEvents.OnTagConnectionResolved += OnTagConnectionResolved;
    void OnDisable() => GameEvents.OnTagConnectionResolved -= OnTagConnectionResolved;

    // ── Event handler ─────────────────────────────────────────────────────────

    void OnTagConnectionResolved(TagConnectionResult result, CardBehaviour a, CardBehaviour b)
    {
        if (result.Polarity != ConnectionPolarity.Positive) return;

        switch (result.PositiveEffect)
        {
            case PositiveEffect.EnvironmentInHouse:
                HandleEnvironmentInHouse(a, b);
                break;

            case PositiveEffect.EnvironmentBelongsToCharacter:
                HandleEnvironmentBelongsToCharacter(a, b);
                break;

            case PositiveEffect.EnvironmentChangedByManipulation:
                HandleEnvironmentChangedByManipulation(a, b);
                break;
        }
    }

    // ── Effect implementations ────────────────────────────────────────────────

    void HandleEnvironmentInHouse(CardBehaviour a, CardBehaviour b)
    {
        CardBehaviour houseCard = a.CardTag == CardTag.House ? a : b;
        RoomConfig room = RoomConfig.FindByCard(houseCard);
        if (room == null)
        {
            Debug.LogWarning($"[PropSwap] EnvironmentInHouse: no RoomConfig found for '{houseCard.cardTitle}'.");
            return;
        }
        SwapRandom(room);
    }

    void HandleEnvironmentBelongsToCharacter(CardBehaviour a, CardBehaviour b)
    {
        CardBehaviour charCard = a.CardTag == CardTag.Character ? a : b;
        RoomConfig room = CharacterBindingHandler.Instance?.GetRoom(charCard);
        if (room == null)
        {
            Debug.LogWarning($"[PropSwap] EnvironmentBelongsToCharacter: '{charCard.cardTitle}' has no bound room.");
            return;
        }
        SwapRandom(room);
    }

    void HandleEnvironmentChangedByManipulation(CardBehaviour a, CardBehaviour b)
    {
        foreach (var room in RoomConfig.All)
            DistortRandom(room);
    }

    // ── Core helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Swaps a random subset of eligible prop slots in the given room.
    /// Only slots with at least one variant prefab are candidates.
    /// Each swap destroys the current object and instantiates a different prefab in its place.
    /// </summary>
    public void SwapRandom(RoomConfig room)
    {
        if (room == null) return;
        List<PropSlot> targets = room.GetRandomSwapTargets();
        foreach (var slot in targets)
            slot.SwapToRandom();

        Debug.Log($"[PropSwap] Swapped {targets.Count} prop(s) in '{room.roomName}'.");
    }

    /// <summary>
    /// Nudges a random subset of distortable props in the given room.
    /// Distortion is applied as a local-space offset so props stay near their origin.
    /// Reset (ResetToDefault) fully undoes distortion — it restores the original transform
    /// for default props and re-instantiates swapped variants clean.
    /// </summary>
    public void DistortRandom(RoomConfig room)
    {
        if (room == null) return;
        List<PropSlot> targets = room.GetRandomDistortTargets();
        int distorted = 0;

        foreach (var slot in targets)
        {
            GameObject active = slot.GetActive();
            if (active == null) continue;

            // Offset relative to current local position so repeated distortions
            // don't compound — each call nudges from where the prop currently sits.
            active.transform.localPosition += new Vector3(
                Random.Range(-distortPositionRange, distortPositionRange),
                Random.Range(-distortPositionRange, distortPositionRange),
                Random.Range(-distortPositionRange, distortPositionRange));

            active.transform.localEulerAngles += new Vector3(
                Random.Range(-distortRotationRange, distortRotationRange),
                Random.Range(-distortRotationRange, distortRotationRange),
                Random.Range(-distortRotationRange, distortRotationRange));

            float scale = Random.Range(1f - distortScaleRange, 1f + distortScaleRange);
            active.transform.localScale *= scale;

            distorted++;
        }

        Debug.Log($"[PropSwap] Distorted {distorted} prop(s) in '{room.roomName}'.");
    }

    /// <summary>Swaps a random subset of props in every registered room.</summary>
    public void SwapAll()
    {
        foreach (var room in RoomConfig.All)
            SwapRandom(room);
    }

    /// <summary>
    /// Resets all prop slots in every room to their default state.
    /// Destroys any swapped instances, re-activates the original scene props,
    /// and restores their original transforms — fully undoing swaps and distortions.
    /// </summary>
    public void ResetAll()
    {
        foreach (var room in RoomConfig.All)
            foreach (var slot in room.propSlots)
                slot.ResetToDefault();

        Debug.Log("[PropSwap] All props reset to default.");
    }

    // ── Dev / Debug ───────────────────────────────────────────────────────────

    [ContextMenu("Test — Swap All Rooms")]
    void DevSwapAll() => SwapAll();

    [ContextMenu("Test — Distort All Rooms")]
    void DevDistortAll()
    {
        foreach (var room in RoomConfig.All)
            DistortRandom(room);
    }

    [ContextMenu("Test — Reset All Rooms")]
    void DevResetAll() => ResetAll();
}
