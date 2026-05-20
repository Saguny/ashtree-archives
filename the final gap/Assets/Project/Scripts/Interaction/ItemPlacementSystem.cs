using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Randomly distributes pickable items across ItemSpawnPoints in the scene at load time.
///
/// Setup:
///   1. Place ItemSpawnPoint GameObjects everywhere an item COULD appear
///      (shelves, desks, floors — and inside drawers with drawerOwner assigned).
///   2. Create prefabs for each pickable item and add them to the itemEntries list.
///   3. Attach this script to a persistent manager GameObject.
///
/// On Start, each item is randomly assigned to one unoccupied spawn point.
/// Items inside drawers are hidden automatically until the drawer opens.
/// </summary>
public class ItemPlacementSystem : MonoBehaviour
{
    public static ItemPlacementSystem Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Items to Place")]
    [Tooltip("Each entry describes one item to randomly place in the world.")]
    public ItemPlacementEntry[] itemEntries = new ItemPlacementEntry[0];

    [Header("Spawn Point Discovery")]
    [Tooltip("If true, all ItemSpawnPoints in the scene are found automatically on Start. " +
             "If false, assign them manually to manualSpawnPoints.")]
    public bool autoDiscoverSpawnPoints = true;

    [Tooltip("Only used when autoDiscoverSpawnPoints is false.")]
    public ItemSpawnPoint[] manualSpawnPoints = new ItemSpawnPoint[0];

    // ── Runtime ───────────────────────────────────────────────────────────────

    readonly List<ItemSpawnPoint> _available = new List<ItemSpawnPoint>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        BuildSpawnPool();
        PlaceAll();
    }

    // ── Core ──────────────────────────────────────────────────────────────────

    void BuildSpawnPool()
    {
        _available.Clear();

        if (autoDiscoverSpawnPoints)
        {
            // FindObjectsByType is the non-deprecated version of FindObjectsOfType
            var found = FindObjectsByType<ItemSpawnPoint>(FindObjectsSortMode.None);
            foreach (var sp in found)
                _available.Add(sp);
        }
        else
        {
            foreach (var sp in manualSpawnPoints)
                if (sp != null) _available.Add(sp);
        }

        // Shuffle the pool so items don't always end up in the same spots
        Shuffle(_available);

        Debug.Log($"[ItemPlacement] Found {_available.Count} spawn point(s).");
    }

    void PlaceAll()
    {
        foreach (var entry in itemEntries)
        {
            if (entry.itemPrefab == null) continue;

            // Skip items the player has already collected on a previous trip.
            // Item ID is keyed by prefab name — make sure prefab names are unique per item type.
            string itemId = entry.itemPrefab.name;
            if (RunInventorySystem.Instance != null && RunInventorySystem.Instance.IsCollected(itemId))
            {
                Debug.Log($"[ItemPlacement] '{itemId}' already collected — not spawning.");
                continue;
            }

            // Respect per-item spawn point tags if set
            ItemSpawnPoint chosen = PickSpawnPoint(entry);

            if (chosen == null)
            {
                Debug.LogWarning($"[ItemPlacement] No available spawn point for '{entry.itemPrefab.name}'. Item skipped.");
                continue;
            }

            GameObject instance = Instantiate(entry.itemPrefab);
            chosen.PlaceItem(instance);

            // So the spawn point knows to clear itself when the player picks the item up
            var pickable = instance.GetComponent<PickableObject>();
            if (pickable != null)
            {
                var tracker = instance.AddComponent<SpawnPointTracker>();
                tracker.Initialize(chosen);
            }

            Debug.Log($"[ItemPlacement] '{entry.itemPrefab.name}' → {chosen.name}" +
                      (chosen.IsInsideDrawer ? $" (inside drawer '{chosen.Drawer.name}')" : ""));
        }
    }

    ItemSpawnPoint PickSpawnPoint(ItemPlacementEntry entry)
    {
        // If the entry restricts to drawer-only or open-world-only, filter accordingly
        for (int i = 0; i < _available.Count; i++)
        {
            var sp = _available[i];
            if (sp.IsOccupied) continue;

            bool drawerOk     = entry.allowDrawerSlots    || !sp.IsInsideDrawer;
            bool openWorldOk  = entry.allowOpenWorldSlots ||  sp.IsInsideDrawer;
            if (!drawerOk || !openWorldOk) continue;

            _available.RemoveAt(i); // mark as used (remove from pool)
            return sp;
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

// ── ItemPlacementEntry ────────────────────────────────────────────────────────

[System.Serializable]
public class ItemPlacementEntry
{
    [Tooltip("Prefab of the pickable item to place. Must have a PickableObject component.")]
    public GameObject itemPrefab;

    [Tooltip("Allow this item to end up inside a drawer.")]
    public bool allowDrawerSlots = true;

    [Tooltip("Allow this item to appear on open surfaces (shelves, desks, floors).")]
    public bool allowOpenWorldSlots = true;
}

// ── SpawnPointTracker ─────────────────────────────────────────────────────────

/// <summary>
/// Auto-added to instantiated items. Notifies the spawn point when the item is picked up,
/// so the slot becomes available again (useful for future respawn or save systems).
/// </summary>
public class SpawnPointTracker : MonoBehaviour
{
    ItemSpawnPoint _spawnPoint;

    public void Initialize(ItemSpawnPoint sp) => _spawnPoint = sp;

    // Called by PickupSystem indirectly — when the object is no longer at its spawn position,
    // we clear the slot. Simplest trigger: OnDisable (item pocketed / held).
    void OnDisable()
    {
        _spawnPoint?.ClearItem();
    }
}
