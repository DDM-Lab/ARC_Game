using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class WorkerReturnSystem : MonoBehaviour
{
    [Header("UI References")]
    public Button returnWorkerButton;

    [Header("System References")]
    public TaskSystem taskSystem;
    public WorkerSystem workerSystem;
    public TaskDetailUI taskDetailUI;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private GameTask currentReturnTask = null;

    public static WorkerReturnSystem Instance { get; private set; }

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
        if (returnWorkerButton != null)
            returnWorkerButton.onClick.AddListener(OnReturnWorkerButtonClicked);
    }

    void SubscribeToEvents()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCompleted += OnTaskCompleted;
            TaskSystem.Instance.OnTaskExpired += OnTaskExpired;
        }
    }

    void OnTaskExpired(GameTask task)
    {
        if (task == currentReturnTask)
        {
            currentReturnTask = null;
            if (showDebugInfo)
                Debug.Log("Worker return task expired - cleared reference");
        }
    }

    public void OnReturnWorkerButtonClicked()
    {
        if (workerSystem == null)
            workerSystem = WorkerSystem.Instance;

        if (workerSystem == null)
        {
            Debug.LogError("WorkerSystem not found!");
            return;
        }

        if (currentReturnTask != null &&
            TaskSystem.Instance.GetAllActiveTasks().Contains(currentReturnTask))
        {
            if (taskDetailUI != null)
            {
                taskDetailUI.ShowTaskDetail(currentReturnTask);
                if (showDebugInfo)
                    Debug.Log("Reopened existing worker return task");
            }
            return;
        }

        int freeUntrained = workerSystem.GetAvailableUntrainedWorkers();
        int freeTrained = workerSystem.GetAvailableTrainedWorkers();

        if (freeUntrained == 0 && freeTrained == 0)
        {
            ToastManager.ShowToast("No free workers available to send back", ToastType.Warning, true);
            return;
        }

        CreateWorkerReturnTask(freeUntrained, freeTrained);
    }

    void CreateWorkerReturnTask(int maxUntrained, int maxTrained)
    {
        if (taskSystem == null)
            taskSystem = TaskSystem.Instance;

        GameTask returnTask = taskSystem.CreateTask(
            "Return Workers to Other Regions",
            TaskType.Other,
            "Worker Management",
            "Send idle workers back to other regions where they are needed most."
        );

        returnTask.roundsRemaining = 5;
        returnTask.taskOfficer = TaskOfficer.WorkforceService;

        returnTask.agentMessages.Add(new AgentMessage(
            "Making the most of every worker's time is critical — an idle worker is a wasted opportunity. Every day they sit unassigned is capacity that could be actively helping survivors somewhere else.",
            taskSystem.workforceServiceSprite
        ));

        returnTask.agentMessages.Add(new AgentMessage(
            "Don't let workers sit idle. If you have more capacity than you currently need, send them back to other regions — the wider relief effort depends on it. You can always request more when demand picks up.",
            taskSystem.workforceServiceSprite
        ));

        returnTask.agentMessages.Add(new AgentMessage(
            $"You currently have {maxUntrained} free untrained and {maxTrained} free trained workers. Only free workers can be returned. How many would you like to send back?",
            taskSystem.workforceServiceSprite
        ));

        AgentNumericalInput untrainedInput = new AgentNumericalInput(
            1,
            NumericalInputType.UntrainedWorkers,
            0,
            0,
            maxUntrained
        );
        untrainedInput.inputLabel = "Untrained Workers to Return";
        untrainedInput.customDescription = $"Select how many untrained workers to send back (0–{maxUntrained} available)";
        returnTask.numericalInputs.Add(untrainedInput);

        AgentNumericalInput trainedInput = new AgentNumericalInput(
            2,
            NumericalInputType.TrainedWorkers,
            0,
            0,
            maxTrained
        );
        trainedInput.inputLabel = "Trained Workers to Return";
        trainedInput.customDescription = $"Select how many trained workers to send back (0–{maxTrained} available)";
        returnTask.numericalInputs.Add(trainedInput);

        currentReturnTask = returnTask;

        if (taskDetailUI != null)
            taskDetailUI.ShowTaskDetail(returnTask);

        if (showDebugInfo)
            Debug.Log($"Created worker return task: up to {maxUntrained} untrained, {maxTrained} trained");
    }

    void OnTaskCompleted(GameTask task)
    {
        if (task.taskTitle != "Return Workers to Other Regions")
            return;

        if (currentReturnTask == task)
            currentReturnTask = null;

        if (task.numericalInputs.Count < 2)
        {
            Debug.LogError("Worker return task is missing numerical inputs!");
            return;
        }

        int untrainedToReturn = task.numericalInputs[0].currentValue;
        int trainedToReturn = task.numericalInputs[1].currentValue;

        if (untrainedToReturn <= 0 && trainedToReturn <= 0)
            return;

        ReturnWorkers(untrainedToReturn, trainedToReturn);
    }

    void ReturnWorkers(int untrainedCount, int trainedCount)
    {
        if (workerSystem == null)
            workerSystem = WorkerSystem.Instance;

        int actualUntrained = 0;
        int actualTrained = 0;

        if (untrainedCount > 0)
        {
            List<Worker> freeUntrained = workerSystem.GetWorkersByType(WorkerType.Untrained)
                .FindAll(w => w.GetCurrentStatus() == "Free");

            int toRemove = Mathf.Min(untrainedCount, freeUntrained.Count);
            for (int i = 0; i < toRemove; i++)
            {
                workerSystem.RemoveWorker(freeUntrained[i]);
                actualUntrained++;
            }
        }

        if (trainedCount > 0)
        {
            List<Worker> freeTrained = workerSystem.GetWorkersByType(WorkerType.Trained)
                .FindAll(w => w.GetCurrentStatus() == "Free");

            int toRemove = Mathf.Min(trainedCount, freeTrained.Count);
            for (int i = 0; i < toRemove; i++)
            {
                workerSystem.RemoveWorker(freeTrained[i]);
                actualTrained++;
            }
        }

        FindObjectOfType<GlobalWorkerManagementUI>()?.RefreshCurrentTab();

        string summary = BuildCountString(actualUntrained, actualTrained);
        ToastManager.ShowToast($"Sending {summary} to other regions.", ToastType.Success, true);
        GameLogPanel.Instance.LogWorkerAction($"Returned {summary} to other regions.");

        if (showDebugInfo)
            Debug.Log($"Returned {actualUntrained} untrained and {actualTrained} trained workers to other regions");
    }

    string BuildCountString(int untrained, int trained)
    {
        if (untrained > 0 && trained > 0)
            return $"{untrained} untrained and {trained} trained workers";
        if (untrained > 0)
            return $"{untrained} untrained workers";
        return $"{trained} trained workers";
    }

    void OnDestroy()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCompleted -= OnTaskCompleted;
            TaskSystem.Instance.OnTaskExpired -= OnTaskExpired;
        }
    }
}
