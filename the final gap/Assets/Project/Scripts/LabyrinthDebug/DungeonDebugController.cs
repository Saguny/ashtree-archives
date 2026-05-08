using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class DungeonDebugController : MonoBehaviour
{
    public static DungeonDebugController Instance { get; private set; }

    // Legacy inspector field — kept so existing scene setups don't break.
    // The active generator can be overridden at runtime via SetActiveGenerator().
    [Tooltip("Classic generator. Assigned automatically if left empty.")]
    public DungeonGenerator3D generator;

    [Header("UI")]
    public TextMeshProUGUI stepText;
    public TextMeshProUGUI descriptionText;
    public Button nextButton;
    public Button resetButton;
    public Button newChunkButton;

    [Header("Input Actions")]
    public InputActionReference nextStepAction;
    public InputActionReference resetAction;

    // Internal active-generator reference (interface).
    // GeneratorSwitcher updates this at runtime when toggling between modes.
    IDungeonGenerator _active;

    // =========================================================================
    //  Unity lifecycle
    // =========================================================================

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        if (nextStepAction != null)
        {
            nextStepAction.action.Enable();
            nextStepAction.action.performed += OnNextStepInput;
        }

        if (resetAction != null)
        {
            resetAction.action.Enable();
            resetAction.action.performed += OnResetInput;
        }
    }

    void OnDisable()
    {
        if (nextStepAction != null)
        {
            nextStepAction.action.performed -= OnNextStepInput;
            nextStepAction.action.Disable();
        }

        if (resetAction != null)
        {
            resetAction.action.performed -= OnResetInput;
            resetAction.action.Disable();
        }
    }

    void Start()
    {
        // Fall back to the inspector-assigned classic generator if nothing else
        // has called SetActiveGenerator yet (preserves original scene behaviour).
        if (generator == null)
            generator = FindObjectOfType<DungeonGenerator3D>();

        if (_active == null)
            _active = generator;

        if (nextButton     != null) nextButton.onClick.AddListener(OnNextStep);
        if (resetButton    != null) resetButton.onClick.AddListener(OnReset);
        if (newChunkButton != null) newChunkButton.onClick.AddListener(OnNewChunk);

        UpdateUI();
        UpdateStepDescription(_active != null ? _active.GetStepDescription() : "Ready");
    }

    // =========================================================================
    //  Public API — called by GeneratorSwitcher
    // =========================================================================

    /// <summary>
    /// Switch the active generator. Called by GeneratorSwitcher on toggle.
    /// </summary>
    public void SetActiveGenerator(IDungeonGenerator gen)
    {
        _active = gen;
        UpdateUI();
        if (gen != null)
            UpdateStepDescription(gen.GetStepDescription());
    }

    // =========================================================================
    //  Input callbacks
    // =========================================================================

    void OnNextStepInput(InputAction.CallbackContext ctx) { OnNextStep(); }
    void OnResetInput(InputAction.CallbackContext ctx)    { OnReset(); }

    void OnNextStep()
    {
        if (_active == null) return;
        _active.NextStep();
        UpdateUI();
    }

    void OnReset()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    void OnNewChunk()
    {
        if (_active == null) return;
        _active.GenerateNewChunk();
        UpdateUI();
    }

    // =========================================================================
    //  UI
    // =========================================================================

    void UpdateUI()
    {
        if (stepText == null || _active == null) return;
        stepText.text = string.Format(
            "Step {0}/{1}  [{2}]\n[Space] Next  |  [R] Reset",
            _active.CurrentStep,
            _active.TotalSteps,
            _active.GeneratorName);
    }

    public void UpdateStepDescription(string description)
    {
        if (descriptionText != null)
            descriptionText.text = description;
    }
}
