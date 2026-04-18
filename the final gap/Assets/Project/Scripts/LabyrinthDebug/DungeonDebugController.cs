using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class DungeonDebugController : MonoBehaviour
{
    public static DungeonDebugController Instance { get; private set; }
    
    public DungeonGenerator3D generator;
    public TextMeshProUGUI stepText;
    public TextMeshProUGUI descriptionText;
    public Button nextButton;
    public Button resetButton;
    public Button newChunkButton;
    
    [Header("Input Actions")]
    public InputActionReference nextStepAction;
    public InputActionReference resetAction;
    
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
        if (generator == null)
            generator = FindObjectOfType<DungeonGenerator3D>();
        
        if (nextButton != null)
            nextButton.onClick.AddListener(OnNextStep);
        
        if (resetButton != null)
            resetButton.onClick.AddListener(OnReset);
        
        if (newChunkButton != null)
            newChunkButton.onClick.AddListener(OnNewChunk);
        
        UpdateUI();
        UpdateStepDescription("Ready to generate dungeon");
    }
    
    void OnNextStepInput(InputAction.CallbackContext context)
    {
        OnNextStep();
    }
    
    void OnResetInput(InputAction.CallbackContext context)
    {
        OnReset();
    }
    
    void OnNextStep()
    {
        if (generator != null)
        {
            generator.NextStep();
            UpdateUI();
        }
    }
    
    void OnReset()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }
    
    void OnNewChunk()
    {
        if (generator != null)
        {
            generator.GenerateNewChunk();
            UpdateUI();
        }
    }
    
    void UpdateUI()
    {
        if (stepText != null && generator != null)
        {
            stepText.text = $"Step {generator.currentStep}/6\n[Space] Next | [R] Reset";
        }
    }
    
    public void UpdateStepDescription(string description)
    {
        if (descriptionText != null)
        {
            descriptionText.text = description;
        }
    }
}
