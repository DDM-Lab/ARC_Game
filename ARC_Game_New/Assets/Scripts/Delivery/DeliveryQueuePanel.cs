using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the delivery queue panel that lists all pending and active deliveries.
/// Attach to the panel GameObject in the scene. Assign rowPrefab (with a DeliveryQueueRow
/// component) and rowContainer in the Inspector.
/// </summary>
public class DeliveryQueuePanel : MonoBehaviour
{
    [Header("Panel References")]
    public GameObject panel;
    public Button toggleButton;
    [SerializeField] private float displayDuration = 2.5f; // how long panel stays open on row add

    [Header("List")]
    public Transform rowContainer;
    public GameObject rowPrefab;

    [Header("Empty State")]
    public GameObject emptyLabel;

    public static DeliveryQueuePanel Instance { get; private set; }

    private List<GameObject> activeRows = new List<GameObject>();

    private Coroutine autoCloseRoutine;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(TogglePanel);

        if (panel != null)
            panel.SetActive(false);

        SubscribeToEvents();
    }


    void SubscribeToEvents()
    {
        if (DeliverySystem.Instance != null)
        {
            DeliverySystem.Instance.OnTaskCreated += HandleTaskCreated;
            DeliverySystem.Instance.OnTaskAssigned += HandleTaskAssigned;
            DeliverySystem.Instance.OnTaskCompleted += HandleTaskCompleted;
        }

        if (DelayedBudgetManager.Instance != null)
        {
            DelayedBudgetManager.Instance.OnBudgetQueueChanged += HandleBudgetQueueChanged;
        }

        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged += _ => HandleQueueChanged();
        }
    }

    private void HandleQueueChanged() => RefreshList();
    private void HandleTaskCreated(DeliveryTask task) => RefreshList(task);
    private void HandleTaskAssigned(DeliveryTask task, Vehicle vehicle) => RefreshList();
    private void HandleTaskCompleted(DeliveryTask task) => RefreshList();
    private void HandleBudgetQueueChanged() => RefreshList();

    public void OnItemAdded(object newItem)
    {
        RefreshList(newItemToHighlight: newItem);
    }
    public void TogglePanel()
    {
        if (panel == null) return;
        bool next = !panel.activeSelf;
        panel.SetActive(next);
        //if (next) RefreshList();
        if (next)
        {
            RefreshList();
        }
        else
        {
            DeliveryRouteVisualizer visualizer = FindObjectOfType<DeliveryRouteVisualizer>();
            if (visualizer != null)
            {
                visualizer.HideRoute();
            }
        }
    }

    public void RefreshList(object newItemToHighlight = null)
    {
        if (newItemToHighlight != null)
        {
            TriggerAutoOpenSequence();
        }
        ClearRows();

        if (DeliverySystem.Instance == null) return;

        var pending = DeliverySystem.Instance.GetPendingTasks();
        var active  = DeliverySystem.Instance.GetActiveTasks();
        var budgets = DelayedBudgetManager.Instance != null ? DelayedBudgetManager.Instance.activeDelayedBudgets : new List<DelayedBudgetItem>();

        var requests = WorkerRequestSystem.Instance != null ? WorkerRequestSystem.Instance.GetActiveRequestTasks().FindAll(r => !r.isCompleted) : new List<WorkerRequestSystem.RequestTask>();
        var trainings = WorkerTrainingSystem.Instance != null ? WorkerTrainingSystem.Instance.GetActiveTrainingTasks().FindAll(t => !t.isCompleted) : new List<WorkerTrainingSystem.TrainingTask>();

        // Worker recruitment/training rows (spawned below) are pending actions too, so
        // the empty-state label must account for them — otherwise "(No action scheduled)"
        // shows alongside a real queued worker request.
        bool any = pending.Count > 0 || active.Count > 0 || budgets.Count > 0
                   || requests.Count > 0 || trainings.Count > 0;

        if (emptyLabel != null)
            emptyLabel.SetActive(!any);

        foreach (var delivery in active)
        {
            bool isNew = (newItemToHighlight is DeliveryTask t && t.taskId == delivery.taskId);
            SpawnRow(delivery, isPending: false, shouldHighlight: isNew);
        }

        foreach (var delivery in pending)
        {
            bool isNew = (newItemToHighlight is DeliveryTask t && t.taskId == delivery.taskId);
            SpawnRow(delivery, isPending: true, shouldHighlight: isNew);
        }

        foreach (var budget in budgets)
        {
            bool isNew = (newItemToHighlight is DelayedBudgetItem b && b.id == budget.id);
            SpawnBudgetRow(budget, shouldHighlight: isNew);
        }

        foreach (var request in requests)
        {
            bool isNew = (newItemToHighlight is WorkerRequestSystem.RequestTask r && r == request);
            SpawnWorkerRequestRow(request, shouldHighlight: isNew);
        }

        foreach (var training in trainings)
        {
            bool isNew = (newItemToHighlight is WorkerTrainingSystem.TrainingTask tr && tr == training);
            SpawnWorkerTrainingRow(training, shouldHighlight: isNew);
        }
    }

    void SpawnBudgetRow(DelayedBudgetItem budget, bool shouldHighlight)
    {
        if (rowPrefab == null || rowContainer == null) return;

        GameObject rowObj = Instantiate(rowPrefab, rowContainer);
        activeRows.Add(rowObj);

        DeliveryQueueRow row = rowObj.GetComponent<DeliveryQueueRow>();
        if (row != null)
        {
            row.InitializeDelayedBudget(budget);
            if (shouldHighlight)
            {
                row.HighlightRow(displayDuration);
            }
        }
    }

    void SpawnRow(DeliveryTask delivery, bool isPending, bool shouldHighlight)
    {
        if (rowPrefab == null || rowContainer == null) return;

        GameObject rowObj = Instantiate(rowPrefab, rowContainer);
        activeRows.Add(rowObj);

        DeliveryQueueRow row = rowObj.GetComponent<DeliveryQueueRow>();
        if (row == null) return;

        Vehicle vehicle = isPending ? null : DeliverySystem.Instance.GetVehicleForTask(delivery.taskId);
        row.Initialize(delivery, vehicle, isPending);

        if (shouldHighlight)
        {
            row.HighlightRow(displayDuration);
        }
    }

    void SpawnWorkerRequestRow(WorkerRequestSystem.RequestTask request, bool shouldHighlight)
    {
        if (rowPrefab == null || rowContainer == null) return;
        GameObject rowObj = Instantiate(rowPrefab, rowContainer);
        activeRows.Add(rowObj);

        DeliveryQueueRow row = rowObj.GetComponent<DeliveryQueueRow>();
        if (row != null)
        {
            row.InitializeWorkerRequest(request);
            if (shouldHighlight) row.HighlightRow(displayDuration);
        }
    }

    void SpawnWorkerTrainingRow(WorkerTrainingSystem.TrainingTask training, bool shouldHighlight)
    {
        if (rowPrefab == null || rowContainer == null) return;
        GameObject rowObj = Instantiate(rowPrefab, rowContainer);
        activeRows.Add(rowObj);

        DeliveryQueueRow row = rowObj.GetComponent<DeliveryQueueRow>();
        if (row != null)
        {
            row.InitializeWorkerTraining(training);
            if (shouldHighlight) row.HighlightRow(displayDuration);
        }
    }

    /// <summary>
    /// Opens the panel when a row is spawned and automatically closes it after displayDuration seconds.
    /// </summary>
    private void TriggerAutoOpenSequence()
    {
        if (panel != null && !panel.activeSelf)
        {
            panel.SetActive(true);
        }

        if (autoCloseRoutine != null)
            StopCoroutine(autoCloseRoutine);

        autoCloseRoutine = StartCoroutine(AutoCloseSequence());
    }

    private IEnumerator AutoCloseSequence()
    {
        yield return new WaitForSecondsRealtime(displayDuration);

        if (panel != null)
            panel.SetActive(false);

        autoCloseRoutine = null;
    }
    void ClearRows()
    {
        foreach (var row in activeRows)
            if (row != null) Destroy(row);
        activeRows.Clear();
    }

    void OnDestroy()
    {
        if (DeliverySystem.Instance == null) return;
        DeliverySystem.Instance.OnTaskCreated  -= _ => RefreshList();
        DeliverySystem.Instance.OnTaskAssigned -= (_, __) => RefreshList();
        DeliverySystem.Instance.OnTaskCompleted -= _ => RefreshList();

        if (DelayedBudgetManager.Instance != null)
        {
            DelayedBudgetManager.Instance.OnBudgetQueueChanged -= HandleBudgetQueueChanged;
        }
    }
}
