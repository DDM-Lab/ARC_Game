using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MapConfigApplier : MonoBehaviour
{
    [Header("Grid → World Mapping")]
    public Vector3 gridOrigin    = new Vector3(-14.5f, -9.5f, 0f);
    public float   cellWorldSize = 1f;

    [Header("Road Tilemap")]
    public Tilemap  roadTilemap;
    public TileBase roadTile;

    [Header("Ground Tilemap")]
    public Tilemap  groundTilemap;
    public TileBase landTile;
    public TileBase riverTile;

    [Header("Blocking Tilemap")]
    public Tilemap  blockingTilemap;
    public TileBase blockingTile;

    [Header("Parents to Clear on Config Load")]
    public Transform treesParent;
    public Transform prebuiltParent;

    [Header("Prefabs")]
    public GameObject communityPrefab;
    public GameObject motelPrefab;
    public GameObject abandonedSitePrefab;
    public GameObject vehiclePrefab;
    public GameObject forestPrefab;

    [Header("Building Labels")]
    public Transform  labelContainer;
    public GameObject buildingLabelPrefab;

    [Header("Debug")]
    public bool showDebugInfo = true;

    static readonly string[] CommunityNames =
    {
        "Trinity", "Amherst", "Charleston", "Riverside", "Oakdale",
        "Maplewood", "Fairview", "Westbrook", "Lakewood", "Pinecrest"
    };
    const int MaxCommunities = 10;

    public bool hasApplied = false;

    IEnumerator Start()
    {
        yield return new WaitUntil(() =>
            GameConfigLoader.Instance != null &&
            GameConfigLoader.Instance.IsMapConfigLoaded());

        if (!GameConfigLoader.Instance.HasServerMapConfig())
        {
            if (showDebugInfo)
                Debug.Log("MapConfigApplier: No server config — keeping default scene layout.");
            hasApplied = true;
            yield break;
        }

        MapConfig cfg = GameConfigLoader.Instance.GetMapConfig();
        ApplyConfig(cfg);
        hasApplied = true;
    }

    public void ApplyConfig(MapConfig cfg)
    {
        ApplyTileLayers(cfg);
        ApplyForests(cfg);
        ApplyPrebuiltObjects(cfg);
        ApplyParameters(cfg);

        // Refresh FloodSystem river tile cache to match the new ground tilemap
        if (FloodSystem.Instance != null)
            FloodSystem.Instance.ReinitializeAfterMapApplied();
        else
            Debug.LogWarning("MapConfigApplier: FloodSystem.Instance not found — flood river cache not refreshed.");
    }

    void ApplyTileLayers(MapConfig cfg)
    {
        ApplyGroundTilemap(cfg);
        ApplyBlockingTilemap(cfg);
        ApplyRoadTilemap(cfg);
    }

    void ApplyGroundTilemap(MapConfig cfg)
    {
        if (groundTilemap == null) return;
        groundTilemap.ClearAllTiles();

        for (int y = 0; y < cfg.gridHeight; y++)
        for (int x = 0; x < cfg.gridWidth;  x++)
        {
            Vector3Int cell = groundTilemap.WorldToCell(GridToWorld(x, y));
            if (cfg.GetRiver(x, y) && riverTile != null)
                groundTilemap.SetTile(cell, riverTile);
            else if (cfg.GetLand(x, y) && landTile != null)
                groundTilemap.SetTile(cell, landTile);
        }

        if (showDebugInfo) Debug.Log("MapConfigApplier: Ground tilemap applied.");
    }

    void ApplyBlockingTilemap(MapConfig cfg)
    {
        if (blockingTilemap == null || blockingTile == null) return;
        blockingTilemap.ClearAllTiles();

        for (int y = 0; y < cfg.gridHeight; y++)
        for (int x = 0; x < cfg.gridWidth;  x++)
        {
            if (cfg.GetBlocking(x, y))
            {
                Vector3Int cell = blockingTilemap.WorldToCell(GridToWorld(x, y));
                blockingTilemap.SetTile(cell, blockingTile);
            }
        }

        if (showDebugInfo) Debug.Log("MapConfigApplier: Blocking tilemap applied.");
    }

    void ApplyRoadTilemap(MapConfig cfg)
    {
        if (roadTilemap == null || roadTile == null)
        {
            Debug.LogWarning("MapConfigApplier: roadTilemap or roadTile not assigned — skipping roads.");
            return;
        }

        roadTilemap.ClearAllTiles();

        for (int y = 0; y < cfg.gridHeight; y++)
        for (int x = 0; x < cfg.gridWidth;  x++)
        {
            if (cfg.GetRoad(x, y))
            {
                Vector3Int cell = roadTilemap.WorldToCell(GridToWorld(x, y));
                roadTilemap.SetTile(cell, roadTile);
            }
        }

        var rtm = FindObjectOfType<RoadTilemapManager>();
        if (rtm != null) rtm.RefreshRoadCache();

        if (showDebugInfo) Debug.Log("MapConfigApplier: Road tilemap applied.");
    }

    void ApplyForests(MapConfig cfg)
    {
        if (treesParent != null)
            ClearChildren(treesParent);

        if (forestPrefab == null)
        {
            Debug.LogWarning("MapConfigApplier: forestPrefab not assigned — skipping forest placement.");
            return;
        }

        int count = 0;
        foreach (var obj in cfg.objects)
        {
            if (obj.type != PlacedObjectType.Forest) continue;

            Vector3 pos = FootprintCentre(obj);
            GameObject go = Instantiate(forestPrefab, pos, Quaternion.identity,
                treesParent != null ? treesParent : transform);
            go.name = $"Forest_{count}";
            count++;
        }

        if (showDebugInfo) Debug.Log($"MapConfigApplier: Spawned {count} forests.");
    }

    void ApplyPrebuiltObjects(MapConfig cfg)
    {
        if (prebuiltParent != null)
            ClearChildren(prebuiltParent);

        int communityIndex = 0;
        int abandonedIndex = 0;
        int vehicleIndex   = 0;
        var spawnedVehicles = new List<Vehicle>();

        foreach (var obj in cfg.objects)
        {
            Vector3 pos = FootprintCentre(obj);

            switch (obj.type)
            {
                case PlacedObjectType.Community:
                    SpawnCommunity(pos, ref communityIndex);
                    break;

                case PlacedObjectType.Motel:
                    SpawnMotel(pos);
                    break;

                case PlacedObjectType.AbandonedSite:
                    SpawnAbandonedSite(pos, abandonedIndex);
                    abandonedIndex++;
                    break;

                case PlacedObjectType.Vehicle:
                    SpawnVehicle(pos, vehicleIndex, spawnedVehicles, obj.gridX, obj.gridY, cfg);
                    vehicleIndex++;
                    break;

                case PlacedObjectType.Forest:
                    break;
            }
        }

        if (showDebugInfo)
            Debug.Log($"MapConfigApplier: Spawned {communityIndex} communities, " +
                      $"1 motel (if placed), {abandonedIndex} abandoned sites, " +
                      $"{vehicleIndex} vehicles.");

        ReregisterAbandonedSites();
        ReregisterVehicles(spawnedVehicles);
    }

    void SpawnCommunity(Vector3 pos, ref int index)
    {
        if (index >= MaxCommunities)
        {
            Debug.LogWarning($"MapConfigApplier: Community limit ({MaxCommunities}) reached — skipping.");
            return;
        }

        if (communityPrefab == null)
        {
            Debug.LogWarning("MapConfigApplier: communityPrefab not assigned.");
            return;
        }

        string communityName = $"Community {CommunityNames[index]}";

        GameObject go = Instantiate(communityPrefab, pos, Quaternion.identity,
            prebuiltParent != null ? prebuiltParent : transform);
        go.name = communityName;

        var pb = go.GetComponent<PrebuiltBuilding>();
        if (pb != null)
        {
            pb.SetBuildingId(index);
            pb.SetBuildingName(communityName);
        }

        CreateLabel(go.transform, communityName);
        index++;
    }

    void SpawnMotel(Vector3 pos)
    {
        if (motelPrefab == null)
        {
            Debug.LogWarning("MapConfigApplier: motelPrefab not assigned.");
            return;
        }

        GameObject go = Instantiate(motelPrefab, pos, Quaternion.identity,
            prebuiltParent != null ? prebuiltParent : transform);
        go.name = "Motel";

        var pb = go.GetComponent<PrebuiltBuilding>();
        if (pb != null) pb.SetBuildingName("Motel");

        CreateLabel(go.transform, "Motel");
    }

    void SpawnAbandonedSite(Vector3 pos, int index)
    {
        if (abandonedSitePrefab == null)
        {
            Debug.LogWarning("MapConfigApplier: abandonedSitePrefab not assigned.");
            return;
        }

        GameObject go = Instantiate(abandonedSitePrefab, pos, Quaternion.identity,
            prebuiltParent != null ? prebuiltParent : transform);
        go.name = $"AbandonedSite_{index}";

        var site = go.GetComponent<AbandonedSite>();
        if (site != null) site.Initialize(index);
    }

    void SpawnVehicle(Vector3 pos, int index, List<Vehicle> spawnedVehicles, int gridX, int gridY, MapConfig cfg)
    {
        if (vehiclePrefab == null)
        {
            Debug.LogWarning("MapConfigApplier: vehiclePrefab not assigned.");
            return;
        }

        Vector3 vehiclePos = pos + new Vector3(0f, -0.5f, 0f);

        GameObject go = Instantiate(vehiclePrefab, vehiclePos, Quaternion.identity,
            prebuiltParent != null ? prebuiltParent : transform);

        go.name = $"Vehicle_{index + 1}";

        var pb = go.GetComponent<PrebuiltBuilding>();
        if (pb != null)
        {
            pb.SetBuildingId(index + 1);
            pb.SetBuildingName($"Vehicle {index + 1}");
        }

        var v = go.GetComponent<Vehicle>();
        if (v != null)
        {
            v.vehicleId   = index + 1;
            v.vehicleName = $"Vehicle {index + 1}";

            bool hasVertical   = cfg.GetRoad(gridX, gridY + 1) || cfg.GetRoad(gridX, gridY - 1);
            bool hasHorizontal = cfg.GetRoad(gridX - 1, gridY) || cfg.GetRoad(gridX + 1, gridY);
            if (hasVertical && !hasHorizontal)
            {
                go.transform.rotation = Quaternion.AngleAxis(v.defaultAngle + 90f, Vector3.forward);
                go.transform.position += new Vector3(-0.5f, 0f, 0f);
            }

            spawnedVehicles.Add(v);
        }
    }

    void ReregisterAbandonedSites()
    {
        var mapSystem = FindObjectOfType<MapSystem>();
        if (mapSystem == null)
        {
            Debug.LogWarning("MapConfigApplier: MapSystem not found — AbandonedSites not registered.");
            return;
        }

        var sites = new List<AbandonedSite>();
        AbandonedSite[] all = FindObjectsOfType<AbandonedSite>();
        foreach (var s in all) sites.Add(s);

        mapSystem.SetAbandonedSites(sites);

        if (showDebugInfo)
            Debug.Log($"MapConfigApplier: Re-registered {sites.Count} AbandonedSites with MapSystem.");
    }

    void ReregisterVehicles(List<Vehicle> vehicles)
    {
        var deliverySystem = DeliverySystem.Instance;
        if (deliverySystem == null)
        {
            Debug.LogWarning("MapConfigApplier: DeliverySystem not found — vehicles not registered.");
            return;
        }

        deliverySystem.availableVehicles.Clear();

        foreach (var v in vehicles)
            deliverySystem.AddVehicle(v);

        if (showDebugInfo)
            Debug.Log($"MapConfigApplier: Registered {vehicles.Count} vehicles with DeliverySystem.");
    }

    static void ApplyParameters(MapConfig cfg)
    {
        if (cfg.parameters == null) return;

        var loader = GameConfigLoader.Instance;
        if (loader == null) return;

        loader.loadedInitialBudget       = cfg.parameters.initialBudget;
        loader.loadedInitialSatisfaction = cfg.parameters.initialSatisfaction;
    }

    void CreateLabel(Transform buildingTransform, string text)
    {
        if (buildingLabelPrefab == null || labelContainer == null)
        {
            if (showDebugInfo)
                Debug.LogWarning($"MapConfigApplier: Cannot create label '{text}' — " +
                    $"buildingLabelPrefab={(buildingLabelPrefab == null ? "NULL" : "ok")}, " +
                    $"labelContainer={(labelContainer == null ? "NULL" : "ok")}");
            return;
        }

        GameObject label = Instantiate(buildingLabelPrefab, labelContainer);
        label.name = $"Label_{text}";

        var lbl = label.GetComponent<PlacedBuildingLabel>();
        if (lbl != null)
        {
            lbl.target = buildingTransform;
            lbl.SetText(text);
        }
    }

    Vector3 FootprintCentre(PlacedObjectData obj)
    {
        Vector3 origin = GridToWorld(obj.gridX, obj.gridY);
        return origin + new Vector3(
            obj.width  * cellWorldSize * 0.5f,
            obj.height * cellWorldSize * 0.5f,
            0f);
    }

    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    public Vector3 GridToWorld(int gx, int gy) =>
        gridOrigin + new Vector3(gx * cellWorldSize, gy * cellWorldSize, 0f);

    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3 local = world - gridOrigin;
        return new Vector2Int(
            Mathf.FloorToInt(local.x / cellWorldSize),
            Mathf.FloorToInt(local.y / cellWorldSize));
    }
}
