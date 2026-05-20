using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class AudioSettingsController : MonoBehaviour
{
    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Sliders")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider dialogueSlider;
    [SerializeField] private Slider ambienceSlider;

    // These must match the exact names you used when exposing
    // the parameters in the Audio Mixer window.
    [Header("Exposed Parameter Names")]
    [SerializeField] private string masterParam   = "MasterVol";
    [SerializeField] private string musicParam    = "Music";
    [SerializeField] private string dialogueParam = "DialogueVol";
    [SerializeField] private string ambienceParam = "AmbienceVol";

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Start()
    {
        InitSlider(masterSlider,   OnMasterChanged);
        InitSlider(musicSlider,    OnMusicChanged);
        InitSlider(dialogueSlider, OnDialogueChanged);
        InitSlider(ambienceSlider, OnAmbienceChanged);
    }

    // -------------------------------------------------------
    // Slider setup
    // -------------------------------------------------------

    private void InitSlider(Slider slider, UnityEngine.Events.UnityAction<float> callback)
    {
        if (slider == null) return;

        slider.minValue = 0f;
        slider.maxValue = 2f;
        slider.value    = 1f;   // middle = unity volume (0 dB)

        slider.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.AddListener(callback);

        // Apply the default value immediately so the mixer is in sync on load
        callback(slider.value);
    }

    // -------------------------------------------------------
    // Callbacks
    // -------------------------------------------------------

    public void OnMasterChanged(float value)   => SetMixerVolume(masterParam,   value);
    public void OnMusicChanged(float value)    => SetMixerVolume(musicParam,    value);
    public void OnDialogueChanged(float value) => SetMixerVolume(dialogueParam, value);
    public void OnAmbienceChanged(float value) => SetMixerVolume(ambienceParam, value);

    // -------------------------------------------------------
    // Conversion: linear (0–2) → dB
    //   0   → -80 dB  (effectively silent)
    //   1   →   0 dB  (unity gain, normal volume)
    //   2   →  ~6 dB  (amplified)
    // -------------------------------------------------------

    private void SetMixerVolume(string paramName, float linearValue)
    {
        float dB = linearValue <= 0.0001f ? -80f : Mathf.Log10(linearValue) * 20f;
        audioMixer.SetFloat(paramName, dB);
    }
}
