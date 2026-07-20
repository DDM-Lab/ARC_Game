using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Agent Choice UI Component
public class AgentChoiceUI : MonoBehaviour
{
    [Header("UI Components")]
    public Button choiceButton;
    public TextMeshProUGUI choiceText;
    public TextMeshProUGUI descriptionText; // Optional: displays agentReasoning
    public Image selectedIndicator;
    public Button previewButton;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.green;

    [Header("Validation Colors")]
    public Color invalidColor = Color.gray;
    public TextMeshProUGUI validationText;

    [Header("Auto-size")]
    [Tooltip("Card grows to fit its text, clamped between these heights.")]
    public float minCardHeight = 90f;
    public float maxCardHeight = 300f;

    private AgentChoice choice;
    private TaskDetailUI parentUI;
    private TextMeshProUGUI activeText; // the single field holding all card text
    private bool isSelected = false;
    private bool isValid = true;
    private string validationMessage = "";

    public void Initialize(AgentChoice agentChoice, TaskDetailUI parent, System.Action<AgentChoice> onPreviewRoute = null)
    {
        if (agentChoice == null)
        {
            Debug.LogError("[AgentChoiceUI] Initialize called with null agentChoice");
            return;
        }

        choice = agentChoice;
        parentUI = parent;

        // The prefab has two text fields (a title + a description) pinned to the
        // SAME anchored position, so they render on top of each other. Rather
        // than fight the layout, present the whole package as ONE block: a bold
        // title header above the grounded cost/outcome + action list. The second
        // field is hidden so nothing can overlap.
        string title = (agentChoice.choiceText ?? "").Trim();
        string body = (agentChoice.agentReasoning ?? "").Trim();
        string combined;
        if (title.Length > 0 && body.Length > 0)
            combined = $"<b>{title}</b>\n{body}";
        else
            combined = title.Length > 0 ? $"<b>{title}</b>" : body;

        // Prefer the description field (it already flows multi-line text); fall
        // back to the title field if that's the only one wired up.
        TextMeshProUGUI primary = descriptionText != null ? descriptionText : choiceText;
        TextMeshProUGUI secondary = descriptionText != null ? choiceText : descriptionText;
        if (primary != null)
        {
            primary.text = combined;
            primary.gameObject.SetActive(combined.Length > 0);
        }
        activeText = primary;
        if (secondary != null)
        {
            secondary.text = "";
            secondary.gameObject.SetActive(false);
        }

        if (choiceButton != null)
        {
            choiceButton.onClick.RemoveAllListeners();
            choiceButton.onClick.AddListener(OnChoiceClicked);
        }

        if (previewButton != null)
        {
            previewButton.onClick.RemoveAllListeners();
            bool hasDelivery = agentChoice.triggersDelivery || agentChoice.immediateDelivery;
            bool hasHandler  = parent != null || onPreviewRoute != null;
            // Immediate food-pack delivery comes from an external source, not a mapped route
            bool isImmediateFoodOrder = agentChoice.immediateDelivery && agentChoice.deliveryCargoType == ResourceType.FoodPacks;
            bool showPreview = hasDelivery && hasHandler && !isImmediateFoodOrder;
            Debug.Log("AgentChoiceUI:" + choiceText.text + ", isImmediateFoodOrder = " + isImmediateFoodOrder.ToString() + ", showPreview = " + showPreview.ToString());
            previewButton.gameObject.SetActive(showPreview);
            if (showPreview)
            {
                if (onPreviewRoute != null)
                    previewButton.onClick.AddListener(() => onPreviewRoute(choice));
                else
                    previewButton.onClick.AddListener(() => parent.PreviewChoiceRoute(choice));
            }
        }

        SetSelected(false);

        // Grow the card to fit the (variable-length) text so nothing spills
        // off the card onto the map. Deferred one frame so TMP has valid rect
        // widths to measure against.
        StartCoroutine(AutoSizeNextFrame());
    }

    IEnumerator AutoSizeNextFrame()
    {
        yield return null; // let layout settle so rect widths are valid
        AutoSizeToContent();
    }

