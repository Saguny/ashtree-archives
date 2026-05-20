using UnityEngine;

/// <summary>
/// Marks a world position as a possible spawn location for a pickable item.
///
/// Place this on an empty GameObject wherever an item COULD appear — on a shelf,
/// on the floor, inside a drawer, on a desk, etc.
///
/// If this spawn point is inside a drawer, assign the drawerOwner field.
/// The item will be hidden until the drawer is opened.
///
/// The ItemPlacementSystem reads all registered spawn points and randomly
/// assigns items to them on scene load.
/// </summary>
public class ItemSpawnPoint : MonoBehaviour
{
    [Header("Drawer (optional)")]
    [Tooltip("If this spawn point lives inside a drawer, assign the DrawerInteractable here. " +
             "Leave null for open-world spawn points (shelves, floor, desk etc.).")]
    [SerializeField] DrawerInteractable drawerOwner;

    [Header("Debug")]
    [Tooltip("Draw a gizmo sphere in the Scene view so you can see all spawn points.")]
    [SerializeField] bool showGizmo = true;
    [SerializeField] Color gizmoColor = new Color(0f, 1f, 0.5f, 0.4f);

    // ── Runtime ───────────────────────────────────────────────────────────────

    GameObject _placedItem;

    public bool IsOccupied        => _placedItem != null;
    public bool IsInsideDrawer    => drawerOwner != null;
    public DrawerInteractable Drawer => drawerOwner;

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ItemPlacementSystem. Places the given item at this point,
    /// parents it to this transform so it inherits movement (important for drawers),
    /// and registers it with the drawer if needed.
    /// </summary>
    public void PlaceItem(GameObject item)
    {
        if (item == null) return;

        _placedItem = item;

        // Parent to this spawn point so drawer items slide with the drawer
        item.transform.SetParent(transform, worldPositionStays: false);
        item.transform.localPosition = Vector3.zero;
        item.transform.localRotation = Quaternion.identity;

        if (drawerOwner != null)
        {
            // Hide the item — drawer reveals it on open
            item.SetActive(false);
            drawerOwner.RegisterSpawnedItem(item);
        }
        else
        {
            item.SetActive(true);
        }
    }

    /// <summary>
    /// Removes the item from this spawn point (e.g. after the player picks it up).
    /// </summary>
    public void ClearItem()
    {
        _placedItem = null;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!showGizmo) return;
        Gizmos.color = drawerOwner != null
            ? new Color(1f, 0.6f, 0f, 0.5f)   // orange = drawer slot
            : gizmoColor;                        // green  = open world slot
        Gizmos.DrawSphere(transform.position, 0.04f);
        Gizmos.DrawWireSphere(transform.position, 0.06f);
    }
}
