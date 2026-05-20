using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to any Main Menu button GameObject.
/// On hover: smoothly scales the button up and tints the TMP label.
/// On exit:  smoothly returns to normal scale and color.
/// All values are configurable in the Inspector.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MainMenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale")]
    [Tooltip("How much bigger the button gets on hover. 1.05 = 5% larger.")]
    [SerializeField] private float hoverScale    = 1.05f;
    [SerializeField] private float scaleDuration = 0.1f;

    [Header("Color")]
    [Tooltip("TMP color when idle. Leave at white to keep your original text color.")]
    [SerializeField] private Color normalColor = Color.white;
    [Tooltip("TMP color when the cursor is over the button.")]
    [SerializeField] private Color hoverColor  = new Color(0.85f, 0.75f, 0.55f, 1f);
    [SerializeField] private float colorDuration = 0.1f;

    [Header("References  (auto-found if left empty)")]
    [SerializeField] private TextMeshProUGUI label;

    // -------------------------------------------------------

    private Vector3     _originalScale;
    private Coroutine   _scaleRoutine;
    private Coroutine   _colorRoutine;
    private Button      _button;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        _originalScale = transform.localScale;
        _button        = GetComponent<Button>();

        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>();

        // Sync normalColor with whatever the designer set in TMP,
        // only if the user left it at the default white.
        if (label != null && normalColor == Color.white)
            normalColor = label.color;
    }

    // -------------------------------------------------------
    // Pointer events
    // -------------------------------------------------------

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;

        SetScale(hoverScale);
        SetColor(hoverColor);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;

        SetScale(1f);
        SetColor(normalColor);
    }

    // -------------------------------------------------------
    // Animation helpers
    // -------------------------------------------------------

    private void SetScale(float targetUniform)
    {
        if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
        _scaleRoutine = StartCoroutine(LerpScale(_originalScale * targetUniform));
    }

    private void SetColor(Color target)
    {
        if (label == null) return;
        if (_colorRoutine != null) StopCoroutine(_colorRoutine);
        _colorRoutine = StartCoroutine(LerpColor(target));
    }

    private IEnumerator LerpScale(Vector3 target)
    {
        Vector3 start   = transform.localScale;
        float   elapsed = 0f;

        while (elapsed < scaleDuration)
        {
            elapsed             += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, target, elapsed / scaleDuration);
            yield return null;
        }

        transform.localScale = target;
    }

    private IEnumerator LerpColor(Color target)
    {
        Color start   = label.color;
        float elapsed = 0f;

        while (elapsed < colorDuration)
        {
            elapsed    += Time.deltaTime;
            label.color = Color.Lerp(start, target, elapsed / colorDuration);
            yield return null;
        }

        label.color = target;
    }
}
