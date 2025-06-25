using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls individual task entry UI elements
/// </summary>
public class TaskEntryController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image taskIcon;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Button completeButton;
    [SerializeField] private TextMeshProUGUI buttonText;
    
    [Header("Status Colors")]
    [SerializeField] private Color todoColor = Color.white;
    [SerializeField] private Color inProgressColor = Color.yellow;
    [SerializeField] private Color completeColor = Color.green;
    [SerializeField] private Color failedColor = Color.red;
    
    private TaskData _taskData;
    private System.Action<string> _onTaskCompleted;
    
    /// <summary>
    /// Initialize the task entry with data and callback
    /// </summary>
    public void Initialize(TaskData task, System.Action<string> onTaskCompleted)
    {
        _taskData = task;
        _onTaskCompleted = onTaskCompleted;
        
        // Auto-assign UI components if not set in inspector
        if (taskIcon == null)
            taskIcon = GetComponentInChildren<Image>();
        if (titleText == null)
            titleText = transform.Find("TitleText")?.GetComponent<TextMeshProUGUI>();
        if (descriptionText == null)
            descriptionText = transform.Find("DescriptionText")?.GetComponent<TextMeshProUGUI>();
        if (completeButton == null)
            completeButton = GetComponentInChildren<Button>();
        if (buttonText == null && completeButton != null)
            buttonText = completeButton.GetComponentInChildren<TextMeshProUGUI>();
        
        // Set UI elements
        if (taskIcon != null && task.taskIcon != null)
            taskIcon.sprite = task.taskIcon;
        if (titleText != null)
            titleText.text = task.title;
        if (descriptionText != null)
            descriptionText.text = task.description;
        
        // Set button state based on task status
        UpdateButtonState();
        
        // Add listener for complete button
        if (completeButton != null)
        {
            completeButton.onClick.RemoveAllListeners();
            completeButton.onClick.AddListener(OnCompleteButtonClicked);
        }
    }
    
    /// <summary>
    /// Update the visual state based on task status
    /// </summary>
    private void UpdateButtonState()
    {
        if (completeButton == null || buttonText == null) return;
        
        switch (_taskData.status)
        {
            case TaskStatus.Todo:
                buttonText.text = "Complete";
                completeButton.interactable = true;
                SetButtonColor(todoColor);
                break;
            case TaskStatus.InProgress:
                buttonText.text = "In Progress";
                completeButton.interactable = false;
                SetButtonColor(inProgressColor);
                break;
            case TaskStatus.Complete:
                buttonText.text = "Completed";
                completeButton.interactable = false;
                SetButtonColor(completeColor);
                break;
            case TaskStatus.Failed:
                buttonText.text = "Failed";
                completeButton.interactable = false;
                SetButtonColor(failedColor);
                break;
        }
    }
    
    /// <summary>
    /// Set the button color based on status
    /// </summary>
    private void SetButtonColor(Color color)
    {
        if (completeButton != null)
        {
            var colors = completeButton.colors;
            colors.normalColor = color;
            completeButton.colors = colors;
        }
    }
    
    /// <summary>
    /// Called when the complete button is clicked
    /// </summary>
    private void OnCompleteButtonClicked()
    {
        if (_taskData.status == TaskStatus.Todo)
        {
            _onTaskCompleted?.Invoke(_taskData.taskId);
        }
    }
    
    /// <summary>
    /// Update the task status and refresh UI
    /// </summary>
    public void UpdateStatus(TaskStatus newStatus)
    {
        _taskData.status = newStatus;
        UpdateButtonState();
    }
    
    /// <summary>
    /// Get the current task data
    /// </summary>
    public TaskData GetTaskData()
    {
        return _taskData;
    }
    
    /// <summary>
    /// Set task as in progress
    /// </summary>
    public void SetInProgress()
    {
        UpdateStatus(TaskStatus.InProgress);
    }
    
    /// <summary>
    /// Set task as completed
    /// </summary>
    public void SetCompleted()
    {
        UpdateStatus(TaskStatus.Complete);
    }
    
    /// <summary>
    /// Set task as failed
    /// </summary>
    public void SetFailed()
    {
        UpdateStatus(TaskStatus.Failed);
    }
}
