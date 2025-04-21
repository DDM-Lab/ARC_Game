using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CityBuilderCore;


/// <summary>
/// --------------Manages building-related systems and communication------------
/// </summary>
public class BuildingSystem : MonoBehaviour
{
    public static BuildingSystem Instance { get; private set; }
    [Header("Initial Building Setup")]
    public BuildingInfo motelInfo;
    public BuildingInfo communityInfo;
    
    [Header("Building Positions")]
    public Vector2Int motelPosition = new Vector2Int(5, 13);
    public Vector2Int[] communityPositions = new Vector2Int[]
    {
        new Vector2Int(14, 26),
        new Vector2Int(36, 23),
        new Vector2Int(32, 11),
        new Vector2Int(16, 3)
    };
    
    // Building tracking
    private List<Building> _communities = new List<Building>();
    private List<Building> _shelters = new List<Building>();
    private List<Building> _kitchens = new List<Building>();
    private List<Building> _others = new List<Building>();
    
    // References
    private IBuildingManager _buildingManager;
    private IGridPositions _gridPositions;
    public void DiagnoseBuildingPlacement()
    {
        Debug.Log("=== BUILDING SYSTEM DIAGNOSTICS ===");
        Debug.Log($"Motel Info assigned: {motelInfo != null}");
        Debug.Log($"Community Info assigned: {communityInfo != null}");
        Debug.Log($"Building Manager found: {_buildingManager != null}");
        Debug.Log($"Grid Positions found: {_gridPositions != null}");
        
        if (motelInfo == null)
            Debug.LogError("CRITICAL: Assign motelInfo in the Inspector!");
        
        if (communityInfo == null)
            Debug.LogError("CRITICAL: Assign communityInfo in the Inspector!");
            
        // Try getting references again if they're missing
        if (_buildingManager == null)
            _buildingManager = Dependencies.Get<IBuildingManager>();
            
        if (_gridPositions == null)
            _gridPositions = Dependencies.Get<IGridPositions>();
            
        Debug.Log("=== END DIAGNOSTICS ===");
    }
    private void Awake()
    {
        // Don't try to get dependencies in Awake. Do it in start instead.
        Instance = this;
    }
    
    private void Start()
    {
        // Get required dependencies in Start instead of Awake
        _buildingManager = Dependencies.Get<IBuildingManager>();
        _gridPositions = Dependencies.Get<IGridPositions>();
        
        Debug.Log($"[BuildingSystem] Dependencies initialized - Building Manager: {_buildingManager != null}, Grid Positions: {_gridPositions != null}");
        
        // Place initial buildings
        StartCoroutine(WaitAndPlaceInitialBuildings());
        
    }
    
    private IEnumerator WaitAndPlaceInitialBuildings()
    {
        // Wait one frame to ensure all dependencies are initialized
        yield return new WaitForEndOfFrame();
        
        // Place initial buildings
        PlaceInitialBuildings();
    }
    
    /// <summary>
    /// Places the initial buildings in the scene (motel and communities)
    /// </summary>
    public void PlaceInitialBuildings()
    {
        if (_buildingManager == null || _gridPositions == null)
        {
            Debug.LogError("[BuildingSystem] Cannot place buildings - dependencies missing!");
            return;
        }
        
        var buildingRotation = BuildingRotation.Create();
        
        // Place motel
        if (motelInfo == null)
        {
            Debug.LogError("[BuildingSystem] MotelInfo is not assigned!");
        }
        else
        {
            // Position and rotation
            Vector3 worldPos = _gridPositions.GetWorldPosition(motelPosition);
            Quaternion rotation = Quaternion.identity;
            motelInfo.Prepare(motelPosition, buildingRotation);
            
            // Instantiate and register motel
            var building = _buildingManager.Add(worldPos, rotation, motelInfo.GetPrefab(0));
            RegisterBuilding(building, "other");
            
            Debug.Log($"[BuildingSystem] Placed Motel at {motelPosition}");
        }
        
        // Place communities
        if (communityInfo == null)
        {
            Debug.LogError("[BuildingSystem] CommunityInfo is not assigned!");
        }
        else
        {
            foreach (var pos in communityPositions)
            {
                communityInfo.Prepare(pos, buildingRotation);
                
                Vector3 worldPos = _gridPositions.GetWorldPosition(pos);
                Quaternion rotation = Quaternion.identity;
                var building = _buildingManager.Add(worldPos, rotation, communityInfo.GetPrefab(0));
                RegisterBuilding(building, "community");
                
                Debug.Log($"[BuildingSystem] Placed Community at {pos}");
            }
        }
    }
    
