using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Replaces IndividualBuildingManageUI with a task-panel-based worker assignment flow.
/// Handles first-time assignment, same-round composition changes, and locked (previous-round) changes.
/// </summary>
public class WorkerAssignmentHandler : MonoBehaviour
{
    public static WorkerAssignmentHandler Instance { get; private set; }

    [Header("Debug")]
    public bool showDebugInfo = true;

    // One pending task per building (buildingId → task)
    private Dictionary<int, GameTask> pendingTasks = new Dictionary<int, GameTask>();

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    void Start()
    {
        if (TaskSystem.Instance != null)
            TaskSystem.Instance.OnTaskCompleted += OnTaskCompleted;
    }

    void OnDestroy()
    {
        if (TaskSystem.Instance != null)
            TaskSystem.Instance.OnTaskCompleted -= OnTaskCompleted;
    }

    // ─────────────────────────────────────────────────────────────────
    // ENTRY POINT
    // ─────────────────────────────────────────────────────────────────

    public void OpenForBuilding(Building building)
    {
        if (GlobalClock.Instance != null && !GlobalClock.Instance.CanPlayerInteract())
        {
            ToastManager.ShowToast("Cannot manage workers while simulation is running.", ToastType.Warning, true);
            return;
        }

        int buildingId = building.GetOriginalSiteId();

        // Re-open existing task if one is already open for this building
        if (pendingTasks.TryGetValue(buildingId, out GameTask existing) && existing != null)
        {
            FindObjectOfType<TaskDetailUI>()?.ShowTaskDetail(existing);
            return;
        }

        WorkerSystem ws = WorkerSystem.Instance ?? FindObjectOfType<WorkerSystem>();
        if (ws == null) { Debug.LogError("[WorkerAssignmentHandler] WorkerSystem not found"); return; }

        bool hasWorkers = ws.GetWorkersByBuildingId(buildingId).Count > 0;
        bool isLocked = WorkerAssignmentTracker.Instance != null
            && WorkerAssignmentTracker.Instance.IsLockedForRelease(buildingId);

        GameTask task = BuildTask(building, ws, hasWorkers, isLocked);
        pendingTasks[buildingId] = task;

        FindObjectOfType<TaskDetailUI>()?.ShowTaskDetail(task);

        if (showDebugInfo)
            Debug.Log($"[WorkerAssignmentHandler] Opened task for {building.GetDisplayName()} (hasWorkers={hasWorkers}, locked={isLocked})");
    }

    // ─────────────────────────────────────────────────────────────────
    // TASK CONSTRUCTION
    // ─────────────────────────────────────────────────────────────────

