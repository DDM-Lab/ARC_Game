using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CityBuilderCore;
using System.Linq;

public class GameDatabase : MonoBehaviour
{
    public static GameDatabase Instance { get; private set; }
    public BuildingInfo motelInfo; // Assign in Inspector
    public BuildingInfo communityInfo; // Assign in Inspector
    private readonly List<Building> _shelters = new();
    private readonly List<Building> _kitchens = new();
    private readonly List<Building> _communities = new();
    private readonly List<Building> _others = new();


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        var buildingManager = Dependencies.GetOptional<IBuildingManager>();
        if (buildingManager == null)
        {
            Debug.LogError("[GameDatabase] IBuildingManager dependency is missing!");
        }
        else
            StartCoroutine(WaitAndPlace());
    }

    private IEnumerator WaitAndPlace()
    {
        yield return new WaitForEndOfFrame();  // wait one frame to ensure dependencies are ready
        PlaceStartingBuildings();
    }


    // --- Registration Methods ---
    public void RegisterShelter(Building shelter)
    {
        if (!_shelters.Contains(shelter))
            _shelters.Add(shelter);
    }
    public void RegisterKitchen(Building kitchen)
    {
        if (!_kitchens.Contains(kitchen))
            _kitchens.Add(kitchen);
    }
    public void RegisterCommunity(Building community)
    {
        if (!_communities.Contains(community))
            _communities.Add(community);
    }

    public void RegisterGeneric(Building building)
    {
        if (!_others.Contains(building))
            _others.Add(building);
    }

    // --- Accessors ---
    public IReadOnlyList<Building> GetAllShelters() => _shelters;
    public IReadOnlyList<Building> GetAllKitchens() => _kitchens;
    public IReadOnlyList<Building> GetAllCommunities() => _communities;
    public IReadOnlyList<Building> GetAllGenericBuildings() => _others;


    // --- Example Daily Logic ---
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

        foreach (var community in _communities)
        {
            var logic = community.GetComponent<CommunityLogic>();
            logic?.CheckFlooded(); // Trigger flood check
        }

        Debug.Log("[GameDatabase] Day advanced, food cleared and new orders placed. Community flood checks initiated.");
    }

    /*public void NotifyKitchensOfNewOrder()
    {
        foreach (var kitchen in _kitchens)
        {
            if (kitchen.TryGetComponent<ProductionWalkerComponent>(out var productionWalker))
            {
                productionWalker.ForceTryDelivery();
            }
        }
    }*/
    public void PlaceStartingBuildings()
    {
        var buildingManager = Dependencies.Get<IBuildingManager>();
        var gridPositions = Dependencies.Get<IGridPositions>();
        var buildingRotation = BuildingRotation.Create();

        //---- Build motel ----
        if (motelInfo == null)
        {
            Debug.LogError("[GameDatabase] MotelInfo is not assigned!");
            return;
        }

        // Position and rotation
        Vector2Int gridPos = new Vector2Int(5, 13);
        Vector3 worldPos = gridPositions.GetWorldPosition(gridPos);
        Quaternion rotation = Quaternion.identity; // or use BuildingRotation if needed
        motelInfo.Prepare(gridPos, buildingRotation);

        // Instantiate and register motel
        var building = buildingManager.Add(worldPos, rotation, motelInfo.GetPrefab(0));
        GameDatabase.Instance.RegisterGeneric(building); // or RegisterMotel if you have one

        Debug.Log($"[GameDatabase] Placed Motel at {gridPos}");

        //---- Build communities ----
        if (communityInfo == null)
        {
            Debug.LogError("[GameDatabase] CommunityInfo is not assigned!");
            return;
        }

        Vector2Int[] communityGridPositions = new Vector2Int[]
        {
            new Vector2Int(14, 26),
            new Vector2Int(36, 23),
            new Vector2Int(32, 11),
            new Vector2Int(16, 3)
        };

        foreach (var pos in communityGridPositions)
        {
            communityInfo.Prepare(pos, buildingRotation);

            Vector3 communityWorldPos = gridPositions.GetWorldPosition(pos);
            var community = buildingManager.Add(communityWorldPos, rotation, communityInfo.GetPrefab(0));
            GameDatabase.Instance.RegisterCommunity(community);  // Make sure this method exists
            Debug.Log($"[GameDatabase] Placed Community at {pos}");
        }
    }



}
