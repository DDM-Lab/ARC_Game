using UnityEngine;
using TMPro;

public class BuildingStatusRow : MonoBehaviour
{
    [Header("Row Fields")]
    public TextMeshProUGUI locationText;
    public TextMeshProUGUI foodPackNeedText;
    public TextMeshProUGUI foodPackConsumedText;
    public TextMeshProUGUI lodgingOccupancyText;
    public TextMeshProUGUI capacityText;

    private MonoBehaviour facility;
    private Building building;
    private PrebuiltBuilding prebuilt;
    private BuildingResourceStorage storage;

    public void Initialize(MonoBehaviour target)
    {
        facility = target;
        building = target.GetComponent<Building>();
        prebuilt = target.GetComponent<PrebuiltBuilding>();
        storage = target.GetComponent<BuildingResourceStorage>();
        Refresh();
    }

    public void Refresh()
    {
        if (facility == null) return;

        if (building != null)
        {
            RefreshForBuilding();
        }
        else if (prebuilt != null)
        {
            RefreshForPrebuilt();
        }
    }

    void RefreshForBuilding()
    {

        if (locationText != null)
        {
            BuildingType type = building.GetBuildingType();
            if (type == BuildingType.Kitchen || type == BuildingType.CaseworkSite) return;
            locationText.text = building.GetDisplayName();

            // BuildingType type = building.GetBuildingType();

            if (type == BuildingType.Shelter && storage != null)
            {
                int population = storage.GetResourceAmount(ResourceType.Population);
                int foodOnHand = storage.GetResourceAmount(ResourceType.FoodPacks);
                int foodNeed = Mathf.Max(0, population - foodOnHand);
                int capacity = storage.GetResourceCapacity(ResourceType.Population);

                if (foodPackNeedText != null) foodPackNeedText.text = $"{foodNeed}";
                if (foodPackConsumedText != null) foodPackConsumedText.text = $"{storage.GetTodayFoodPacksConsumed()}";
                if (lodgingOccupancyText != null) lodgingOccupancyText.text = $"{population}";
                if (capacityText != null) capacityText.text = $"{capacity}";
            }
            else
            {
                SetDashes();
            }
        }
            
    }

    void RefreshForPrebuilt()
    {
        if (locationText != null)
        {
            PrebuiltBuildingType type = prebuilt.GetPrebuiltType();
            if (type == PrebuiltBuildingType.Motel) return;
            locationText.text = prebuilt.GetBuildingName();

            // PrebuiltBuildingType type = prebuilt.GetPrebuiltType();

             if (type == PrebuiltBuildingType.Community && storage != null)
            {
                int population = storage.GetResourceAmount(ResourceType.Population);
                int foodOnHand = storage.GetResourceAmount(ResourceType.FoodPacks);
                int foodNeed = Mathf.Max(0, population - foodOnHand);
                int capacity = storage.GetResourceCapacity(ResourceType.Population);

                if (foodPackNeedText != null) foodPackNeedText.text = $"{foodNeed}";
                if (foodPackConsumedText != null) foodPackConsumedText.text =$"{storage.GetTodayFoodPacksConsumed()}";
                if (lodgingOccupancyText != null) lodgingOccupancyText.text = $"{population}";
                if (capacityText != null) capacityText.text = $"{capacity}";
            }
            else
            {
                SetDashes();
            }
        }
    }

    void SetDashes()
    {
        if (foodPackNeedText != null) foodPackNeedText.text = "—";
        if (foodPackConsumedText != null) foodPackConsumedText.text = "—";
        if (lodgingOccupancyText != null) lodgingOccupancyText.text = "—";
        if (capacityText != null) capacityText.text = "—";
    }
}