    GameTask BuildTask(Building building, WorkerSystem ws, bool hasWorkers, bool isLocked)
    {
        int buildingId = building.GetOriginalSiteId();
        int required   = building.GetRequiredWorkforce();

        WorkerStatistics stats    = ws.GetWorkerStatistics();
        List<Worker> current      = ws.GetWorkersByBuildingId(buildingId);
        int currentTrained        = current.Count(w => w.Type == WorkerType.Trained);
        int currentUntrained      = current.Count(w => w.Type == WorkerType.Untrained);
        int currentWorkforce      = currentTrained * 2 + currentUntrained;
        int currentHeadCount      = currentTrained + currentUntrained;

        // Current workers will be released on apply, so they return to the available pool
        int availableTrained   = stats.trainedFree + currentTrained;
        int availableUntrained = stats.untrainedFree + currentUntrained;

        string title = hasWorkers
            ? $"Change Worker Composition: {building.GetDisplayName()}"
            : $"Assign Workers: {building.GetDisplayName()}";

        GameTask task = TaskSystem.Instance.CreateTask(
            title, TaskType.Other, "Worker Management",
            $"Manage worker assignment for {building.GetDisplayName()}."
        );
        task.roundsRemaining = 10;
        task.taskOfficer     = TaskOfficer.WorkforceService;
        task.isGlobalTask    = true;

        Sprite icon = TaskSystem.Instance.workforceServiceSprite;

        // ── Message 1: facility overview ──────────────────────────────
        task.agentMessages.Add(new AgentMessage(
            $"Facility: {building.GetDisplayName()} ({building.GetBuildingType()})\n" +
            $"Required workforce: {required}    Currently assigned: {currentWorkforce}/{required}",
            icon
        ));

        // ── Message 2: context-specific guidance ──────────────────────
        if (!hasWorkers)
        {
            task.agentMessages.Add(new AgentMessage(
                $"This facility has no workers yet. Assign exactly {required} workforce points " +
                $"to bring it online.\n" +
                $"Available: {availableTrained} trained (2 pts each) and {availableUntrained} untrained (1 pt each).",
                icon
            ));
        }
        else if (isLocked)
        {
            task.agentMessages.Add(new AgentMessage(
                $"Workers here were assigned in a previous round. They are committed and cannot be released — " +
                $"you may only swap trained for untrained or vice versa, keeping the same total of {currentHeadCount} workers.\n\n" +
                $"⚠ Once a round ends, assigned workers are permanently locked to their facility until the game ends.",
                icon
            ));
        }
        else
        {
            task.agentMessages.Add(new AgentMessage(
                $"Workers were assigned this round, so you can still adjust freely.\n" +
                $"Reminder: once the next round begins, you will not be able to reduce the workforce here.\n" +
                $"Available: {availableTrained} trained and {availableUntrained} untrained (including current assignment).",
                icon
            ));
        }

        // ── Message 3: trained-worker nudge ───────────────────────────
        task.agentMessages.Add(new AgentMessage(
            "Trained workers contribute 2 workforce points each — always prefer them where available " +
            "to stretch your workforce further.",
            icon
        ));

        // ── Numerical inputs ──────────────────────────────────────────
        int maxTrained, maxUntrained;

        if (isLocked)
        {
            // Head count is fixed; player can only swap within the same total
            maxTrained   = Mathf.Min(availableTrained,   currentHeadCount);
            maxUntrained = Mathf.Min(availableUntrained, currentHeadCount);
        }
        else
        {
            maxTrained   = availableTrained;
            maxUntrained = availableUntrained;
        }

        AgentNumericalInput trainedInput = new AgentNumericalInput(
            1, NumericalInputType.TrainedWorkers, 0, 0, maxTrained);
        trainedInput.inputLabel       = "Trained Workers";
        trainedInput.customDescription = $"2 workforce pts each — {availableTrained} available";
        if (hasWorkers) trainedInput.currentValue = currentTrained;
        task.numericalInputs.Add(trainedInput);

        AgentNumericalInput untrainedInput = new AgentNumericalInput(
            2, NumericalInputType.UntrainedWorkers, 0, 0, maxUntrained);
        untrainedInput.inputLabel       = "Untrained Workers";
        untrainedInput.customDescription = $"1 workforce pt each — {availableUntrained} available";
        if (hasWorkers) untrainedInput.currentValue = currentUntrained;
        task.numericalInputs.Add(untrainedInput);

        return task;
    }

    // ─────────────────────────────────────────────────────────────────
    // TASK COMPLETION
    // ─────────────────────────────────────────────────────────────────

