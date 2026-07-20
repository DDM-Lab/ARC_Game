using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

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

    [Header("Delivery Details Section")]
    public GameObject deliveryDetailsSection;
    public TextMeshProUGUI deliveryPhaseText;
    public TextMeshProUGUI deliveryCargoText;
    public TextMeshProUGUI deliveryRouteText;
    public TextMeshProUGUI deliveryNoteText;

    private GameTask currentTask;
    
    void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => { TaskResultManager.Instance?.OnPopupClosed(); Destroy(gameObject); });
        
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
            facilityText.text = task.isGlobalTask ? "Global Task" : (string.IsNullOrEmpty(task.facilityDisplayName) ? task.affectedFacility : task.facilityDisplayName);
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

        PopulateDeliveryDetails(task);
    }

    void PopulateDeliveryDetails(GameTask task)
    {
        bool hasLinkedDeliveries = task.linkedDeliveryTaskIds != null && task.linkedDeliveryTaskIds.Count > 0;

        if (deliveryDetailsSection != null)
            deliveryDetailsSection.SetActive(hasLinkedDeliveries);

        if (!hasLinkedDeliveries) return;

        // Use the first linked delivery for display (most tasks link one delivery)
        int deliveryId = task.linkedDeliveryTaskIds[0];

        DeliveryTask delivery = null;
        Vehicle vehicle = null;

        if (DeliverySystem.Instance != null)
        {
            vehicle = DeliverySystem.Instance.GetVehicleForTask(deliveryId);

            // Search active and pending lists for the delivery task data
            foreach (var d in DeliverySystem.Instance.GetActiveTasks())
                if (d.taskId == deliveryId) { delivery = d; break; }

            if (delivery == null)
                foreach (var d in DeliverySystem.Instance.GetPendingTasks())
                    if (d.taskId == deliveryId) { delivery = d; break; }
        }

        if (delivery == null) return;

        // Cargo
        if (deliveryCargoText != null)
        {
            string cargoLabel = delivery.cargoType == ResourceType.Population ? "clients" : "meals";
            deliveryCargoText.text = $"{delivery.quantity}x {cargoLabel}";
        }

        // Route
        if (deliveryRouteText != null)
        {
            string src = GetBuildingDisplayName(delivery.sourceBuilding);
            string dst = GetBuildingDisplayName(delivery.destinationBuilding);
            deliveryRouteText.text = $"{src}  →  {dst}";
        }

        // Phase — determined by vehicle state or cargo
        if (deliveryPhaseText != null)
        {
            string phase = "Awaiting vehicle";
            if (vehicle != null)
            {
                phase = vehicle.currentStatus switch
                {
                    VehicleStatus.InTransit when vehicle.currentCargo.Any(kv => kv.Value > 0)
                        => "En route to drop-off",
                    VehicleStatus.InTransit
                        => "En route to pick-up",
                    VehicleStatus.Loading    => "Loading cargo",
                    VehicleStatus.Unloading  => "Unloading cargo",
                    VehicleStatus.Damaged    => "Vehicle damaged (flood)",
                    _                        => "Vehicle assigned"
                };
            }
            deliveryPhaseText.text = phase;
        }

        // Note — context about what happens next
        if (deliveryNoteText != null)
        {
            deliveryNoteText.text = task.status switch
            {
                TaskStatus.Incomplete =>
                    "The delivery has been halted. Satisfaction penalty has been applied.",
                TaskStatus.Expired =>
                    "The deadline has passed. The delivery may still be in progress but no longer counts toward the task.",
                TaskStatus.Completed =>
                    "All deliveries completed successfully.",
                _ => ""
            };
        }
    }

    static string GetBuildingDisplayName(MonoBehaviour building)
    {
        if (building == null) return "Unknown";
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetBuildingName();
        Building b = building.GetComponent<Building>();
        if (b != null) return b.GetDisplayName();
        return building.name;
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
        
        TaskResultManager.Instance?.OnPopupClosed();
        Destroy(gameObject);
    }
}