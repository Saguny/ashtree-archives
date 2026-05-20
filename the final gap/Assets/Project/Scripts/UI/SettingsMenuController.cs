using UnityEngine;
using TMPro;

public class SettingsMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject audioPanel;
    [SerializeField] private GameObject controlPanel;

    [Header("Tab Button Texts")]
    [SerializeField] private TextMeshProUGUI audioButtonText;
    [SerializeField] private TextMeshProUGUI controlsButtonText;

    [Header("Alpha Values")]
    [SerializeField] private float selectedAlpha    = 1.0f;
    [SerializeField] private float deselectedAlpha  = 0.7f;
    [SerializeField] private float hoverAlpha       = 0.9f;

    // Which tab is currently active
    private bool audioIsSelected = true;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void OnEnable()
    {
        // Always reset to Audio tab when the Settings canvas is opened
        SelectAudio();
    }

    // -------------------------------------------------------
    // Public — wire these to the Button onClick events
    // -------------------------------------------------------

    public void SelectAudio()
    {
        if (audioIsSelected) return;

        audioIsSelected = true;
        audioPanel.SetActive(true);
        controlPanel.SetActive(false);

        SetAlpha(audioButtonText,    selectedAlpha);
        SetAlpha(controlsButtonText, deselectedAlpha);
    }

    public void SelectControls()
    {
        if (!audioIsSelected) return;

        audioIsSelected = false;
        controlPanel.SetActive(true);
        audioPanel.SetActive(false);

        SetAlpha(controlsButtonText, selectedAlpha);
        SetAlpha(audioButtonText,    deselectedAlpha);
    }

    // -------------------------------------------------------
    // Hover callbacks — called by SettingsTabButton helpers
    // -------------------------------------------------------

    public void OnAudioHoverEnter()
    {
        if (!audioIsSelected)
            SetAlpha(audioButtonText, hoverAlpha);
    }

    public void OnAudioHoverExit()
    {
        if (!audioIsSelected)
            SetAlpha(audioButtonText, deselectedAlpha);
    }

    public void OnControlsHoverEnter()
    {
        if (audioIsSelected)
            SetAlpha(controlsButtonText, hoverAlpha);
    }

    public void OnControlsHoverExit()
    {
        if (audioIsSelected)
            SetAlpha(controlsButtonText, deselectedAlpha);
    }

    // -------------------------------------------------------
    // Helper
    // -------------------------------------------------------

    private void SetAlpha(TextMeshProUGUI tmp, float alpha)
    {
        Color c = tmp.color;
        c.a = alpha;
        tmp.color = c;
    }
}
