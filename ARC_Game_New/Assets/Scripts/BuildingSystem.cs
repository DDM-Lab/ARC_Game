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
}

// Note: ConstructionProject class is no longer needed since buildings handle their own construction