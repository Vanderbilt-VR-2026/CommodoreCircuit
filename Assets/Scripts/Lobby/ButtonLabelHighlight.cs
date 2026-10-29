using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Recolours a button's label while the laser or a finger is over it, so it's clear what the trigger will press.
public class ButtonLabelHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Color HighlightColor = new Color32(0xCF, 0xAE, 0x70, 0xFF); // Vanderbilt gold
    private TMP_Text label;
    private Color normalColor;

    private void Awake()
    {
        label = GetComponentInChildren<TMP_Text>();
        normalColor = label.color;
    }

    public void OnPointerEnter(PointerEventData eventData) => label.color = HighlightColor;
    public void OnPointerExit(PointerEventData eventData) => label.color = normalColor;

    // Panels swap while a button is hovered (e.g. Join → keypad), so reset or it comes back still gold.
    private void OnDisable() => label.color = normalColor;
}
