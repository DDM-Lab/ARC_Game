using UnityEngine;
using System.Collections.Generic;

public class MapSystem : MonoBehaviour
{
    [Header("Map Configuration")]
    public Vector2 mapSize = new Vector2(1440, 1024);
    public Transform mapContainer;
    
    [Header("Building System Reference")]
    public BuildingSystem buildingSystem;
    
    [Header("Map Layout")]
    public Vector2 motelPosition = new Vector2(0, 200);
    public int numberOfCommunities = 3;
    public List<Vector2> communityPositions = new List<Vector2>();
    private List<AbandonedSite> abandonedSites = new List<AbandonedSite>();
    
    void Start()
    {
        InitializeMap();
        SetupCamera();
    }

    void InitializeMap()
    {
        if (mapContainer == null)
        {
            mapContainer = new GameObject("MapContainer").transform;
        }

        // Find motel position
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

        // Find community positions
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

        // Find all pre-placed abandoned sites in the scene
        AbandonedSite[] foundSites = FindObjectsOfType<AbandonedSite>();

        if (foundSites.Length == 0)
        {
            Debug.LogWarning("No AbandonedSite objects found in the scene.");
            GameLogPanel.Instance.LogError("No AbandonedSite objects found in the scene.");
        }

        for (int i = 0; i < foundSites.Length; i++)
        {
            foundSites[i].Initialize(i);
            abandonedSites.Add(foundSites[i]);
            var pos = foundSites[i].transform.position;
            Debug.Log($"Found AbandonedSite at ({pos.x:F2}, {pos.y:F2})");
            GameLogPanel.Instance.LogBuildingStatus($"AbandonedSite_{i + 1} located at ({pos.x:F2}, {pos.y:F2})");
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
            mainCamera.orthographicSize = 10f;
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