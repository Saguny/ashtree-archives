using UnityEngine;

public class PinBehaviour : Interactable
{
    [Header("Highlight")]
    [SerializeField] Color normalColor = new Color(0.8f, 0.1f, 0.1f, 1f);
    [SerializeField] Color highlightColor = new Color(1f, 0.9f, 0.1f, 1f);

    Renderer _renderer;
    Material _mat;
    CardBehaviour _card;

    public CardBehaviour Card => _card;

    public void Init(CardBehaviour card)
    {
        _card = card;
    }

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            _mat = new Material(_renderer.sharedMaterial);
            _renderer.material = _mat;
            _mat.SetColor("_BaseColor", normalColor);
        }

        promptText = "";
        interactKey = InteractKey.LeftClick;
    }

    public override void OnInteract()
    {
        if (_card == null || !_card.IsPinned) return;

        if (YarnSystem.Instance.IsPending)
            YarnSystem.Instance.TryCompleteYarn(this);
        else
            YarnSystem.Instance.StartYarn(this);
    }

    public override void OnFocus()
    {
        if (_card == null || !_card.IsPinned) return;
        _mat?.SetColor("_BaseColor", highlightColor);
    }

    public override void OnLoseFocus()
    {
        _mat?.SetColor("_BaseColor", normalColor);
    }
}