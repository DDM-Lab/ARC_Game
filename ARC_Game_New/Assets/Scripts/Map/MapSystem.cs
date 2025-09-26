using UnityEngine;
using System.Collections.Generic;

public class MapSystem : MonoBehaviour
{
    [Header("Map Configuration")]
    public Vector2 mapSize = new Vector2(1440, 1024);
    public Transform mapContainer;
    
    [Header("Building System Reference")]
    public BuildingSystem buildingSystem;
    
    [Header("Prefabs")]
    public GameObject motelPrefab;
    public GameObject communityPrefab;
    public GameObject abandonedSitePrefab;
    
    [Header("Map Layout")]
    public Vector2 motelPosition = new Vector2(0, 200);
    public int numberOfCommunities = 3;
    public List<Vector2> communityPositions = new List<Vector2>();
    public List<Vector2> abandonedSitePositions = new List<Vector2>();
    private List<AbandonedSite> abandonedSites = new List<AbandonedSite>();
    
    void Start()
    {
        InitializeMap();
        SetupCamera();
    }

    void InitializeMap()
    {
        // Create map container if not assigned
        if (mapContainer == null)
        {
            mapContainer = new GameObject("MapContainer").transform;
        }

        //Find motel position
        GameObject motel = GameObject.Find("Motel");
        if (motel != null)
        {
            motelPosition = motel.transform.position;
            Debug.Log($"Found Motel at ({motelPosition.x:F2}, {motelPosition.y:F2})");
            GameLogPanel.Instance.LogBuildingStatus($"Motel established at position {motelPosition}");
        }
        else
        {
            Debug.LogWarning("Motel object not found in the scene.");
            GameLogPanel.Instance.LogError("Motel object not found in the scene.");
        }

        //Find community positions
        for (int i = 0; i < numberOfCommunities; i++)
        {
            GameObject community = GameObject.Find($"Community0{i + 1}");
            if (community != null)
            {
                communityPositions.Add(community.transform.position);
                var communityPosition = community.transform.position;
                Debug.Log($"Found Community0{i + 1} at ({communityPosition.x:F2}, {communityPosition.y:F2})");
                GameLogPanel.Instance.LogBuildingStatus($"Found Community0{i + 1} at ({communityPosition.x:F2}, {communityPosition.y:F2})");
            }
            else
            {
                Debug.LogWarning($"Community0{i + 1} object not found in the scene.");
                GameLogPanel.Instance.LogError($"Community0{i + 1} object not found in the scene.");
            }
        }

        // Create abandoned sites
        for (int i = 0; i < abandonedSitePositions.Count; i++)
        {
            if (abandonedSitePrefab != null)
            {
                GameObject siteObj = Instantiate(abandonedSitePrefab, abandonedSitePositions[i], Quaternion.identity, mapContainer);
                siteObj.name = $"AbandonedSite_{i + 1}";

                AbandonedSite site = siteObj.GetComponent<AbandonedSite>();
                if (site != null)
                {
                    site.Initialize(i);
                    abandonedSites.Add(site);
                    var abandonedSitePosition = abandonedSitePositions[i];
                    Debug.Log($"Created AbandonedSite_{i + 1} at ({abandonedSitePosition.x:F2}, {abandonedSitePosition.y:F2})");
                    GameLogPanel.Instance.LogBuildingStatus($"AbandonedSite_{i + 1} located at ({abandonedSitePosition.x:F2}, {abandonedSitePosition.y:F2})");
                }
            }
            else
            {
                Debug.LogError("AbandonedSite prefab is not assigned in MapSystem.");
                GameLogPanel.Instance.LogError("AbandonedSite prefab is not assigned in MapSystem.");
            }
        }

        // Register abandoned sites with building system
        if (buildingSystem != null)
        {
            buildingSystem.RegisterAbandonedSites(abandonedSites);
        }
        else
        {
            Debug.LogWarning("BuildingSystem reference not assigned in MapSystem.");
            GameLogPanel.Instance.LogError("BuildingSystem reference not assigned in MapSystem.");
        }

        Debug.Log($"Map initialized with {abandonedSites.Count} abandoned sites, {communityPositions.Count} communities, and one motel.");
    }
    
    void SetupCamera()
    {

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.orthographic = true;
            mainCamera.orthographicSize = 10f; // fixed size for testing only
            mainCamera.transform.position = new Vector3(0, 0, -10);
        }

    }
    
    public List<AbandonedSite> GetAbandonedSites()
    {
        return abandonedSites;
    }
    
    public AbandonedSite GetAbandonedSiteById(int id)
    {
        return abandonedSites.Find(site => site.GetId() == id);
    }
}