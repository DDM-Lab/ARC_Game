using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

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

    [Header("List")]
    public Transform rowContainer;
    public GameObject rowPrefab;

    [Header("Empty State")]
    public GameObject emptyLabel;

    public static DeliveryQueuePanel Instance { get; private set; }

    private List<GameObject> activeRows = new List<GameObject>();

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

        SubscribeToDeliveryEvents();
    }

    void SubscribeToDeliveryEvents()
    {
        if (DeliverySystem.Instance == null) return;
        DeliverySystem.Instance.OnTaskCreated  += _ => RefreshList();
        DeliverySystem.Instance.OnTaskAssigned += (_, __) => RefreshList();
        DeliverySystem.Instance.OnTaskCompleted += _ => RefreshList();
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

    public void RefreshList()
    {
        ClearRows();

        if (DeliverySystem.Instance == null) return;

        var pending = DeliverySystem.Instance.GetPendingTasks();
        var active  = DeliverySystem.Instance.GetActiveTasks();

        bool any = pending.Count > 0 || active.Count > 0;

        if (emptyLabel != null)
            emptyLabel.SetActive(!any);

        foreach (var delivery in active)
            SpawnRow(delivery, isPending: false);

        foreach (var delivery in pending)
            SpawnRow(delivery, isPending: true);
    }

    void SpawnRow(DeliveryTask delivery, bool isPending)
    {
        if (rowPrefab == null || rowContainer == null) return;

        GameObject rowObj = Instantiate(rowPrefab, rowContainer);
        activeRows.Add(rowObj);

        DeliveryQueueRow row = rowObj.GetComponent<DeliveryQueueRow>();
        if (row == null) return;

        Vehicle vehicle = isPending ? null : DeliverySystem.Instance.GetVehicleForTask(delivery.taskId);
        row.Initialize(delivery, vehicle, isPending);
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
    }
}
