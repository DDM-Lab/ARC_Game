using UnityEngine;
public class BuildingSystemUIIntegration : MonoBehaviour
{
    private BuildingSystem buildingSystem;
    private BuildingUIOverlay uiOverlay;

    public static event System.Action<Building> OnBuildingCreated;
    public static event System.Action<Building> OnBuildingDestroyed;
    
    void Start()
    {
        buildingSystem = GetComponent<BuildingSystem>();
        if (buildingSystem == null)
        {
            buildingSystem = FindObjectOfType<BuildingSystem>();
        }
        
        uiOverlay = FindObjectOfType<BuildingUIOverlay>();
        
        if (buildingSystem == null)
        {
            Debug.LogError("BuildingSystemUIIntegration: BuildingSystem not found!");
        }
        
        if (uiOverlay == null)
        {
            Debug.LogError("BuildingSystemUIIntegration: BuildingUIOverlay not found!");
        }
    }
    
    // Call this method after creating a building in BuildingSystem
    public void NotifyBuildingCreated(Building building)
    {
        if (uiOverlay != null && building != null)
        {
            uiOverlay.OnBuildingCreated(building);
        }

        OnBuildingCreated?.Invoke(building);
    }
    
    // Call this method before destroying a building
    public void NotifyBuildingDestroyed(Building building)
    {
        if (uiOverlay != null && building != null)
        {
            uiOverlay.OnBuildingDestroyed(building);
        }
        OnBuildingDestroyed?.Invoke(building);
    }
}