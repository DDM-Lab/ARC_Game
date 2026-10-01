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

    private BuildingResourceStorage subscribedStorage;

    public void Initialize(MonoBehaviour target)
    {
        facility = target;
        Refresh();
    }

    public bool IsFacilityGone => facility == null;

    public void Refresh()
    {
        if (facility == null) return;

        try
        {
            Building building = facility.GetComponent<Building>();
            if (building != null)
            {
                RefreshForBuilding(building);
                return;
            }

            PrebuiltBuilding prebuilt = facility.GetComponent<PrebuiltBuilding>();
            if (prebuilt != null)
            {
                RefreshForPrebuilt(prebuilt);
            }
        }
        catch (MissingReferenceException ex)
        {
            Debug.LogWarning($"[BuildingStatusRow] '{name}' hit a destroyed reference during Refresh() — treating facility as gone. {ex.Message}");
            UnsubscribeStorage();
            facility = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[BuildingStatusRow] '{name}' threw during Refresh(): {ex}");
        }
    }

    void RefreshForBuilding(Building building)
    {
        if (locationText == null) return;

        BuildingType type = building.GetBuildingType();
        if (type == BuildingType.Kitchen || type == BuildingType.CaseworkSite) return;

        locationText.text = building.GetDisplayName();

        if (type == BuildingType.Shelter)
        {
            BuildingResourceStorage storage = GetStorage(building);
            if (storage != null)
            {
                int population = storage.GetResourceAmount(ResourceType.Population);
                // GetFoodNeed() reads the outstanding-need ledger (BuildingResourceStorage), not a
                // population-minus-stock snapshot — it correctly reflects an earlier missed request
                // that a delivery still sitting in storage hasn't been credited against yet.
                int foodNeed = storage.GetFoodNeed();
                int capacity = storage.GetResourceCapacity(ResourceType.Population);

                SetText(foodPackNeedText, $"{foodNeed}");
                SetText(foodPackConsumedText, $"{storage.GetTodayFoodPacksConsumed()}");
                SetText(lodgingOccupancyText, $"{population}");
                SetText(capacityText, $"{capacity}");
                return;
            }
        }

        SetDashes();
    }

    void RefreshForPrebuilt(PrebuiltBuilding prebuilt)
    {
        if (locationText == null) return;

        PrebuiltBuildingType type = prebuilt.GetPrebuiltType();
        locationText.text = prebuilt.GetBuildingName();

        BuildingResourceStorage storage = GetStorage(prebuilt);

        if (type == PrebuiltBuildingType.Community && storage != null)
        {
            int population = storage.GetResourceAmount(ResourceType.Population);
            int capacity = storage.GetResourceCapacity(ResourceType.Population);

            int foodNeed = DailyReportData.Instance != null
                ? DailyReportData.Instance.GetTodayCommunityFoodDemandForFacility(prebuilt.name)
                : 0;
            int foodUsed = DailyReportData.Instance != null
                ? DailyReportData.Instance.GetTodayCommunityFoodUsedForFacility(prebuilt.name)
                : 0;

            SetText(foodPackNeedText, $"{foodNeed}");
            SetText(foodPackConsumedText, $"{foodUsed}");
            SetText(lodgingOccupancyText, $"{population}");
            SetText(capacityText, $"{capacity}");
        }
        else if (type == PrebuiltBuildingType.Motel && storage != null)
        {
            int population = storage.GetResourceAmount(ResourceType.Population);
            int foodNeed = storage.GetFoodNeed();
            int capacity = storage.GetResourceCapacity(ResourceType.Population);

            SetText(foodPackNeedText, $"{foodNeed}");
            SetText(foodPackConsumedText, $"{storage.GetTodayFoodPacksConsumed()}");
            SetText(lodgingOccupancyText, $"{population}");
            SetText(capacityText, $"{capacity}");
        }
        else
        {
            SetDashes();
        }
    }

    BuildingResourceStorage GetStorage(MonoBehaviour target)
    {
        BuildingResourceStorage storage = target.GetComponent<BuildingResourceStorage>();

        if (!ReferenceEquals(subscribedStorage, storage))
        {
            UnsubscribeStorage();
            subscribedStorage = storage;
            if (subscribedStorage != null)
                subscribedStorage.OnStorageUpdated += HandleStorageUpdated;
        }

        return storage;
    }

    void UnsubscribeStorage()
    {
        if (subscribedStorage != null)
            subscribedStorage.OnStorageUpdated -= HandleStorageUpdated;
        subscribedStorage = null;
    }

    void HandleStorageUpdated()
    {
        Refresh();
    }

    void OnDestroy()
    {
        UnsubscribeStorage();
    }

    void SetText(TextMeshProUGUI field, string value)
    {
        if (field != null) field.text = value;
    }

    void SetDashes()
    {
        SetText(foodPackNeedText, "—");
        SetText(foodPackConsumedText, "—");
        SetText(lodgingOccupancyText, "—");
        SetText(capacityText, "—");
    }
}