    void OnTaskCompleted(GameTask task)
    {
        int buildingId = -1;
        foreach (var kvp in pendingTasks)
        {
            if (kvp.Value == task) { buildingId = kvp.Key; break; }
        }
        if (buildingId == -1) return;

        pendingTasks.Remove(buildingId);

        if (task.numericalInputs.Count < 2)
        {
            Debug.LogError("[WorkerAssignmentHandler] Task missing numerical inputs");
            return;
        }

        Building building = FindObjectsOfType<Building>()
            .FirstOrDefault(b => b.GetOriginalSiteId() == buildingId);
        if (building == null) return;

        WorkerSystem ws = WorkerSystem.Instance ?? FindObjectOfType<WorkerSystem>();
        if (ws == null) return;

        int newTrained   = task.numericalInputs[0].currentValue;
        int newUntrained = task.numericalInputs[1].currentValue;
        int newWorkforce = newTrained * 2 + newUntrained;
        int newHeadCount = newTrained + newUntrained;
        int required     = building.GetRequiredWorkforce();

        List<Worker> current     = ws.GetWorkersByBuildingId(buildingId);
        int currentHeadCount     = current.Count;
        bool isLocked            = WorkerAssignmentTracker.Instance != null
            && WorkerAssignmentTracker.Instance.IsLockedForRelease(buildingId);

        // ── Execute ───────────────────────────────────────────────────
        bool success = ApplyAssignment(ws, building, buildingId, newTrained, newUntrained);

        if (success)
        {
            WorkerAssignmentTracker.Instance?.RecordAssignment(buildingId);
            string action = current.Count == 0 ? "assigned to" : "updated for";
            ToastManager.ShowToast(
                $"{newTrained} trained + {newUntrained} untrained {action} {building.GetDisplayName()}.",
                ToastType.Success, true);
            GameLogPanel.Instance?.LogPlayerAction(
                $"Worker assignment {action} {building.GetDisplayName()}: {newTrained} trained, {newUntrained} untrained");
        }
        else
        {
            ToastManager.ShowToast("Failed to assign workers — not enough available.", ToastType.Warning, true);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // PRE-CONFIRM VALIDATION (called by TaskDetailUI before CompleteTask)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns false and sets errorMessage if the current numerical input values are invalid.
    /// Called by TaskDetailUI before allowing confirmation.
    /// </summary>
    public bool ValidateForConfirm(GameTask task, out string errorMessage)
    {
        errorMessage = "";

        int buildingId = -1;
        foreach (var kvp in pendingTasks)
        {
            if (kvp.Value == task) { buildingId = kvp.Key; break; }
        }
        if (buildingId == -1) return true; // Not our task — let it through

        if (task.numericalInputs.Count < 2) return true;

        Building building = FindObjectsOfType<Building>()
            .FirstOrDefault(b => b.GetOriginalSiteId() == buildingId);
        if (building == null) return true;

        WorkerSystem ws = WorkerSystem.Instance ?? FindObjectOfType<WorkerSystem>();
        if (ws == null) return true;

        int newTrained   = task.numericalInputs[0].currentValue;
        int newUntrained = task.numericalInputs[1].currentValue;
        int newWorkforce = newTrained * 2 + newUntrained;
        int newHeadCount = newTrained + newUntrained;
        int required     = building.GetRequiredWorkforce();

        bool isLocked        = WorkerAssignmentTracker.Instance != null
            && WorkerAssignmentTracker.Instance.IsLockedForRelease(buildingId);
        int currentHeadCount = ws.GetWorkersByBuildingId(buildingId).Count;

        if (isLocked && newHeadCount < currentHeadCount)
        {
            errorMessage = $"Workers committed last round cannot be released. " +
                           $"You may only swap trained ↔ untrained — keep the same total of {currentHeadCount} workers.";
            return false;
        }

        if (isLocked && newHeadCount > currentHeadCount)
        {
            errorMessage = $"You cannot add workers beyond the {currentHeadCount} already locked in. " +
                           $"Only composition swaps are allowed.";
            return false;
        }

        if (newWorkforce < required)
        {
            errorMessage = $"Not enough workforce — this facility needs {required} points " +
                           $"but you have assigned only {newWorkforce}. " +
                           $"Add more trained (2 pts) or untrained (1 pt) workers.";
            return false;
        }

        if (newWorkforce > required)
        {
            errorMessage = $"You are assigning more workers than required — " +
                           $"{newWorkforce} pts assigned vs {required} needed. " +
                           $"Reduce the count before confirming.";
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    // ASSIGNMENT EXECUTION
    // ─────────────────────────────────────────────────────────────────

    bool ApplyAssignment(WorkerSystem ws, Building building, int buildingId, int trainedCount, int untrainedCount)
    {
        try
        {
            ws.ReleaseWorkersFromBuilding(buildingId);

            List<Worker> available  = ws.GetAvailableWorkers();
            var toAssign            = new List<Worker>();

            toAssign.AddRange(available.Where(w => w.Type == WorkerType.Trained).Take(trainedCount));
            toAssign.AddRange(available.Where(w => w.Type == WorkerType.Untrained).Take(untrainedCount));

            if (toAssign.Count(w => w.Type == WorkerType.Trained)   < trainedCount ||
                toAssign.Count(w => w.Type == WorkerType.Untrained) < untrainedCount)
            {
                Debug.LogError($"[WorkerAssignmentHandler] Not enough workers available for {building.GetDisplayName()}");
                return false;
            }

            foreach (Worker w in toAssign)
                w.TryAssignToBuilding(buildingId);

            building.UpdateWorkerStatus();
            if (building.HasSufficientWorkforce() && building.NeedsWorker())
                building.AssignWorker();

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorkerAssignmentHandler] Exception: {e.Message}");
            return false;
        }
    }
}
