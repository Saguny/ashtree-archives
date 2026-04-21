using UnityEngine;

/// <summary>
/// Placeholder — tape storage has been merged into HotbarSystem (F key).
/// This component stays in the scene to avoid missing-script warnings but does nothing.
/// </summary>
public class ItemInventory : MonoBehaviour
{
    public static ItemInventory Instance { get; private set; }

    // Thin redirects so any leftover references still compile
    public bool HasTape => HotbarSystem.Instance != null && HotbarSystem.Instance.HasTape;
    public VhsTape StoredTape => HotbarSystem.Instance != null ? HotbarSystem.Instance.StoredTape : null;
    public VhsTape RemoveTape() => HotbarSystem.Instance != null ? HotbarSystem.Instance.RemoveTape() : null;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }
}