    /// <summary>
    /// Grow the card to fit its (single, merged) text block, clamped to
    /// [minCardHeight, maxCardHeight]. All card text lives in one field
    /// (activeText) so nothing can overlap. We measure the wrapped text height
    /// straight from TMP, take manual control of the vertical axis (the prefab's
    /// VerticalLayoutGroup didn't reliably position the text), size statLayout /
    /// the beige background / root to the clamped height, and publish a
    /// LayoutElement so the parent conversation VerticalLayoutGroup reserves
    /// exactly one card's height (no gaps / overlap between cards).
    /// </summary>
    void AutoSizeToContent()
    {
        RectTransform rootRT = transform as RectTransform;
        if (rootRT == null || activeText == null) return;

        WrapAndOverflow(activeText);
        activeText.verticalAlignment = VerticalAlignmentOptions.Top; // flow downward

        // Container chain: statLayout (VLG) -> ChoiceSection (beige bg) -> root.
        RectTransform textRT = activeText.rectTransform;
        RectTransform statRT = textRT.parent as RectTransform;
        RectTransform sectionRT = statRT != null ? statRT.parent as RectTransform : null;

        // The inner VerticalLayoutGroup wasn't reliably positioning the text, so
        // take manual control of the vertical axis: disable the VLG and any
        // ContentSizeFitters, and stop the (horizontal) layout groups from
        // driving child heights — but keep them enabled so the icon / text /
        // button stay arranged left-to-right and top-aligned.
        if (statRT != null)
        {
            var vlg = statRT.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = false;
            var csf = statRT.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = false;
        }
        if (sectionRT != null)
        {
            var hlg = sectionRT.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childControlHeight = false;
                hlg.childForceExpandHeight = false;
                hlg.childAlignment = TextAnchor.UpperLeft;
            }
            var csf = sectionRT.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = false;
        }
        var rootHlg = rootRT.GetComponent<HorizontalLayoutGroup>();
        if (rootHlg != null)
        {
            rootHlg.childControlHeight = false;
            rootHlg.childForceExpandHeight = false;
            rootHlg.childAlignment = TextAnchor.UpperLeft;
        }

        // Measure the wrapped text height directly from TMP (independent of any
        // layout group), then clamp the card height.
        float w = textRT.rect.width;
        if (w <= 1f) w = 278f; // prefab text-box width fallback
        float textH = activeText.GetPreferredValues(activeText.text, w, 0f).y;

        const float padY = 26f; // top+bottom breathing room inside the card
        float target = Mathf.Clamp(textH + padY, minCardHeight, maxCardHeight);

        // If the text would exceed the max, ellipsize so it can't spill below.
        if (textH + padY > maxCardHeight)
            activeText.overflowMode = TextOverflowModes.Ellipsis;

        // Pin the text box to the TOP of its container (keep X anchoring) and
        // size it to fill the padded card, so with Top alignment the text flows
        // straight down from the top edge.
        AnchorTopY(textRT);
        textRT.anchoredPosition = new Vector2(textRT.anchoredPosition.x, -padY * 0.5f);
        textRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target - padY);

        // Grow statLayout, the beige background and the root to the target.
        if (statRT != null)
            statRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target);
        if (sectionRT != null)
            sectionRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target);
        rootRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target);

        // Publish to the parent conversation VerticalLayoutGroup so it reserves
        // exactly one card's height (no gaps / overlap between cards).
        LayoutElement le = GetComponent<LayoutElement>();
        if (le == null) le = gameObject.AddComponent<LayoutElement>();
        le.minHeight = target;
        le.preferredHeight = target;

        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRT);
        if (rootRT.parent is RectTransform parentRT)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRT);
    }

    // Re-anchor a rect to the top edge on the Y axis only (X anchoring kept).
    static void AnchorTopY(RectTransform rt)
    {
        rt.anchorMin = new Vector2(rt.anchorMin.x, 1f);
        rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
        rt.pivot = new Vector2(rt.pivot.x, 1f);
    }

    static void WrapAndOverflow(TextMeshProUGUI t)
    {
        if (t == null) return;
        t.enableWordWrapping = true;
        t.overflowMode = TextOverflowModes.Overflow;
    }

    static float PreferredTextHeight(TextMeshProUGUI t)
    {
        if (t == null || string.IsNullOrEmpty(t.text)) return 0f;
        float w = t.rectTransform.rect.width;
        if (w <= 1f) w = 278f; // prefab text-box width fallback
        return t.GetPreferredValues(t.text, w, 0f).y;
    }

    void OnChoiceClicked()
    {
        SetSelected(true);
        parentUI?.OnChoiceSelected(choice);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (selectedIndicator != null)
            selectedIndicator.gameObject.SetActive(selected);

        if (choiceButton != null)
        {
            Image buttonImage = choiceButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = selected ? selectedColor : normalColor;
        }
    }

    public AgentChoice GetChoice() => choice;

    public void SetPreviewVisible(bool visible)
    {
        if (previewButton != null) previewButton.gameObject.SetActive(visible);
    }

    public void SetValidationState(bool valid, string message)
    {
        isValid = valid;
        validationMessage = message;

        // Update button appearance — disable the checkbox for an invalid choice so it can't be
        // selected (a non-interactable Button won't fire OnChoiceClicked), and grey it out.
        if (choiceButton != null)
        {
            choiceButton.interactable = valid;

            Image buttonImage = choiceButton.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = valid ? normalColor : invalidColor;
            }
        }

        // Show the inline reason (e.g. "All routes blocked by flood") in red under the choice.
        if (validationText != null)
        {
            validationText.text = valid ? "" : message;
            validationText.color = Color.red;
            validationText.gameObject.SetActive(!valid);
        }
    }
    
    public void InitializeAsHistorical(AgentChoice choice, bool wasSelected = false)
    {
        Initialize(choice, null); // parent=null disables preview button automatically

        if (choiceButton != null)
        {
            choiceButton.interactable = false;
            Image buttonImage = choiceButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = wasSelected ? selectedColor : Color.gray;
        }

        if (selectedIndicator != null)
            selectedIndicator.gameObject.SetActive(wasSelected);
    }
}