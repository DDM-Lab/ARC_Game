using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Attach to any button (or other UI Graphic). On hover, hides the elements in
/// elementsToHide and shows hoverText. On exit, restores the original state.
/// </summary>
public class HoverSwapDisplay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("Elements to hide while hovering")]
    public List<GameObject> elementsToHide = new List<GameObject>();

    [Tooltip("TMP text to show while hovering")]
    public TextMeshProUGUI hoverText;

    [Tooltip("Text to display while hovering")]
    public string hoverMessage = "";

    private bool[] originalActiveStates;

    void Awake()
    {
        if (hoverText != null)
            hoverText.gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        originalActiveStates = new bool[elementsToHide.Count];
        for (int i = 0; i < elementsToHide.Count; i++)
        {
            if (elementsToHide[i] == null) continue;
            originalActiveStates[i] = elementsToHide[i].activeSelf;
            elementsToHide[i].SetActive(false);
        }

        if (hoverText != null)
        {
            if (!string.IsNullOrEmpty(hoverMessage))
                hoverText.text = hoverMessage;
            hoverText.gameObject.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (originalActiveStates != null)
        {
            for (int i = 0; i < elementsToHide.Count; i++)
            {
                if (elementsToHide[i] == null) continue;
                elementsToHide[i].SetActive(originalActiveStates[i]);
            }
        }

        if (hoverText != null)
            hoverText.gameObject.SetActive(false);
    }
}
