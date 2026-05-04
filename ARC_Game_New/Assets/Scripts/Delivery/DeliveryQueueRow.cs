using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

/// <summary>
/// Populates a single row in the delivery queue panel.
/// Attach to the row prefab and wire up the text/button fields in the Inspector.
/// </summary>
public class DeliveryQueueRow : MonoBehaviour
{
    [Header("Text Fields")]
    public TextMeshProUGUI statusText;   // "Delivering" / "Picking up" / "Queued" / "Damaged"
    public TextMeshProUGUI cargoText;    // "5x food packs"
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
            string cargoLabel = delivery.cargoType == ResourceType.Population ? "clients" : "food packs";
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
                    VehicleStatus.Returning                => "Returning",
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
            bool canLocate = vehicle != null && vehicle.currentStatus != VehicleStatus.Idle;
            locateButton.interactable = canLocate;
            locateButton.onClick.AddListener(OnLocateClicked);
        }
    }

    void OnLocateClicked()
    {
        if (associatedVehicle == null) return;

        if (VehicleInfoPanel.Instance != null)
            VehicleInfoPanel.Instance.ShowVehicleInfo(associatedVehicle);
    }

    static string GetBuildingDisplayName(MonoBehaviour building)
    {
        if (building == null) return "Unknown";
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetBuildingName();
        Building b = building.GetComponent<Building>();
        if (b != null) return $"{b.GetBuildingType()} (Site {b.GetOriginalSiteId()})";
        return building.name;
    }
}
