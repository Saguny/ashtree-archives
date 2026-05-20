using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to any button to play a sound when it is clicked.
/// Routes through AudioManager's shared UI AudioSource so all
/// click sounds respect the global volume fade during transitions.
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonClickSound : MonoBehaviour
{
    [SerializeField] private AudioClip clickClip;
    [SerializeField] [Range(0f, 1f)] private float volume = 1f;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(PlayClick);
    }

    private void OnDestroy()
    {
        // Clean up listener to avoid leaks on scene reload
        var btn = GetComponent<Button>();
        if (btn != null) btn.onClick.RemoveListener(PlayClick);
    }

    private void PlayClick()
    {
        AudioManager.Instance?.PlayUISound(clickClip, volume);
    }
}
