using UnityEngine;

/// <summary>
/// Wraps a set of invisible wall GameObjects (each with a Collider) that define
/// where the player is allowed to walk during a tape section.
///
/// The TapeDirector calls SetActive(true/false) on zones to open up new areas
/// as the narrative progresses — or seal off areas that should no longer be visited.
///
/// Setup:
///   1. Create a TapeBoundaryZone GameObject in your tape scene.
///   2. Add child GameObjects, each with a BoxCollider (not trigger), sized/positioned
///      to form the invisible walls of this zone. Assign them to the wallObjects array,
///      or just make them direct children and leave wallObjects empty — they'll
///      be auto-discovered on Awake.
///   3. Set the wall GameObjects to a layer that collides with the player
///      (e.g. "Default" or a dedicated "Boundary" layer).
///   4. Optionally: remove the MeshRenderer component so the walls are truly invisible,
///      or use a transparent material for editor visibility only.
///
/// The CharacterController naturally collides with these walls —
/// no extra physics setup required.
/// </summary>
public class TapeBoundaryZone : MonoBehaviour
{
    [Tooltip("Wall GameObjects to enable/disable. If left empty, all direct children are used.")]
    [SerializeField] GameObject[] wallObjects;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (wallObjects == null || wallObjects.Length == 0)
            GatherChildrenAsWalls();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Enable or disable the walls that form this boundary zone.</summary>
    public void SetActive(bool active)
    {
        foreach (var wall in wallObjects)
            if (wall != null) wall.SetActive(active);
    }

    // ── Private ────────────────────────────────────────────────────────────

    void GatherChildrenAsWalls()
    {
        wallObjects = new GameObject[transform.childCount];
        for (int i = 0; i < transform.childCount; i++)
            wallObjects[i] = transform.GetChild(i).gameObject;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Draws a label in the Scene view so you can identify zones without
    /// having to click on each one.
    /// </summary>
    void OnDrawGizmos()
    {
        UnityEditor.Handles.Label(transform.position, $"[Zone] {gameObject.name}");
    }
#endif
}
