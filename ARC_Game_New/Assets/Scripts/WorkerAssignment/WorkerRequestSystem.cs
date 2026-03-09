using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class WorkerRequestSystem : MonoBehaviour
{
    [Header("UI References")]
    public Button requestUntrainedButton;
    public Button requestTrainedButton;
    
    [Header("Request Settings - Untrained")]
    public int untrainedWorkerCost = 100;
    public int untrainedArrivalDays = 1;
    
    [Header("Request Settings - Trained")]
    public int trainedWorkerCost = 300;
    public int trainedArrivalDays = 1;

    [Header("System References")]
    public TaskSystem taskSystem;
    public WorkerSystem workerSystem;
    public TaskDetailUI taskDetailUI;

    [Header("Debug")]
    public bool showDebugInfo = true;

    [Header("Task Tracking")]
    private GameTask currentUntrainedRequestTask = null;
    private GameTask currentTrainedRequestTask = null;

    // Track request tasks
    private List<RequestTask> activeRequestTasks = new List<RequestTask>();
    
    public static WorkerRequestSystem Instance { get; private set; }

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
        if (requestUntrainedButton != null)
            requestUntrainedButton.onClick.AddListener(OnRequestUntrainedButtonClicked);
        
        if (requestTrainedButton != null)
            requestTrainedButton.onClick.AddListener(OnRequestTrainedButtonClicked);
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
        // Clear reference if request task expired
        if (task.taskTitle == "Request Untrained Responders" && currentUntrainedRequestTask == task)
        {
            currentUntrainedRequestTask = null;
            if (showDebugInfo)
                Debug.Log("Untrained request task expired - cleared reference");
        }
        else if (task.taskTitle == "Request Trained Responders" && currentTrainedRequestTask == task)
        {
            currentTrainedRequestTask = null;
            if (showDebugInfo)
                Debug.Log("Trained request task expired - cleared reference");
        }
    }
    
    public void OnRequestUntrainedButtonClicked()
    {
        if (workerSystem == null)
            workerSystem = WorkerSystem.Instance;

        if (workerSystem == null)
        {
            Debug.LogError("WorkerSystem not found!");
            return;
        }

        // Check if there's already an active untrained request task
        if (currentUntrainedRequestTask != null &&
            TaskSystem.Instance.GetAllActiveTasks().Contains(currentUntrainedRequestTask))
        {
            // Open existing task
            if (taskDetailUI != null)
            {
                taskDetailUI.ShowTaskDetail(currentUntrainedRequestTask);
                if (showDebugInfo)
                    Debug.Log("Reopened existing untrained worker request task");
            }
            return;
        }
        
        // Create untrained worker request task
        CreateWorkerRequestTask(WorkerType.Untrained);
    }
    
    public void OnRequestTrainedButtonClicked()
    {
        if (workerSystem == null)
            workerSystem = WorkerSystem.Instance;

        if (workerSystem == null)
        {
            Debug.LogError("WorkerSystem not found!");
            return;
        }

        // Check if there's already an active trained request task
        if (currentTrainedRequestTask != null &&
            TaskSystem.Instance.GetAllActiveTasks().Contains(currentTrainedRequestTask))
        {
            // Open existing task
            if (taskDetailUI != null)
            {
                taskDetailUI.ShowTaskDetail(currentTrainedRequestTask);
                if (showDebugInfo)
                    Debug.Log("Reopened existing trained worker request task");
            }
            return;
        }
        
        // Create trained worker request task
        CreateWorkerRequestTask(WorkerType.Trained);
    }
    
    void CreateWorkerRequestTask(WorkerType workerType)
    {
        if (taskSystem == null)
            taskSystem = TaskSystem.Instance;
        
        bool isUntrained = (workerType == WorkerType.Untrained);
        string workerTypeLabel = isUntrained ? "Untrained" : "Trained";
        int costPerWorker = isUntrained ? untrainedWorkerCost : trainedWorkerCost;
        int arrivalDays = isUntrained ? untrainedArrivalDays : trainedArrivalDays;
        
        GameTask requestTask = taskSystem.CreateTask(
            $"Request {workerTypeLabel} Responders",
            TaskType.Other,
            "Worker Management",
            $"Request additional {workerTypeLabel.ToLower()} responders to expand your workforce capacity."
        );
        
        // Set timing
        requestTask.roundsRemaining = 5;
        requestTask.taskOfficer = TaskOfficer.WorkforceService;
        
        // Add agent messages
        if (isUntrained)
        {
            requestTask.agentMessages.Add(new AgentMessage(
                "We can recruit additional untrained volunteers from the community. They won't be as efficient as trained responders, but they're available quickly and cost less.",
                taskSystem.workforceServiceSprite
            ));
            
            requestTask.agentMessages.Add(new AgentMessage(
                $"Each untrained responder costs ${costPerWorker} and will arrive in {arrivalDays} day(s). They provide 1 workforce point each and can be trained later for better efficiency.",
                taskSystem.workforceServiceSprite
            ));
        }
        else
        {
            requestTask.agentMessages.Add(new AgentMessage(
                "We can recruit experienced, trained responders from neighboring regions. They're ready to work immediately at full efficiency.",
                taskSystem.workforceServiceSprite
            ));
            
            requestTask.agentMessages.Add(new AgentMessage(
                $"Each trained responder costs ${costPerWorker} and will arrive in {arrivalDays} day(s). They provide 2 workforce points and can be assigned to facilities immediately upon arrival.",
                taskSystem.workforceServiceSprite
            ));
        }
        
        requestTask.agentMessages.Add(new AgentMessage(
            $"How many {workerTypeLabel.ToLower()} responders would you like to request?",
            taskSystem.workforceServiceSprite
        ));

        AgentNumericalInput workerCountInput = new AgentNumericalInput(
            1,                                          // inputId
            isUntrained ? NumericalInputType.UntrainedWorkers : NumericalInputType.TrainedWorkers,
            1,                                          // currentValue (default to 1)
            0,                                          // minValue
            20                                          // maxValue (reasonable cap)
        );
        
        workerCountInput.inputLabel = $"{workerTypeLabel} Responders to Request";
        workerCountInput.customDescription = $"Select how many {workerTypeLabel.ToLower()} responders to request (${costPerWorker} per worker, {arrivalDays} days)";

        requestTask.numericalInputs.Add(workerCountInput);

        // Store reference to current task
        if (isUntrained)
            currentUntrainedRequestTask = requestTask;
        else
            currentTrainedRequestTask = requestTask;
        
        // Show task detail immediately
        if (taskDetailUI != null)
        {
            taskDetailUI.ShowTaskDetail(requestTask);
        }
        
        if (showDebugInfo)
            Debug.Log($"Created {workerTypeLabel.ToLower()} worker request task");
    }

    void OnTaskCompleted(GameTask task)
    {
        // Check if this is a worker request task
        bool isUntrainedRequest = task.taskTitle == "Request Untrained Responders";
        bool isTrainedRequest = task.taskTitle == "Request Trained Responders";
        
        if (!isUntrainedRequest && !isTrainedRequest)
            return;

        // Clear current task reference when completed
        if (isUntrainedRequest && currentUntrainedRequestTask == task)
            currentUntrainedRequestTask = null;
        else if (isTrainedRequest && currentTrainedRequestTask == task)
            currentTrainedRequestTask = null;
        
        // Get the number of workers to request from numerical input
        if (task.numericalInputs.Count == 0)
        {
            Debug.LogError("No numerical input found in worker request task!");
            return;
        }
        
        int workersToRequest = task.numericalInputs[0].currentValue;
        
        if (workersToRequest <= 0)
        {
            //ToastManager.ShowToast("No responders selected for request", ToastType.Warning, true);
            return;
        }
        
        WorkerType workerType = isUntrainedRequest ? WorkerType.Untrained : WorkerType.Trained;
        int costPerWorker = isUntrainedRequest ? untrainedWorkerCost : trainedWorkerCost;
        
        // Calculate cost
        int totalCost = workersToRequest * costPerWorker;
        
        // Check budget
        if (SatisfactionAndBudget.Instance == null || !SatisfactionAndBudget.Instance.CanAfford(totalCost))
        {
            //ToastManager.ShowToast($"Insufficient budget! Need ${totalCost}", ToastType.Warning, true);
            GameLogPanel.Instance.LogError($"Cannot afford worker request: ${totalCost}");
            return;
        }
        
        // Deduct budget
        SatisfactionAndBudget.Instance.RemoveBudget(totalCost, $"Requesting {workersToRequest} {workerType.ToString().ToLower()} responders");

        // Start worker request
        StartWorkerRequest(workersToRequest, workerType);
    }
    
    void StartWorkerRequest(int workerCount, WorkerType workerType)
    {
        bool isUntrained = (workerType == WorkerType.Untrained);
        int arrivalDays = isUntrained ? untrainedArrivalDays : trainedArrivalDays;
        
        // Calculate arrival day
        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        int arrivalDay = currentDay + arrivalDays;
        
        // Create workers with NotArrived status
        List<Worker> incomingWorkers = new List<Worker>();
        
        for (int i = 0; i < workerCount; i++)
        {
            Worker worker;
            if (isUntrained)
            {
                // Untrained workers use NotArrived status
                worker = workerSystem.CreateUntrainedWorker(UntrainedWorkerStatus.NotArrived);
                incomingWorkers.Add(worker);
            }
            else
            {
                // Trained workers use NotArrived status
                worker = workerSystem.CreateTrainedWorker(TrainedWorkerStatus.NotArrived);
                incomingWorkers.Add(worker);
            }
        }
        
        RequestTask requestTask = new RequestTask
        {
            workers = incomingWorkers,
            workerType = workerType,
            workerCount = workerCount,
            requestDay = currentDay,
            arrivalDay = arrivalDay,
            isCompleted = false
        };
        
        activeRequestTasks.Add(requestTask);

        string workerTypeLabel = isUntrained ? "untrained" : "trained";
        ToastManager.ShowToast($"Requested {workerCount} {workerTypeLabel} responders. Estimated Arrival Date: Day {arrivalDay}", ToastType.Success, true);
        GameLogPanel.Instance.LogWorkerAction($"Requested {workerCount} {workerTypeLabel} responders (arrival Day {arrivalDay})");

        if (showDebugInfo)
            Debug.Log($"Worker request started on Day {currentDay} for {workerCount} {workerTypeLabel} responders, arrival day: {arrivalDay}");
    }
    
    void OnDayChanged(int newDay)
    {
        foreach (RequestTask request in activeRequestTasks)
        {
            if (!request.isCompleted && newDay >= request.arrivalDay)
            {
                CompleteWorkerRequest(request);
            }
        }
    }
    
    void CompleteWorkerRequest(RequestTask request)
    {
        int workersArrived = 0;
        bool isUntrained = (request.workerType == WorkerType.Untrained);

        if (isUntrained)
        {
            // Create untrained workers on arrival (they were not pre-created)
            foreach (Worker worker in request.workers)
            {
                if (worker != null && worker.Type == WorkerType.Untrained && worker.GetCurrentStatus() == "NotArrived")
                {
                    worker.SetUntrainedStatus(UntrainedWorkerStatus.Free);
                    workersArrived++;
                }
            }
        }
        else
        {
            // Trained workers: change from NotArrived to Free
            foreach (Worker worker in request.workers)
            {
                if (worker != null && worker.Type == WorkerType.Trained && worker.GetCurrentStatus() == "NotArrived")
                {
                    worker.SetTrainedStatus(TrainedWorkerStatus.Free);
                    workersArrived++;
                }
            }
        }
        
        request.isCompleted = true;
        
        string workerTypeLabel = isUntrained ? "untrained" : "trained";
        Debug.Log($"Worker request completed: {workersArrived} {workerTypeLabel} workers arrived");

        ToastManager.ShowToast($"{workersArrived} {workerTypeLabel} responders have arrived and are ready to work!", ToastType.Success, true);
        GameLogPanel.Instance.LogWorkerAction($"Worker request complete: {workersArrived} {workerTypeLabel} responders arrived");
    }
    
    void OnDestroy()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCompleted -= OnTaskCompleted;
            TaskSystem.Instance.OnTaskExpired -= OnTaskExpired;
        }
        
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
        }
    }

    [System.Serializable]
    public class RequestTask
    {
        public List<Worker> workers;            // For trained workers (created with NotArrived status)
        public WorkerType workerType;
        public int workerCount;
        public int requestDay;
        public int arrivalDay;
        public bool isCompleted = false;
    }
    
    public List<RequestTask> GetActiveRequestTasks()
    {
        return new List<RequestTask>(activeRequestTasks);
    }
}