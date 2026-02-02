using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class WorkerTrainingSystem : MonoBehaviour
{
    [Header("UI References")]
    public Button trainWorkerButton;
    
    [Header("Training Settings")]
    public int trainingCostPerWorker = 500;
    public int trainingDurationDays = 1;
    public int satisfactionPerTrainedWorker = 2;

    [Header("System References")]
    public TaskSystem taskSystem;
    public WorkerSystem workerSystem;
    public TaskDetailUI taskDetailUI;

    [Header("Debug")]
    public bool showDebugInfo = true;

    [Header("Task Tracking")]
    private GameTask currentTrainingTask = null;

    // Track training tasks
    private List<TrainingTask> activeTrainingTasks = new List<TrainingTask>();
    
    public static WorkerTrainingSystem Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        SetupUI();
        SubscribeToEvents();
    }
    
    void SetupUI()
    {
        if (trainWorkerButton != null)
            trainWorkerButton.onClick.AddListener(OnTrainWorkerButtonClicked);
    }

    void SubscribeToEvents()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCompleted += OnTaskCompleted;
            TaskSystem.Instance.OnTaskExpired += OnTaskExpired;
        }

        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
        }
    }
    void OnTaskExpired(GameTask task)
    {
        // Clear reference if training task expired
        if (task.taskTitle == "Worker Training Program" && currentTrainingTask == task)
        {
            currentTrainingTask = null;
            if (showDebugInfo)
                Debug.Log("Training task expired - cleared reference");
        }
    }
    
    public void OnTrainWorkerButtonClicked()
    {
        // Check if we have untrained workers
        if (workerSystem == null)
            workerSystem = WorkerSystem.Instance;

        if (workerSystem == null)
        {
            Debug.LogError("WorkerSystem not found!");
            return;
        }

        // Check if there's already an active training task
        if (currentTrainingTask != null &&
            TaskSystem.Instance.GetAllActiveTasks().Contains(currentTrainingTask))
        {
            // Open existing task
            if (taskDetailUI != null)
            {
                taskDetailUI.ShowTaskDetail(currentTrainingTask);
                if (showDebugInfo)
                    Debug.Log("Reopened existing worker training task");
            }
            return;
        }
        
        int availableUntrained = workerSystem.GetUntrainedWorkersCount();
        
        if (availableUntrained == 0)
        {
            ToastManager.ShowToast("No untrained responders available to train", ToastType.Warning, true);
            return;
        }
        
        // Create training task
        CreateWorkerTrainingTask(availableUntrained);
    }
    
    void CreateWorkerTrainingTask(int maxTrainable)
    {
        if (taskSystem == null)
            taskSystem = TaskSystem.Instance;
            
        GameTask trainingTask = taskSystem.CreateTask(
            "Responder Training Program",
            TaskType.Other,
            "Worker Management",
            $"We have {maxTrainable} untrained responders available. Training them will improve their productivity from 1 to 2 workforce points."
        );
        
        // Set timing
        trainingTask.roundsRemaining = 5;
        trainingTask.taskOfficer = TaskOfficer.WorkforceService;
        
        // Add agent messages
        trainingTask.agentMessages.Add(new AgentMessage(
            "Our volunteers are enthusiastic—but untrained. That might be enough for now, but if you want to make the most out of your limited resources, training is essential.",
            taskSystem.workforceServiceSprite
        ));
        
        trainingTask.agentMessages.Add(new AgentMessage(
            "Trained responders are two times more efficient than untrained ones. That means lower cost per case, less resource waste, and higher overall impact.",
            taskSystem.workforceServiceSprite
        ));
        
        trainingTask.agentMessages.Add(new AgentMessage(
            $"We currently have {maxTrainable} untrained responders who could benefit from training. Training costs ${trainingCostPerWorker} per responder and takes {trainingDurationDays} day(s) to complete. How many responders would you like to train?",
            taskSystem.workforceServiceSprite
        ));

        
        AgentNumericalInput workerCountInput = new AgentNumericalInput(
            1,                                    // inputId
            NumericalInputType.UntrainedWorkers, // inputType
            1,                                    // currentValue (default to 1)
            0,                                    // minValue
            maxTrainable                         // maxValue
        );
        
        // Optional: Override the label and description for this specific context
        workerCountInput.inputLabel = "Responders to Train";
        workerCountInput.customDescription = $"Select how many untrained responders to train (${trainingCostPerWorker} per worker, {trainingDurationDays} days)";

        trainingTask.numericalInputs.Add(workerCountInput);

        // Store reference to current task
        currentTrainingTask = trainingTask;
        
        // Show task detail immediately
        if (taskDetailUI != null)
        {
            taskDetailUI.ShowTaskDetail(trainingTask);
        }
        
        if (showDebugInfo)
            Debug.Log($"Created worker training task with max {maxTrainable} workers");
    }

    void OnTaskCompleted(GameTask task)
    {
        // Check if this is a training task
        if (task.taskTitle != "Responder Training Program")
            return;

        // Clear current task reference when completed
        if (currentTrainingTask == task)
            currentTrainingTask = null;
        
        // Get the number of workers to train from numerical input
        if (task.numericalInputs.Count == 0)
        {
            Debug.LogError("No numerical input found in training task!");
            return;
        }
        
        int workersToTrain = task.numericalInputs[0].currentValue;
        
        if (workersToTrain <= 0)
        {
            ToastManager.ShowToast("No responders selected for training", ToastType.Warning, true);
            return;
        }
        
        // Calculate cost
        int totalCost = workersToTrain * trainingCostPerWorker;
        
        // Check budget
        if (SatisfactionAndBudget.Instance == null || !SatisfactionAndBudget.Instance.CanAfford(totalCost))
        {
            ToastManager.ShowToast($"Insufficient budget! Need ${totalCost}", ToastType.Warning, true);
            GameLogPanel.Instance.LogError($"Cannot afford responder training: ${totalCost}");
            return;
        }
        
        // Deduct budget
        SatisfactionAndBudget.Instance.RemoveBudget(totalCost, $"Training {workersToTrain} responders");

        // Start training
        StartWorkerTraining(workersToTrain);
    }
    
    void StartWorkerTraining(int workerCount)
    {
        // Get FREE untrained workers only
        List<Worker> untrainedWorkers = workerSystem.GetWorkersByType(WorkerType.Untrained)
            .FindAll(w => w.GetCurrentStatus() == "Free");
        
        if (untrainedWorkers.Count < workerCount)
        {
            Debug.LogError($"Not enough free untrained responders! Requested: {workerCount}, Available: {untrainedWorkers.Count}");
            ToastManager.ShowToast($"Not enough free untrained responders available", ToastType.Warning, true);
            return;
        }
        
        // Set workers to training status
        List<Worker> trainingWorkers = new List<Worker>();
        for (int i = 0; i < workerCount; i++)
        {
            Worker worker = untrainedWorkers[i];
            worker.SetUntrainedStatus(UntrainedWorkerStatus.Training);
            trainingWorkers.Add(worker);
        }
        
        // If training duration is 1 day, and we start on day 1, it completes on day 2
        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        int completionDay = currentDay + trainingDurationDays;
        
        TrainingTask trainingTask = new TrainingTask
        {
            workers = trainingWorkers,
            startDay = currentDay,
            completionDay = completionDay,
            workerCount = workerCount
        };
        
        activeTrainingTasks.Add(trainingTask);

        ToastManager.ShowToast($"Started training {workerCount} responders. Completion: Day {completionDay}", ToastType.Success, true);
        GameLogPanel.Instance.LogWorkerAction($"Started training {workerCount} responders (completion Day {completionDay})");

        if (showDebugInfo)
            Debug.Log($"Training started on Day {currentDay} for {workerCount} responders, completion day: {completionDay}");
    }
    
    void OnDayChanged(int newDay)
    {
        foreach (TrainingTask training in activeTrainingTasks)
        {
            if (newDay >= training.completionDay)
            {
                CompleteWorkerTraining(training);
            }
        }
        
        // Don't remove completed training - they stay in the list as completed
    }
    
    void CompleteWorkerTraining(TrainingTask training)
    {
        int successfullyTrained = 0;

        foreach (Worker worker in training.workers)
        {
            // Check if worker is still in training status
            if (worker.Type == WorkerType.Untrained && worker.GetCurrentStatus() == "Training")
            {
                // Remove old untrained worker
                workerSystem.RemoveWorker(worker);

                // Create new trained worker with FREE status
                Worker newTrainedWorker = workerSystem.CreateTrainedWorker(TrainedWorkerStatus.Free);

                successfullyTrained++;
            }
        }
        training.isCompleted = true;
        Debug.Log($"Completed training for {successfullyTrained} workers");

        // Add satisfaction for successful training
        if (SatisfactionAndBudget.Instance != null && successfullyTrained > 0)
        {
            SatisfactionAndBudget.Instance.AddSatisfaction(successfullyTrained * satisfactionPerTrainedWorker,
                "Completed training " + successfullyTrained + " responders");

            ToastManager.ShowToast("Satisfaction increased by " + (successfullyTrained * satisfactionPerTrainedWorker) + " for training completion", ToastType.Info, true);
            GameLogPanel.Instance.LogMetricsChange("Satisfaction increased by " + (successfullyTrained * satisfactionPerTrainedWorker) + " for training completion");
        }

        ToastManager.ShowToast("Training complete! " + successfullyTrained + " responders are now trained and available", ToastType.Success, true);
        GameLogPanel.Instance.LogWorkerAction("Training complete: " + successfullyTrained + " responders now trained and free");

    }
    
    void OnDestroy()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCompleted -= OnTaskCompleted;
        }
        
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
        }
    }

    [System.Serializable]
    public class TrainingTask
    {
        public List<Worker> workers;
        public int startDay;
        public int completionDay;
        public int workerCount;
        public bool isCompleted = false;
    }
    
    public List<TrainingTask> GetActiveTrainingTasks()
    {
        return new List<TrainingTask>(activeTrainingTasks);
    }
}