using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class BuildingSystem : MonoBehaviour
{
    [Header("Building Prefabs")]
    public GameObject kitchenPrefab;
    public GameObject shelterPrefab;
    public GameObject caseworkSitePrefab;
    
    [Header("Construction Settings")]
    public float constructionTime = 5f; // Base construction time in seconds
    
    [Header("UI References")]
    public BuildingSelectionUI buildingSelectionUI;
    
    private List<AbandonedSite> registeredSites = new List<AbandonedSite>();
    private AbandonedSite selectedSite;
    
    void Start()
    {
        // Subscribe to construction events if needed
    }
    
    public void RegisterAbandonedSites(List<AbandonedSite> sites)
    {
        registeredSites.AddRange(sites);
        foreach (var site in sites)
        {
            site.OnSiteSelected += HandleSiteSelection;
        }
        Debug.Log($"Registered {sites.Count} abandoned sites with BuildingSystem");
    }
    
    void HandleSiteSelection(AbandonedSite site)
    {
        if (site.IsAvailable())
        {
            selectedSite = site;
            // Show building selection UI
            if (buildingSelectionUI != null)
            {
                buildingSelectionUI.ShowSelectionUI(site.transform.position);
            }
            else
            {
                Debug.LogWarning("BuildingSelectionUI not assigned!");
            }
        }
    }
    
    public void OnBuildingTypeSelected(BuildingType buildingType)
    {
        if (selectedSite != null)
        {
            CreateBuildingImmediately(selectedSite, buildingType);
            selectedSite = null; // Clear selection
        }
    }
    
    public void CancelBuildingSelection()
    {
        selectedSite = null;
    }
    
    public void CreateBuildingImmediately(AbandonedSite site, BuildingType buildingType)
    {
        if (!site.IsAvailable())
        {
            Debug.LogWarning($"Site {site.GetId()} is not available for construction");
            return;
        }
        
        // Get the prefab for the building type
        GameObject buildingPrefab = GetBuildingPrefab(buildingType);
        
        if (buildingPrefab != null)
        {
            // Create the building at the site location immediately
            Vector3 sitePosition = site.transform.position;
            GameObject newBuilding = Instantiate(buildingPrefab, sitePosition, Quaternion.identity);
            newBuilding.name = $"{buildingType}_{site.GetId()}";
            
            // Initialize building component (this will start construction automatically)
            Building buildingComponent = newBuilding.GetComponent<Building>();
            if (buildingComponent != null)
            {
                buildingComponent.Initialize(buildingType, site.GetId());
            }
            
            // Convert abandoned site (disable it)
            site.ConvertToBuilding();
            
            Debug.Log($"Building {buildingType} created at site {site.GetId()} - construction started");
        }
        else
        {
            Debug.LogError($"No prefab found for building type: {buildingType}");
        }
    }
    
    GameObject GetBuildingPrefab(BuildingType buildingType)
    {
        switch (buildingType)
        {
            case BuildingType.Kitchen:
                return kitchenPrefab;
            case BuildingType.Shelter:
                return shelterPrefab;
            case BuildingType.CaseworkSite:
                return caseworkSitePrefab;
            default:
                return null;
        }
    }
    
    public bool IsUnderConstruction(AbandonedSite site)
    {
        // This method is no longer needed since buildings handle their own construction
        // Keeping for backward compatibility
        return false;
    }
    
    public List<Building> GetAllBuildings()
    {
        // Find all buildings in the scene
        return new List<Building>(FindObjectsOfType<Building>());
    }
    
    public List<Building> GetBuildingsUnderConstruction()
    {
        List<Building> buildings = GetAllBuildings();
        return buildings.FindAll(b => b.IsUnderConstruction());
    }
    
    public BuildingStatistics GetBuildingStatistics()
    {
        List<Building> allBuildings = GetAllBuildings();
        BuildingStatistics stats = new BuildingStatistics();
        
        foreach (Building building in allBuildings)
        {
            BuildingType type = building.GetBuildingType();
            BuildingStatus status = building.GetCurrentStatus();
            
            // Update counts by type
            switch (type)
            {
                case BuildingType.Kitchen:
                    stats.kitchenStats.UpdateCount(status);
                    break;
                case BuildingType.Shelter:
                    stats.shelterStats.UpdateCount(status);
                    break;
                case BuildingType.CaseworkSite:
                    stats.caseworkStats.UpdateCount(status);
                    break;
            }
            
            // Update total counts
            stats.totalStats.UpdateCount(status);
        }
        
        return stats;
    }
    
    public void PrintBuildingStatistics()
    {
        BuildingStatistics stats = GetBuildingStatistics();
        
        Debug.Log("=== BUILDING STATISTICS ===");
        Debug.Log($"Kitchen - InUse: {stats.kitchenStats.inUse}, UnderConstruction: {stats.kitchenStats.underConstruction}, NeedWorker: {stats.kitchenStats.needWorker}, Disabled: {stats.kitchenStats.disabled}");
        Debug.Log($"Shelter - InUse: {stats.shelterStats.inUse}, UnderConstruction: {stats.shelterStats.underConstruction}, NeedWorker: {stats.shelterStats.needWorker}, Disabled: {stats.shelterStats.disabled}");
        Debug.Log($"Casework - InUse: {stats.caseworkStats.inUse}, UnderConstruction: {stats.caseworkStats.underConstruction}, NeedWorker: {stats.caseworkStats.needWorker}, Disabled: {stats.caseworkStats.disabled}");
        Debug.Log($"TOTAL - InUse: {stats.totalStats.inUse}, UnderConstruction: {stats.totalStats.underConstruction}, NeedWorker: {stats.totalStats.needWorker}, Disabled: {stats.totalStats.disabled}");
    }
}

[System.Serializable]
public class BuildingTypeStats
{
    public int inUse = 0;
    public int underConstruction = 0;
    public int needWorker = 0;
    public int disabled = 0;
    
    public void UpdateCount(BuildingStatus status)
    {
        switch (status)
        {
            case BuildingStatus.InUse:
                inUse++;
                break;
            case BuildingStatus.UnderConstruction:
                underConstruction++;
                break;
            case BuildingStatus.NeedWorker:
                needWorker++;
                break;
            case BuildingStatus.Disabled:
                disabled++;
                break;
        }
    }
    
    public int GetTotal()
    {
        return inUse + underConstruction + needWorker + disabled;
    }
}

[System.Serializable]
public class BuildingStatistics
{
    public BuildingTypeStats kitchenStats = new BuildingTypeStats();
    public BuildingTypeStats shelterStats = new BuildingTypeStats();
    public BuildingTypeStats caseworkStats = new BuildingTypeStats();
    public BuildingTypeStats totalStats = new BuildingTypeStats();
    
    public int GetTotalBuildings()
    {
        return totalStats.GetTotal();
    }
    
    public float GetOperationalPercentage()
    {
        int total = GetTotalBuildings();
        return total > 0 ? (float)totalStats.inUse / total * 100f : 0f;
    }
}

// Note: ConstructionProject class is no longer needed since buildings handle their own construction