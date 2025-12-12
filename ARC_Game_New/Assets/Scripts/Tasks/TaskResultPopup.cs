using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TaskResultPopup : MonoBehaviour
{
    [Header("UI References")]
    public Image taskImage;
    public Image taskTypeIcon;
    public TextMeshProUGUI taskTitleText;
    public TextMeshProUGUI facilityText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI reasonText;
    public Button closeButton;
    public Button viewDetailsButton;
    
    [Header("Task Type Icons")]
    public Sprite emergencyIcon;
    public Sprite demandIcon;
    public Sprite advisoryIcon;
    public Sprite alertIcon;
    
    [Header("Result Colors")]
    public Color completedColor = new Color(0.2f, 0.8f, 0.2f);
    public Color expiredColor = new Color(0.9f, 0.6f, 0.2f);
    public Color incompleteColor = new Color(0.9f, 0.2f, 0.2f);
    
    private GameTask currentTask;
    
    void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => Destroy(gameObject));
        
        if (viewDetailsButton != null)
            viewDetailsButton.onClick.AddListener(OnViewDetailsClicked);
    }
    
    public void Initialize(GameTask task, string reason = "")
    {
        currentTask = task;
        
        if (taskImage != null)
            taskImage.sprite = task.taskImage;
        
        if (taskTypeIcon != null)
        {
            taskTypeIcon.sprite = task.taskType switch
            {
                TaskType.Emergency => emergencyIcon,
                TaskType.Demand => demandIcon,
                TaskType.Advisory => advisoryIcon,
                TaskType.Alert => alertIcon,
                _ => null
            };
        }
        
        if (taskTitleText != null)
            taskTitleText.text = task.taskTitle;
        
        if (facilityText != null)
        {
            facilityText.text = task.isGlobalTask ? "Global Task" : task.affectedFacility;
        }
        
        if (resultText != null)
        {
            string resultLabel = task.status switch
            {
                TaskStatus.Completed => "COMPLETED",
                TaskStatus.Expired => "EXPIRED",
                TaskStatus.Incomplete => "INCOMPLETE",
                _ => "UNKNOWN"
            };
            
            resultText.text = resultLabel;
            resultText.color = task.status switch
            {
                TaskStatus.Completed => completedColor,
                TaskStatus.Expired => expiredColor,
                TaskStatus.Incomplete => incompleteColor,
                _ => Color.white
            };
        }
        
        if (reasonText != null)
        {
            if (string.IsNullOrEmpty(reason))
            {
                reason = GetDefaultReason(task);
            }
            reasonText.text = reason;
        }
    }
    
    string GetDefaultReason(GameTask task)
    {
        switch (task.status)
        {
            case TaskStatus.Completed:
                return "Task completed successfully.";
            
            case TaskStatus.Expired:
                if (task.roundsRemaining <= 0)
                    return "Task expired - ran out of time.";
                else if (task.hasRealTimeLimit && task.realTimeRemaining <= 0)
                    return "Task expired - real-time limit reached.";
                else
                    return "Task expired.";
            
            case TaskStatus.Incomplete:
                if (task.linkedDeliveryTaskIds != null && task.linkedDeliveryTaskIds.Count > 0)
                    return "Task incomplete - delivery failed.";
                else
                    return "Task incomplete - requirements not met.";
            
            default:
                return "";
        }
    }
    
    void OnViewDetailsClicked()
    {
        if (currentTask == null)
        {
            Debug.LogWarning("No task to view!");
            return;
        }
        
        TaskDetailUI taskDetailUI = FindObjectOfType<TaskDetailUI>();
        if (taskDetailUI != null)
        {
            taskDetailUI.ShowTaskDetail(currentTask);
        }
        else
        {
            Debug.LogWarning("TaskDetailUI not found!");
        }
        
        Destroy(gameObject);
    }
}