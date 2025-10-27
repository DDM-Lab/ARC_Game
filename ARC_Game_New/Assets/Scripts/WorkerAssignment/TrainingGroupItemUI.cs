using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TrainingGroupItemUI : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI workersInTrainingText;
    public TextMeshProUGUI dayRangeText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI workforceRewardText;
    public TextMeshProUGUI satisfactionRewardText;
    public Image backgroundImage;

    [Header("Completed State Colors")]
    public Color completedBackgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

    private int trainingCostPerWorker = 500; // Default value, will be updated
    private int satisfactionPerWorker = 2; // Default value, will be updated

    private WorkerTrainingSystem.TrainingTask trainingTask;
    
    public void Initialize(WorkerTrainingSystem.TrainingTask training)
    {
        trainingTask = training;
        UpdateDisplay();
        trainingCostPerWorker = WorkerTrainingSystem.Instance.trainingCostPerWorker;
        satisfactionPerWorker = WorkerTrainingSystem.Instance.satisfactionPerTrainedWorker;
    }
    
    void UpdateDisplay()
    {
        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        int daysRemaining = Mathf.Max(0, trainingTask.completionDay - currentDay);
        bool isCompleted = trainingTask.isCompleted;
        
        // Workers in training
        if (workersInTrainingText != null)
        {
            workersInTrainingText.text = isCompleted ? 
                $"{trainingTask.workerCount} Responders (Completed)" : 
                $"{trainingTask.workerCount} Responders in Training";
            
        }
        
        // Day range with arrival message
        if (dayRangeText != null)
        {
            if (isCompleted)
            {
                dayRangeText.text = $"Day {trainingTask.startDay} → Day {trainingTask.completionDay} (completed)";
            }
            else
            {
                string arrivalMessage = GetArrivalMessage(daysRemaining);
                dayRangeText.text = $"Day {trainingTask.startDay} → Day {trainingTask.completionDay} ({arrivalMessage})";
            }
        }
        
        // Cost
        if (costText != null)
        {
            int totalCost = trainingTask.workerCount * trainingCostPerWorker;
            costText.text = $"Cost: ${totalCost:N0}";
            
        }
        
        // Workforce reward
        if (workforceRewardText != null)
        {
            int workforceGain = trainingTask.workerCount * 2;
            workforceRewardText.text = isCompleted ? 
                $"Workforce gained: +{workforceGain}" : 
                $"+{workforceGain} workforce on complete";
            
        }
        
        // Satisfaction reward
        if (satisfactionRewardText != null)
        {
            int satisfactionGain = trainingTask.workerCount * satisfactionPerWorker;
            satisfactionRewardText.text = isCompleted ? 
                $"Satisfaction gained: +{satisfactionGain}" : 
                $"+{satisfactionGain} satisfaction on complete";

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