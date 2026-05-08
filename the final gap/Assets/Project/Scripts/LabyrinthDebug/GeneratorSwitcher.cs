using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages the toggle between DungeonGenerator3D (Classic) and ObeliskDungeonGenerator.
///
/// Setup in Unity
/// ──────────────
///  1. Create an empty GameObject called "GeneratorSwitcher" in your scene.
///  2. Attach this script.
///  3. Assign classicGenerator  → your existing DungeonGenerator3D GameObject.
///  4. Assign obeliskGenerator  → a new GameObject with ObeliskDungeonGenerator.
///  5. (Optional) Wire toggleButton, generatorLabel, stepCountLabel to UI Text/Button elements.
///  6. Press Play — Space/R hotkeys and all buttons route through the active generator.
///
/// The switcher enables/disables the generator GameObjects so their visuals are
/// automatically shown or hidden without any extra cleanup code.
/// </summary>
public class GeneratorSwitcher : MonoBehaviour
{
    // =========================================================================
    //  Inspector
    // =========================================================================

    [Header("Generators")]
    [Tooltip("The original DungeonGenerator3D — Classic mode")]
    public DungeonGenerator3D    classicGenerator;

    [Tooltip("The new ObeliskDungeonGenerator — Obelisk mode")]
    public ObeliskDungeonGenerator obeliskGenerator;

    [Header("UI (all optional)")]
    [Tooltip("Button that switches between generators")]
    public Button toggleButton;

    [Tooltip("Label on the toggle button")]
    public TextMeshProUGUI toggleButtonLabel;

    [Tooltip("Displays the active generator's name")]
    public TextMeshProUGUI generatorNameLabel;

    [Tooltip("Displays 'Step X / TotalSteps'")]
    public TextMeshProUGUI stepCountLabel;

    // =========================================================================
    //  Runtime state
    // =========================================================================

    bool _obeliskActive = false;

    /// <summary>The currently active generator as the shared interface.</summary>
    public IDungeonGenerator Active =>
        _obeliskActive
            ? (IDungeonGenerator)obeliskGenerator
            : (IDungeonGenerator)classicGenerator;

    // =========================================================================
    //  Unity lifecycle
    // =========================================================================

    void Start()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);

        // Start with Classic active, Obelisk hidden
        SetActive(false);
    }

    void Update()
    {
        // Refresh step counter every frame (cheap string update is fine here)
        UpdateStepLabel();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Switch to the other generator.  No-op while the active one is animating.
    /// </summary>
    public void Toggle()
    {
        if (Active != null && Active.IsAnimating)
        {
            Debug.Log("[GeneratorSwitcher] Cannot switch while a generator is animating.");
            return;
        }

        SetActive(!_obeliskActive);
    }

    /// <summary>
    /// Switch to a specific generator by flag.
    /// false = Classic  /  true = Obelisk
    /// </summary>
    public void SetActive(bool useObelisk)
    {
        _obeliskActive = useObelisk;

        // Toggle GameObject visibility — also pauses their Start/Update if applicable
        if (classicGenerator  != null) classicGenerator.gameObject.SetActive(!useObelisk);
        if (obeliskGenerator  != null) obeliskGenerator.gameObject.SetActive(useObelisk);

        // Tell the debug controller which generator to drive
        DungeonDebugController.Instance?.SetActiveGenerator(Active);

        RefreshUI();

        Debug.Log($"[GeneratorSwitcher] Active → {Active?.GeneratorName ?? "none"}");
    }

    // =========================================================================
    //  UI helpers
    // =========================================================================

    void RefreshUI()
    {
        if (toggleButtonLabel != null)
            toggleButtonLabel.text = _obeliskActive ? "Switch to Classic" : "Switch to Obelisk";

        if (generatorNameLabel != null)
            generatorNameLabel.text = Active?.GeneratorName ?? "—";

        UpdateStepLabel();
    }

    void UpdateStepLabel()
    {
        if (stepCountLabel == null || Active == null) return;
        stepCountLabel.text = $"Step {Active.CurrentStep} / {Active.TotalSteps}";
    }
}
