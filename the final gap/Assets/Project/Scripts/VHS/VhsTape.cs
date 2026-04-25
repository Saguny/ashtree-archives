using UnityEngine;

/// <summary>
/// A VHS tape that can be picked up, stored in ItemInventory (G), and inserted
/// into a VhsInsertSlot. When dropped while in VhsMode it auto-snaps to the
/// nearest slot — mirroring how CardBehaviour snaps to SlotBehaviour on the board.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VhsTape : PickableObject
{
    [Header("Tape Info")]
    [Tooltip("The label shown in the VHS player tooltip, e.g. 'Shiny EV-DT 1'")]
    public string tapeLabel = "Shiny EV-DT 1";
    [Tooltip("Exact scene name to load when this tape is played (must be in Build Settings).")]
    public string tapeSceneName;

    [Header("Completion")]
    [Tooltip("Spawned on the desk when this tape finishes playing. Leave null for no sticky note.")]
    public GameObject stickyNotePrefab;

    [Header("Snap Settings")]
    [SerializeField] float snapDistance = 1.5f;

    protected override void Awake()
    {
        base.Awake();
        promptText = "Pick Up";
        interactKey = InteractKey.Either;
    }

    public override void OnDropped(Vector3 throwVelocity)
    {
        // In VhsMode, check for a nearby insert slot and snap to it.
        // Mirrors CardBehaviour.OnDropped -> FindNearestSlot / TryOccupy.
        if (GameManager.Instance.CurrentState == GameState.VhsMode)
        {
            VhsInsertSlot slot = FindNearestSlot();
            if (slot != null && !slot.IsOccupied)
            {
                slot.InsertTape(this);
                return;
            }
        }

        base.OnDropped(throwVelocity);
    }

    VhsInsertSlot FindNearestSlot()
    {
        VhsInsertSlot nearest = null;
        float nearestDist = snapDistance;

        foreach (var slot in FindObjectsByType<VhsInsertSlot>(FindObjectsSortMode.None))
        {
            float dist = Vector3.Distance(transform.position, slot.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = slot;
            }
        }

        return nearest;
    }
}
