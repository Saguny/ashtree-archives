using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Canvas Groups")]
    [SerializeField] private CanvasGroup mainMenuCanvasGroup;
    [SerializeField] private CanvasGroup settingsCanvasGroup;

    [Header("Transition")]
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("Load Game — No Save State")]
    [Tooltip("The TMP label on the Load Game button.")]
    [SerializeField] private TextMeshProUGUI loadGameText;
    [Tooltip("The Button component on Load Game (will be disabled if no save exists).")]
    [SerializeField] private Button loadGameButton;
    [Tooltip("Alpha applied to the Load Game label when no save file is detected.")]
    [SerializeField] [Range(0f, 1f)] private float noSaveAlpha = 0.2f;

    private bool isTransitioning = false;

    private void Start()
    {
        SetCanvasGroup(mainMenuCanvasGroup, true);
        SetCanvasGroup(settingsCanvasGroup, false);

        RefreshLoadGameState();
    }

    // -------------------------------------------------------
    // Load Game availability
    // -------------------------------------------------------

    private void RefreshLoadGameState()
    {
        if (loadGameText == null) return;

        bool hasSave = SaveSystem.Instance != null && SaveSystem.Instance.HasSaveFile;

        // Dim the label
        Color c = loadGameText.color;
        c.a = hasSave ? 1f : noSaveAlpha;
        loadGameText.color = c;

        // Also block interaction so the button can't be clicked
        if (loadGameButton != null)
            loadGameButton.interactable = hasSave;
    }

    // --- Called by the Settings button in the Main Menu ---
    public void OpenSettings()
    {
        if (isTransitioning) return;
        StartCoroutine(Transition(mainMenuCanvasGroup, settingsCanvasGroup));
    }

    // --- Called by the Back button inside the Settings canvas ---
    public void CloseSettings()
    {
        if (isTransitioning) return;
        StartCoroutine(Transition(settingsCanvasGroup, mainMenuCanvasGroup));
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // -------------------------------------------------------

    private IEnumerator Transition(CanvasGroup from, CanvasGroup to)
    {
        isTransitioning = true;

        yield return StartCoroutine(FadeCanvasGroup(from, 1f, 0f));
        SetCanvasGroup(from, false);

        SetCanvasGroup(to, true);
        to.alpha = 0f;
        yield return StartCoroutine(FadeCanvasGroup(to, 0f, 1f));

        isTransitioning = false;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to)
    {
        float elapsed = 0f;
        cg.alpha = from;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }

        cg.alpha = to;
    }

    private void SetCanvasGroup(CanvasGroup cg, bool active)
    {
        cg.alpha          = active ? 1f : 0f;
        cg.interactable   = active;
        cg.blocksRaycasts = active;
    }
}
