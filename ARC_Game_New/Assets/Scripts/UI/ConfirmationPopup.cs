using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class ConfirmationPopup : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject popupPanel;
    [SerializeField] private RectTransform popupPanelRect;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private RectTransform messageTextRect;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    
    [Header("Optional: Titles")]
    [SerializeField] private TextMeshProUGUI titleText;
    
    [Header("Dynamic Sizing")]
    [SerializeField] private float minHeight = 150f;
    [SerializeField] private float maxHeight = 600f;
    [SerializeField] private float paddingTop = 60f;
    [SerializeField] private float paddingBottom = 80f;
    [SerializeField] private float textPadding = 20f;
    
    // Singleton instance
    public static ConfirmationPopup Instance { get; private set; }
    
    // Callbacks for current popup
    private Action onConfirmCallback;
    private Action onCancelCallback;
    
    // Flag for layout updates
    private bool needsLayoutUpdate = false;
    private int updateFrameCount = 0;
    
    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Get RectTransform references if not assigned
        if (popupPanelRect == null && popupPanel != null)
            popupPanelRect = popupPanel.GetComponent<RectTransform>();
        
        if (messageTextRect == null && messageText != null)
            messageTextRect = messageText.GetComponent<RectTransform>();
        
        // Setup button listeners
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmClicked);
        
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);
        
        // Hide popup initially
        HidePopup();
    }
    
    /// <summary>
    /// Show confirmation popup with a message and callbacks
    /// </summary>
    /// <param name="message">The message to display</param>
    /// <param name="onConfirm">Action to execute when Confirm is clicked</param>
    /// <param name="onCancel">Optional action to execute when Cancel is clicked</param>
    /// <param name="title">Optional title for the popup</param>
    public void ShowPopup(string message, Action onConfirm, Action onCancel = null, string title = "Confirm Action")
    {
        // Store callbacks
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        
        // Set text
        if (messageText != null)
            messageText.text = message;
        
        if (titleText != null)
            titleText.text = title;
        
        // Show popup
        if (popupPanel != null)
            popupPanel.SetActive(true);
        
        // Immediate first update
        AdjustPopupHeightImmediate();
        
        // Flag for next few frames update (in case first update wasn't accurate)
        needsLayoutUpdate = true;
        updateFrameCount = 0;
        
        Debug.Log($"Confirmation popup shown: {message}");
    }
    
    void LateUpdate()
    {
        // Update on next few frames regardless of timeScale
        // This ensures proper layout calculation even when game is paused
        if (needsLayoutUpdate)
        {
            AdjustPopupHeightImmediate();
            updateFrameCount++;
            
            // Stop updating after 3 frames (more than enough for layout to stabilize)
            if (updateFrameCount >= 3)
            {
                needsLayoutUpdate = false;
                updateFrameCount = 0;
            }
        }
    }
    
    /// <summary>
    /// Adjust popup height based on text content - immediate calculation
    /// </summary>
    private void AdjustPopupHeightImmediate()
    {
        if (messageText == null || popupPanelRect == null || messageTextRect == null)
        {
            Debug.LogWarning("Cannot adjust popup height - missing references");
            return;
        }
        
        // Force TextMeshPro to update its mesh and layout
        messageText.ForceMeshUpdate(true, true);
        
        // Force Unity's layout system to rebuild
        LayoutRebuilder.ForceRebuildLayoutImmediate(messageTextRect);
        
        // Force canvas update
        Canvas.ForceUpdateCanvases();
        
        // Get preferred text size with width constraint
        Vector2 textSize = messageText.GetPreferredValues(
            messageText.text,
            messageTextRect.rect.width, // Use actual width constraint
            0 // No height constraint
        );
        
        float textHeight = textSize.y;
        
        // Fallback: if GetPreferredValues returns 0, use other methods
        if (textHeight <= 0)
        {
            textHeight = messageText.preferredHeight;
        }
        
        if (textHeight <= 0)
        {
            textHeight = messageText.renderedHeight;
        }
        
        // Last resort fallback
        if (textHeight <= 0)
        {
            textHeight = 100f; // Default reasonable height
            Debug.LogWarning("Could not calculate text height, using default");
        }
        
        // Calculate total required height
        float totalHeight = paddingTop + textHeight + textPadding + paddingBottom;
        
        // Clamp to min/max values
        totalHeight = Mathf.Clamp(totalHeight, minHeight, maxHeight);
        
        // Apply new height
        Vector2 newSize = popupPanelRect.sizeDelta;
        newSize.y = totalHeight;
        popupPanelRect.sizeDelta = newSize;
        
        Debug.Log($"Popup height adjusted to {totalHeight} (text height: {textHeight}, padding top: {paddingTop}, padding bottom: {paddingBottom})");
    }
    
    /// <summary>
    /// Hide the popup without triggering any callbacks
    /// </summary>
    public void HidePopup()
    {
        if (popupPanel != null)
            popupPanel.SetActive(false);
        
        // Clear callbacks
        onConfirmCallback = null;
        onCancelCallback = null;
        
        // Cancel any pending layout updates
        needsLayoutUpdate = false;
        updateFrameCount = 0;
    }
    
    private void OnConfirmClicked()
    {
        Debug.Log("Confirmation popup: Confirm clicked");
        
        // Execute confirm callback
        onConfirmCallback?.Invoke();
        
        // Hide popup
        HidePopup();
    }
    
    private void OnCancelClicked()
    {
        Debug.Log("Confirmation popup: Cancel clicked");
        
        // Execute cancel callback if provided
        onCancelCallback?.Invoke();
        
        // Hide popup
        HidePopup();
    }
    
    void OnDestroy()
    {
        // Clean up button listeners
        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(OnConfirmClicked);
        
        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(OnCancelClicked);
    }
}

/* Example Usage:
public void OnFireWorkerButtonClicked(Worker worker)
{
    if (ConfirmationPopup.Instance != null)
    {
        ConfirmationPopup.Instance.ShowPopup(
            message: $"Are you sure you want to remove Worker {worker.WorkerId}?\n\nThey will return to the worker pool.",
            onConfirm: () => {
                workerSystem.ReleaseWorkersFromBuilding(buildingId);
                RefreshUI();
            },
            onCancel: () => {//optional
                Debug.Log("Worker removal cancelled by user.");
            },
            title: "Remove Worker"
        );
    }
}*/
 