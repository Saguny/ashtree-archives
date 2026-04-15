using UnityEngine;
using UnityEngine.UI;

public class HotbarUI : MonoBehaviour
{
    [Header("Slot Visuals")]
    [SerializeField] Image[] slots;
    [SerializeField] Color emptyColor = new Color(0.1f, 0.1f, 0.1f, 0.6f);
    [SerializeField] Color filledColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
    [SerializeField] Color selectedColor = new Color(0.9f, 0.7f, 0.1f, 0.9f);

    void OnEnable() => GameEvents.OnHotbarChanged += OnHotbarChanged;
    void OnDisable() => GameEvents.OnHotbarChanged -= OnHotbarChanged;

    void OnHotbarChanged(CardBehaviour[] cards, int selected)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (i == selected)
                slots[i].color = selectedColor;
            else if (cards[i] != null)
                slots[i].color = filledColor;
            else
                slots[i].color = emptyColor;
        }
    }
}