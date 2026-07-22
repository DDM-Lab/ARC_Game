using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System.Collections;

/// <summary>
/// Populates a single row in the delivery queue panel.
/// Attach to the row prefab and wire up the text/button fields in the Inspector.
/// </summary>
public class DeliveryQueueRow : MonoBehaviour
{

    [Header ("UI Highlight")]
    [SerializeField] private Image rowBackgroundImage; 
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.4f, 1f); // new row notif highlight

    [Header("Text Fields")]
    public TextMeshProUGUI statusText;   // "Delivering" / "Picking up" / "Queued" / "Damaged"
    public TextMeshProUGUI cargoText;    // "5x meals"
    public TextMeshProUGUI routeText;    // "Kitchen  →  Community 1"
    public TextMeshProUGUI etaText;      // "ETA: 00:42" or "Queued"

    [Header("Locate Button")]
    public Button locateButton;

    private Vehicle associatedVehicle;

    public void Initialize(DeliveryTask delivery, Vehicle vehicle, bool isPending)
    {
        associatedVehicle = vehicle;

        // Cargo
        if (cargoText != null)
        {
            string cargoLabel = delivery.cargoType == ResourceType.Population ? "clients" : "meals";
            cargoText.text = $"{delivery.quantity}x {cargoLabel}";
        }

        // Route
        if (routeText != null)
        {
            string src = GetBuildingDisplayName(delivery.sourceBuilding);
            string dst = GetBuildingDisplayName(delivery.destinationBuilding);
            routeText.text = $"{src}  →  {dst}";
        }

        // Status
        if (statusText != null)
        {
            if (isPending || vehicle == null)
            {
                statusText.text = "Queued";
            }
            else
            {
                bool hasLoaded = vehicle.currentCargo.Any(kv => kv.Value > 0);
                statusText.text = vehicle.currentStatus switch
                {
                    VehicleStatus.InTransit when hasLoaded => "Delivering",
                    VehicleStatus.InTransit                => "Picking up",
                    VehicleStatus.Loading                  => "Loading",
                    VehicleStatus.Unloading                => "Unloading",
                    VehicleStatus.Damaged                  => "Damaged",
                    _                                      => "In progress"
                };
            }
        }

        // ETA
        if (etaText != null)
        {
            if (isPending || vehicle == null || vehicle.currentStatus == VehicleStatus.Damaged)
            {
                etaText.text = isPending ? "Queued" : "N/A";
            }
            else
            {
                string eta = delivery.GetEstimatedTimeString();
                etaText.text = eta == "Unknown" ? "ETA: —" : $"ETA: {eta}";
            }
        }

        // Locate button — only enabled when there is a live vehicle to focus on
        if (locateButton != null)
        {
            locateButton.gameObject.SetActive(true);
            bool canLocate = vehicle != null && vehicle.currentStatus != VehicleStatus.Idle;
            locateButton.interactable = canLocate;
            locateButton.onClick.RemoveAllListeners();
            locateButton.onClick.AddListener(OnLocateClicked);
        }
    }

    void OnLocateClicked()
    {
        if (associatedVehicle == null) return;

        // Close the vehicle info panel if open — just show the route on the map.
        VehicleInfoPanel.Instance?.ClosePanel();

        DeliveryRouteVisualizer visualizer = FindObjectOfType<DeliveryRouteVisualizer>();
        if (visualizer == null) return;

        var path = associatedVehicle.currentPath;
        if (path == null || path.Count == 0) return;

        Vector3 srcPos  = associatedVehicle.sourceBuilding != null
            ? associatedVehicle.sourceBuilding.transform.position : path[0];
        Vector3 destPos = associatedVehicle.destinationBuilding != null
            ? associatedVehicle.destinationBuilding.transform.position : path[path.Count - 1];

        visualizer.ShowRoute(path, srcPos, destPos);
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

    public void InitializeDelayedBudget(DelayedBudgetItem budgetItem)
    {
        associatedVehicle = null;
        if (cargoText != null)
        {
            string sign = budgetItem.amount >= 0 ? "+" : "";
            cargoText.text = $"{sign}${budgetItem.amount:N0}";
        }
        if (routeText != null)
        {
            routeText.text = $"{budgetItem.sourceTaskTitle}";
        }
        if (statusText != null)
        {
            string roundsLabel = budgetItem.roundsRemaining == 1 ? "1 round" : $"{budgetItem.roundsRemaining} rounds";
            statusText.text = "in " + roundsLabel;
        }
        if (locateButton != null)
        {
            locateButton.gameObject.SetActive(false);
        }
    }

    // --- Add to DeliveryQueueRow.cs ---

    public void InitializeWorkerRequest(WorkerRequestSystem.RequestTask request)
    {
        associatedVehicle = null;

        string label = request.workerType == WorkerType.Untrained ? "Untrained" : "Trained";

        if (cargoText != null)
        {
            cargoText.text = $"+{request.workerCount} {label}";
        }

        if (routeText != null)
        {
            routeText.text = "Worker Recruitment";
        }

        if (statusText != null)
        {
            int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
            int daysLeft = Mathf.Max(0, request.arrivalDay - currentDay);
            statusText.text = daysLeft == 1 ? "Arrive in 1 day" : $"Arrive in {daysLeft} days";
        }

        if (etaText != null) etaText.text = "";

        if (locateButton != null) locateButton.gameObject.SetActive(false);
    }

    public void InitializeWorkerTraining(WorkerTrainingSystem.TrainingTask training)
    {
        associatedVehicle = null;

        if (cargoText != null)
        {
            cargoText.text = $"Training {training.workerCount}";
        }

        if (routeText != null)
        {
            routeText.text = "Responder Training";
        }

        if (statusText != null)
        {
            int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
            int daysLeft = Mathf.Max(0, training.completionDay - currentDay);
            statusText.text = daysLeft == 1 ? "Ready in 1 day" : $"Ready in {daysLeft} days";
        }

        if (etaText != null) etaText.text = "";

        if (locateButton != null) locateButton.gameObject.SetActive(false);
    }


    /// <summary>
    /// Highlights a row, used for showing new queued rows
    /// </summary>
    public void HighlightRow(float duration = 1.5f)
    {
        if (rowBackgroundImage != null)
        {
            StartCoroutine(FlashRoutine(duration));
        }
    }

    private IEnumerator FlashRoutine(float duration)
    {
        Color originalColor = rowBackgroundImage.color;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float pingPong = Mathf.PingPong(elapsed * 2f, 1f);
            rowBackgroundImage.color = Color.Lerp(originalColor, highlightColor, pingPong);
            yield return null;
        }

        rowBackgroundImage.color = originalColor;
    }
}
