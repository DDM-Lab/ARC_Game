using System.Collections.Generic;
using UnityEngine;

public class BuildingDatabase : MonoBehaviour
{
    public static BuildingDatabase Instance;

    private List<BuildingComponent> allBuildings = new();
    private Dictionary<GlobalEnums.BuildingType, List<BuildingComponent>> buildingsByType = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        foreach (GlobalEnums.BuildingType type in System.Enum.GetValues(typeof(GlobalEnums.BuildingType)))
        {
            buildingsByType[type] = new List<BuildingComponent>();
        }
    }

    public void RegisterBuilding(BuildingComponent building)
    {
        if (!allBuildings.Contains(building))
        {
            allBuildings.Add(building);
            buildingsByType[building.buildingType].Add(building);

            Debug.Log($"[BuildingDatabase] Registered new {building.buildingType}: {building.buildingName} at {building.transform.position}");
        }
    }

    public List<BuildingComponent> GetAllBuildings() => allBuildings;

    public List<BuildingComponent> GetBuildingsOfType(GlobalEnums.BuildingType type)
    {
        return buildingsByType.ContainsKey(type) ? buildingsByType[type] : new List<BuildingComponent>();
    }
}