    /// <summary>
    /// Register a new building in the system
    /// </summary>
    public void RegisterBuilding(Building building, string type)
    {
        switch (type.ToLower())
        {
            case "community":
                if (!_communities.Contains(building))
                    _communities.Add(building);
                break;
            case "shelter":
                if (!_shelters.Contains(building))
                    _shelters.Add(building);
                break;
            case "kitchen":
                if (!_kitchens.Contains(building))
                    _kitchens.Add(building);
                break;
            default:
                if (!_others.Contains(building))
                    _others.Add(building);
                break;
        }
        
        Debug.Log($"[BuildingSystem] Registered building {building.name} as {type}");
    }
    
    /// <summary>
    /// Register buildings using the same method names as GameDatabase for compatibility
    /// </summary>
    public void RegisterShelter(Building shelter) => RegisterBuilding(shelter, "shelter");
    public void RegisterKitchen(Building kitchen) => RegisterBuilding(kitchen, "kitchen");
    public void RegisterCommunity(Building community) => RegisterBuilding(community, "community");
    public void RegisterGeneric(Building building) => RegisterBuilding(building, "other");
    
    /// <summary>
    /// Update all buildings after disaster events or at end of day
    /// </summary>
    public void UpdateAllBuildings()
    {
        // Notify communities of flood changes
        NotifyCommunitiesOfFloodChange();
        
        // Reset food orders in shelters
        foreach (var building in _shelters)
        {
            if (building != null && building.TryGetComponent<ShelterLogic>(out var shelter))
            {
                shelter.ClearFoodStorage();         // Clear previous food
                shelter.GenerateFoodOrderDebug();   // Create new daily order
            }
        }
        
        // Notify kitchens of new orders
        NotifyKitchensOfNewOrder();
        
        Debug.Log("[BuildingSystem] Updated all buildings");
    }
    
    /// <summary>
    /// Implements the AdvanceDay function from GameDatabase
    /// </summary>
    public void AdvanceDay()
    {
        foreach (var building in _shelters)
        {
            var shelter = building.GetComponent<ShelterLogic>();
            if (shelter != null)
            {
                shelter.ClearFoodStorage();         // Clear previous food
                shelter.GenerateFoodOrderDebug();   // Create new daily order
            }
        }

        NotifyCommunitiesOfFloodChange();  // Check flood status and notify communities

        Debug.Log("[BuildingSystem] Day advanced, food cleared and new orders placed. Community flood checks initiated.");
    }
    
    /// <summary>
    /// Replicates the NotifyCommunitiesOfFloodChange function from GameDatabase
    /// </summary>
    public void NotifyCommunitiesOfFloodChange()
    {
        foreach (var community in _communities)
        {
            if (community == null)
                continue;

            if (community.TryGetComponent<CommunityLogic>(out var logic))
            {
                logic.CheckFloodStatusAndUpdateOrder();
            }
            else
            {
                Debug.LogWarning($"[BuildingSystem] Community {community.name} has no CommunityLogic component.");
            }
        }

        Debug.Log("[BuildingSystem] Notified all communities of flood tile changes.");
    }
    
    /// <summary>
    /// Notify kitchens to restart delivery attempts
    /// </summary>
    public void NotifyKitchensOfNewOrder()
    {
        foreach (var kitchen in _kitchens)
        {
            if (kitchen.TryGetComponent<ProductionWalkerComponent>(out var productionWalker))
            {
                productionWalker.TryRestartDelivery();
            }
        }

        Debug.Log("[BuildingSystem] Notified kitchens to retry delivery.");
    }
    
    /// <summary>
    /// Get all buildings of a specific type
    /// </summary>
    public IReadOnlyList<Building> GetBuildingsByType(string type)
    {
        switch (type.ToLower())
        {
            case "community":
                return _communities;
            case "shelter":
                return _shelters;
            case "kitchen":
                return _kitchens;
            default:
                return _others;
        }
    }
    
    // Accessor methods to match GameDatabase API during transition
    public IReadOnlyList<Building> GetAllCommunities() => _communities;
    public IReadOnlyList<Building> GetAllShelters() => _shelters;
    public IReadOnlyList<Building> GetAllKitchens() => _kitchens;
    public IReadOnlyList<Building> GetAllGenericBuildings() => _others;
}