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
    public Vector2[] communityPositions = new Vector2[4]
    {
        new Vector2(-400, 100),
        new Vector2(400, 100),
        new Vector2(-400, -100),
        new Vector2(400, -100)
    };
    
    public Vector2[] abandonedSitePositions = new Vector2[8]
    {
        new Vector2(-200, 300),
        new Vector2(200, 300),
        new Vector2(-600, 0),
        new Vector2(600, 0),
        new Vector2(-300, -200),
        new Vector2(300, -200),
        new Vector2(-100, -300),
        new Vector2(100, -300)
    };
    
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
        
        // Create motel
        if (motelPrefab != null)
        {
            GameObject motel = Instantiate(motelPrefab, motelPosition, Quaternion.identity, mapContainer);
            motel.name = "Motel";
        }
        
        // Create communities
        for (int i = 0; i < communityPositions.Length; i++)
        {
            if (communityPrefab != null)
            {
                GameObject community = Instantiate(communityPrefab, communityPositions[i], Quaternion.identity, mapContainer);
                community.name = $"Community_{i + 1}";
            }
        }
        
        // Create abandoned sites
        for (int i = 0; i < abandonedSitePositions.Length; i++)
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
                }
            }
        }
        
        // Register abandoned sites with building system
        if (buildingSystem != null)
        {
            buildingSystem.RegisterAbandonedSites(abandonedSites);
        }
        
        Debug.Log($"Map initialized with {abandonedSites.Count} abandoned sites");
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