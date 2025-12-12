using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RequestGroupItemUI : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI workersRequestedText;
    public TextMeshProUGUI dayRangeText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI workerTypeText;
    public TextMeshProUGUI workforceGainText;
    public Image backgroundImage;
    public Image workerTypeIcon;
    
    [Header("Worker Type Icons")]  // ADD THIS SECTION
    public Sprite trainedIcon;
    public Sprite untrainedIcon;

    [Header("Completed State Colors")]
    public Color completedBackgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

    [Header("Worker Type Colors")]
    public Color trainedColor = new Color(0.2f, 0.6f, 0.9f, 1f);      // Blue for trained
    public Color untrainedColor = new Color(0.9f, 0.6f, 0.2f, 1f);    // Orange for untrained

    private WorkerRequestSystem.RequestTask requestTask;
    
    public void Initialize(WorkerRequestSystem.RequestTask request)
    {
        requestTask = request;
        UpdateDisplay();
    }
    
    void UpdateDisplay()
    {
        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        int daysRemaining = Mathf.Max(0, requestTask.arrivalDay - currentDay);
        bool isCompleted = requestTask.isCompleted;
        bool isUntrained = (requestTask.workerType == WorkerType.Untrained);
        
        string workerTypeLabel = isUntrained ? "Untrained" : "Trained";
        int costPerWorker = isUntrained ? 
            WorkerRequestSystem.Instance.untrainedWorkerCost : 
            WorkerRequestSystem.Instance.trainedWorkerCost;
        
        // Workers requested
        if (workersRequestedText != null)
        {
            workersRequestedText.text = isCompleted ? 
                $"{requestTask.workerCount} Responders (Arrived)" : 
                $"{requestTask.workerCount} {workerTypeLabel} Responders En Route";
        }
        
        // Day range with arrival message
        if (dayRangeText != null)
        {
            if (isCompleted)
            {
                dayRangeText.text = $"Day {requestTask.requestDay} → Day {requestTask.arrivalDay} (arrived)";
            }
            else
            {
                string arrivalMessage = GetArrivalMessage(daysRemaining);
                dayRangeText.text = $"Day {requestTask.requestDay} → Day {requestTask.arrivalDay} ({arrivalMessage})";
            }
        }
        
        // Cost
        if (costText != null)
        {
            int totalCost = requestTask.workerCount * costPerWorker;
            costText.text = $"Cost: ${totalCost:N0}";
        }
        
        // Worker type
        if (workerTypeText != null)
        {
            workerTypeText.text = $"Type: {workerTypeLabel}";
            workerTypeText.color = isUntrained ? untrainedColor : trainedColor;
        }

        // Worker type icon - ADD THIS SECTION
        if (workerTypeIcon != null)
        {
            workerTypeIcon.sprite = isUntrained ? untrainedIcon : trainedIcon;
            if (workerTypeIcon.sprite != null)
            {
                workerTypeIcon.enabled = true;
            }
        }
        
        // Workforce gain
        if (workforceGainText != null)
        {
            int workforcePerWorker = isUntrained ? 1 : 2;
            int totalWorkforce = requestTask.workerCount * workforcePerWorker;
            
            workforceGainText.text = isCompleted ? 
                $"Workforce gained: +{totalWorkforce}" : 
                $"+{totalWorkforce} workforce on arrival";
        }
        
        // Background color
        if (backgroundImage != null && isCompleted)
        {
            backgroundImage.color = completedBackgroundColor;
        }
    }
    
    string GetArrivalMessage(int daysRemaining)
    {
        switch (daysRemaining)
        {
            case 0:
                return "arrive today";
            case 1:
                return "arrive tomorrow";
            case 2:
                return "arrive in 2 days";
            default:
                return $"arrive in {daysRemaining} days";
        }
    }
}