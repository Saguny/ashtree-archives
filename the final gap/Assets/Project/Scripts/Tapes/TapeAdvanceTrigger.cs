using UnityEngine;

/// <summary>
/// A trigger volume that the TapeDirector can wait on before advancing to the next beat.
/// Use this for "walk toward the light" moments where narration should only continue
/// once the player physically moves to a specific spot.
///
/// Setup:
///   1. Add this to a GameObject with a Collider set to Is Trigger.
///   2. Make sure the player GameObject has the "Player" tag.
///   3. Assign this to a TapeBeat's advanceTrigger field in TapeDirector.
///   4. The TapeDirector calls Arm() when it starts waiting, which resets the trigger
///      and (optionally) enables the zone indicator visual.
///
/// The trigger auto-disables itself after the player enters, so it can't be
/// re-triggered unintentionally.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TapeAdvanceTrigger : MonoBehaviour
{
    [Tooltip("Optional visual that pulses or glows to guide the player toward this spot. " +
             "Enabled when the trigger is armed, hidden once triggered.")]
    [SerializeField] GameObject zoneIndicator;

    /// <summary>True once the player has stepped into this trigger zone.</summary>
    public bool HasBeenTriggered { get; private set; }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        // Start disabled — TapeDirector arms us when needed
        gameObject.SetActive(false);
        if (zoneIndicator != null) zoneIndicator.SetActive(false);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by TapeDirector when it's ready to wait for the player.
    /// Resets the triggered flag and shows the optional zone indicator.
    /// </summary>
    public void Arm()
    {
        HasBeenTriggered = false;
        gameObject.SetActive(true);
        if (zoneIndicator != null) zoneIndicator.SetActive(true);
    }

    // ── Trigger ────────────────────────────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        HasBeenTriggered = true;
        if (zoneIndicator != null) zoneIndicator.SetActive(false);
        // Leave the collider active — TapeDirector disables it after processing
    }
}
