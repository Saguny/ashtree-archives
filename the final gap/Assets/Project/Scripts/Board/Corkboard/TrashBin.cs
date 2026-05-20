using UnityEngine;

/// <summary>
/// Physics-trigger trash receptacle for the corkboard area.
///
/// Place a GameObject with a Collider (set as Trigger) in the scene — ideally
/// a wastebasket near the corkboard. When a non-pinned CardBehaviour enters the
/// trigger volume (e.g. the player throws a card into it), the card is:
///   1. Reported via GameEvents.CardTrashed  → EndingManager ticks it off its list
///   2. Destroyed
///
/// The card must NOT be currently pinned — pinned cards are still on the board
/// and can only be trashed after the player unpins them first.
///
/// OPTIONAL: assign a ParticleSystem to _trashFX for a small paper-crumple effect.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TrashBin : MonoBehaviour
{
    [Header("Optional FX")]
    [Tooltip("Particle effect played when a card is trashed. Leave empty for none.")]
    [SerializeField] ParticleSystem _trashFX;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        // Enforce trigger mode — nothing should collide with the bin as a solid.
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[TrashBin] Collider on '{name}' was not set to Trigger — fixed at runtime.", this);
        }
    }

    // ── Physics trigger ───────────────────────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        // Walk up the hierarchy in case the collider lives on a child object.
        CardBehaviour card = other.GetComponentInParent<CardBehaviour>();
        if (card == null) return;

        // Only accept cards that have been removed from the board.
        // Pinned cards are still corkboard-resident — the player must unpin first.
        if (card.IsPinned) return;

        TrashCard(card);
    }

    // ── Core logic ────────────────────────────────────────────────────────────

    void TrashCard(CardBehaviour card)
    {
        Debug.Log($"[TrashBin] Card trashed: {card.cardTitle}");

        // Notify the ending system (and any other subscribers) before destroying.
        GameEvents.CardTrashed(card);

        if (_trashFX != null)
        {
            _trashFX.transform.position = card.transform.position;
            _trashFX.Play();
        }

        Destroy(card.gameObject);
    }
}
