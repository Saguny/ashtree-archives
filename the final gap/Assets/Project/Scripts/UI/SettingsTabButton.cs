using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach this to the Audio or Controls button GameObject.
/// It forwards pointer-enter/exit events to SettingsMenuController
/// so the TMP alpha updates on hover without needing EventTrigger components.
/// </summary>
public class SettingsTabButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum TabType { Audio, Controls }

    [SerializeField] private TabType tab = TabType.Audio;

    private SettingsMenuController controller;

    private void Awake()
    {
        // Walk up the hierarchy to find the controller (it lives on the Settings canvas root)
        controller = GetComponentInParent<SettingsMenuController>();

        if (controller == null)
            Debug.LogWarning($"[SettingsTabButton] No SettingsMenuController found in parents of {gameObject.name}.");
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (controller == null) return;

        if (tab == TabType.Audio)
            controller.OnAudioHoverEnter();
        else
            controller.OnControlsHoverEnter();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (controller == null) return;

        if (tab == TabType.Audio)
            controller.OnAudioHoverExit();
        else
            controller.OnControlsHoverExit();
    }
}
