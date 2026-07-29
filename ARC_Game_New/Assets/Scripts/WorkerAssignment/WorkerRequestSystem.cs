using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class WorkerRequestSystem : MonoBehaviour
{
    [Header("UI References")]
    public Button requestWorkersButton;

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

    private GameTask currentRequestTask = null;

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
        if (requestWorkersButton != null)
            requestWorkersButton.onClick.AddListener(OnRequestWorkersButtonClicked);
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
        if (task == currentRequestTask)
        {
            currentRequestTask = null;
            if (showDebugInfo)
                Debug.Log("Worker request task expired - cleared reference");
        }
    }

    public void OnRequestWorkersButtonClicked()
    {
        if (workerSystem == null)
            workerSystem = WorkerSystem.Instance;

        if (workerSystem == null)
        {
            Debug.LogError("WorkerSystem not found!");
            return;
        }

        if (currentRequestTask != null &&
            TaskSystem.Instance.GetAllActiveTasks().Contains(currentRequestTask))
        {
            if (taskDetailUI != null)
            {
                taskDetailUI.ShowTaskDetail(currentRequestTask);
                if (showDebugInfo)
                    Debug.Log("Reopened existing worker request task");
            }
            return;
        }

        CreateWorkerRequestTask();
    }

    void CreateWorkerRequestTask()
    {
        if (taskSystem == null)
            taskSystem = TaskSystem.Instance;

        GameTask requestTask = taskSystem.CreateTask(
            "Request Additional Workers",
            TaskType.Other,
            "Worker Management",
            "Request untrained or trained workers from other regions to expand your workforce."
        );

        requestTask.roundsRemaining = 5;
        requestTask.taskOfficer = TaskOfficer.WorkforceService;

        requestTask.agentMessages.Add(new AgentMessage(
            "We can bring in additional workers from other regions. Untrained workers are cheaper and arrive quickly, but each only contributes 1 workforce point. Trained workers cost more, but hit the ground running at 2 workforce points each.",
            taskSystem.workforceServiceSprite
        ));

        requestTask.agentMessages.Add(new AgentMessage(
            $"Untrained: ${untrainedWorkerCost}/worker, arrives in {untrainedArrivalDays} {(untrainedArrivalDays == 1 ? "day" : "days")}. Trained: ${trainedWorkerCost}/worker, arrives in {trainedArrivalDays} {(trainedArrivalDays == 1 ? "day" : "days")}. How many of each would you like to request?",
            taskSystem.workforceServiceSprite
        ));

        //AgentNumericalInput untrainedInput = new AgentNumericalInput(
        //    1,
        //    NumericalInputType.UntrainedWorkers,
        //    0,
        //    0,
        //    20
        //);
        //untrainedInput.inputLabel = "Untrained Workers to Request";
        //untrainedInput.customDescription = $"${untrainedWorkerCost}/worker · arrives in {untrainedArrivalDays} {(untrainedArrivalDays == 1 ? "day" : "days")} · 1 workforce each";
        //requestTask.numericalInputs.Add(untrainedInput);

        //AgentNumericalInput trainedInput = new AgentNumericalInput(
        //    2,
        //    NumericalInputType.TrainedWorkers,
        //    0,
        //    0,
        //    20
        //);
        //trainedInput.inputLabel = "Trained Workers to Request";
        //trainedInput.customDescription = $"${trainedWorkerCost}/worker · arrives in {trainedArrivalDays} {(trainedArrivalDays == 1 ? "day" : "days")} · 2 workforce each";
        //requestTask.numericalInputs.Add(trainedInput);
        AgentNumericalInput trainedInput = new AgentNumericalInput(
            1, // Updated ID to 1
            NumericalInputType.TrainedWorkers,
            0,
            0,
            20
        );
        trainedInput.inputLabel = "Trained Workers to Request";
        trainedInput.customDescription = $"${trainedWorkerCost}/worker · arrives in {trainedArrivalDays} {(trainedArrivalDays == 1 ? "day" : "days")} · 2 workforce each";
        requestTask.numericalInputs.Add(trainedInput);

        AgentNumericalInput untrainedInput = new AgentNumericalInput(
            2, // Updated ID to 2
            NumericalInputType.UntrainedWorkers,
            0,
            0,
            20
        );
        untrainedInput.inputLabel = "Untrained Workers to Request";
        untrainedInput.customDescription = $"${untrainedWorkerCost}/worker · arrives in {untrainedArrivalDays} {(untrainedArrivalDays == 1 ? "day" : "days")} · 1 workforce each";
        requestTask.numericalInputs.Add(untrainedInput);

        currentRequestTask = requestTask;

        if (taskDetailUI != null)
            taskDetailUI.ShowTaskDetail(requestTask);

        if (showDebugInfo)
            Debug.Log("Created combined worker request task");
    }

    void OnTaskCompleted(GameTask task)
    {
        if (task.taskTitle != "Request Additional Workers")
            return;

        if (currentRequestTask == task)
            currentRequestTask = null;

        if (task.numericalInputs.Count < 2)
        {
            Debug.LogError("Worker request task is missing numerical inputs!");
            return;
        }

        //int untrainedToRequest = task.numericalInputs[0].currentValue;
        //int trainedToRequest = task.numericalInputs[1].currentValue;
        int trainedToRequest = task.numericalInputs[0].currentValue;
        int untrainedToRequest = task.numericalInputs[1].currentValue;

        if (untrainedToRequest <= 0 && trainedToRequest <= 0)
            return;

        int totalCost = (untrainedToRequest * untrainedWorkerCost) + (trainedToRequest * trainedWorkerCost);

        // Check budget (honors the no-debt policy; allows overspend when allowNegativeBudget is on)
        // if (SatisfactionAndBudget.Instance == null || !SatisfactionAndBudget.Instance.WouldAllowSpend(totalCost))
        // {
        //     GameLogPanel.Instance.LogError($"Cannot afford worker request: ${totalCost}");
        //     return;
        // }
        if (SatisfactionAndBudget.Instance == null)
        {
            return;
        }

        SatisfactionAndBudget.Instance.RemoveBudget(totalCost, SatisfactionAndBudget.SpendCategory.Worker, $"Requesting {untrainedToRequest} untrained and {trainedToRequest} trained workers");


        // SatisfactionAndBudget.Instance.RemoveBudget(totalCost, $"Requesting {untrainedToRequest} untrained and {trainedToRequest} trained workers");
        if (DailyReportData.Instance != null)
            DailyReportData.Instance.RecordWorkerRequestCostCumulative(totalCost);

        if (untrainedToRequest > 0)
            StartWorkerRequest(untrainedToRequest, WorkerType.Untrained);
        if (trainedToRequest > 0)
            StartWorkerRequest(trainedToRequest, WorkerType.Trained);
    }

    public void StartWorkerRequest(int workerCount, WorkerType workerType)
    {
        bool isUntrained = (workerType == WorkerType.Untrained);
        int arrivalDays = isUntrained ? untrainedArrivalDays : trainedArrivalDays;

        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        int arrivalDay = currentDay + arrivalDays;

        List<Worker> incomingWorkers = new List<Worker>();

        for (int i = 0; i < workerCount; i++)
        {
            Worker worker = isUntrained
                ? workerSystem.CreateUntrainedWorker(UntrainedWorkerStatus.NotArrived)
                : workerSystem.CreateTrainedWorker(TrainedWorkerStatus.NotArrived);
            incomingWorkers.Add(worker);
        }

        activeRequestTasks.Add(new RequestTask
        {
            workers = incomingWorkers,
            workerType = workerType,
            workerCount = workerCount,
            requestDay = currentDay,
            arrivalDay = arrivalDay,
            isCompleted = false
        });

        FindObjectOfType<GlobalWorkerManagementUI>()?.RefreshCurrentTab();

        string label = isUntrained ? "untrained" : "trained";
        ToastManager.ShowToast($"Requested {workerCount} {label} workers. Estimated Arrival Date: Day {arrivalDay}", ToastType.Success, true);
        GameLogPanel.Instance?.LogWorkerAction($"Requested {workerCount} {label} workers (arrival Day {arrivalDay})");

        if (showDebugInfo)
            Debug.Log($"Worker request started on Day {currentDay} for {workerCount} {label} workers, arrival day: {arrivalDay}");

        RequestTask newTask = activeRequestTasks[activeRequestTasks.Count - 1];
        DeliveryQueuePanel.Instance?.OnItemAdded(newTask);
    }

    void OnDayChanged(int newDay)
    {
        foreach (RequestTask request in activeRequestTasks)
        {
            if (!request.isCompleted && newDay >= request.arrivalDay)
                CompleteWorkerRequest(request);
        }
    }

    void CompleteWorkerRequest(RequestTask request)
    {
        int workersArrived = 0;
        bool isUntrained = (request.workerType == WorkerType.Untrained);

        foreach (Worker worker in request.workers)
        {
            if (worker == null) continue;

            if (isUntrained && worker.Type == WorkerType.Untrained && worker.GetCurrentStatus() == "NotArrived")
            {
                worker.SetUntrainedStatus(UntrainedWorkerStatus.Free);
                workersArrived++;
            }
            else if (!isUntrained && worker.Type == WorkerType.Trained && worker.GetCurrentStatus() == "NotArrived")
            {
                worker.SetTrainedStatus(TrainedWorkerStatus.Free);
                workersArrived++;
            }
        }

        request.isCompleted = true;

        string label = isUntrained ? "untrained" : "trained";
        Debug.Log($"Worker request completed: {workersArrived} {label} workers arrived");

        ToastManager.ShowToast($"{workersArrived} {label} workers have arrived and are ready to work!", ToastType.Success, true);
        GameLogPanel.Instance?.LogWorkerAction($"Worker request complete: {workersArrived} {label} workers arrived");
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
        public List<Worker> workers;
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
