using UnityEngine;
using TMPro;

public class BuildingStatsUI : MonoBehaviour
{
    [Header("Stats Panel")]
    public GameObject statsPanel;
    
    [Header("Kitchen Stats")]
    public TextMeshProUGUI kitchenInUseText;
    public TextMeshProUGUI kitchenTotalText;
    public TextMeshProUGUI kitchenNeedWorkerText;
    public TextMeshProUGUI kitchenConstructionText;
    
    [Header("Shelter Stats")]
    public TextMeshProUGUI shelterInUseText;
    public TextMeshProUGUI shelterTotalText;
    public TextMeshProUGUI shelterNeedWorkerText;
    public TextMeshProUGUI shelterConstructionText;
    
    [Header("Casework Stats")]
    public TextMeshProUGUI caseworkInUseText;
    public TextMeshProUGUI caseworkTotalText;
    public TextMeshProUGUI caseworkNeedWorkerText;
    public TextMeshProUGUI caseworkConstructionText;
    
    [Header("Building System Reference")]
    public BuildingSystem buildingSystem;
    
    [Header("Update Settings")]
    public float updateInterval = 1f; // Update every second when panel is open
    
    private bool isPanelOpen = false;
    private float lastUpdateTime = 0f;
    
    void Start()
    {
        // Hide panel initially
        //if (statsPanel != null)
        //    statsPanel.SetActive(false);
        
        // Subscribe to building events for real-time updates
        SubscribeToBuildingEvents();
        
        Debug.Log("BuildingStatsUI initialized");
    }
    
    void Update()
    {
        // Update stats periodically when panel is open
        if (isPanelOpen && Time.time - lastUpdateTime > updateInterval)
        {
            UpdateStatsDisplay();
            UpdateStatsWithColors(); // Optional: Update colors based on values
            lastUpdateTime = Time.time;
        }
    }
    
    void SubscribeToBuildingEvents()
    {
        // Find all existing buildings and subscribe to their events
        Building[] allBuildings = FindObjectsOfType<Building>();
        foreach (Building building in allBuildings)
        {
            SubscribeToBuilding(building);
        }
    }
    
    void SubscribeToBuilding(Building building)
    {
        // Since we don't have events in the Building class yet, we'll rely on periodic updates
        // In a more advanced system, you could add events to the Building class
    }
    
    public void ShowStatsPanel()
    {
        if (statsPanel != null)
        {
            statsPanel.SetActive(true);
            isPanelOpen = true;
            UpdateStatsDisplay(); // Immediate update when opening
            Debug.Log("Building stats panel opened");
        }
    }
    
    public void HideStatsPanel()
    {
        if (statsPanel != null)
        {
            statsPanel.SetActive(false);
            isPanelOpen = false;
            Debug.Log("Building stats panel closed");
        }
    }
    
    public void UpdateStatsDisplay()
    {
        if (buildingSystem == null)
        {
            Debug.LogWarning("BuildingSystem reference is null!");
            return;
        }
        
        // Get current statistics
        BuildingStatistics stats = buildingSystem.GetBuildingStatistics();
        
        // Update Kitchen stats
        UpdateTextSafe(kitchenInUseText, stats.kitchenStats.inUse.ToString());
        UpdateTextSafe(kitchenTotalText, stats.kitchenStats.GetTotal().ToString());
        UpdateTextSafe(kitchenNeedWorkerText, stats.kitchenStats.needWorker.ToString());
        UpdateTextSafe(kitchenConstructionText, stats.kitchenStats.underConstruction.ToString());
        
        // Update Shelter stats
        UpdateTextSafe(shelterInUseText, stats.shelterStats.inUse.ToString());
        UpdateTextSafe(shelterTotalText, stats.shelterStats.GetTotal().ToString());
        UpdateTextSafe(shelterNeedWorkerText, stats.shelterStats.needWorker.ToString());
        UpdateTextSafe(shelterConstructionText, stats.shelterStats.underConstruction.ToString());
        
        // Update Casework stats
        UpdateTextSafe(caseworkInUseText, stats.caseworkStats.inUse.ToString());
        UpdateTextSafe(caseworkTotalText, stats.caseworkStats.GetTotal().ToString());
        UpdateTextSafe(caseworkNeedWorkerText, stats.caseworkStats.needWorker.ToString());
        UpdateTextSafe(caseworkConstructionText, stats.caseworkStats.underConstruction.ToString());
        
        Debug.Log($"Stats updated - Total buildings: {stats.GetTotalBuildings()}, Operational: {stats.GetOperationalPercentage():F1}%");
    }
    
    void UpdateTextSafe(TextMeshProUGUI textComponent, string value)
    {
        if (textComponent != null)
        {
            textComponent.text = value;
        }
    }
    
    // Method to force immediate update (can be called from other scripts)
    public void ForceUpdateStats()
    {
        if (isPanelOpen)
        {
            UpdateStatsDisplay();
        }
    }
    
    // Method to check if panel is currently open
    public bool IsStatsPanelOpen()
    {
        return isPanelOpen;
    }
    
    void OnDestroy()
    {
        // Clean up any subscriptions if needed
    }

    // Optional: Add color coding for different stats
    void UpdateStatsWithColors()
    {
        if (buildingSystem == null) return;

        BuildingStatistics stats = buildingSystem.GetBuildingStatistics();

        // Color code stats based on values
        UpdateTextWithColor(kitchenInUseText, stats.kitchenStats.inUse.ToString(),
                           stats.kitchenStats.inUse > 0 ? Color.green : Color.gray);
        UpdateTextWithColor(kitchenNeedWorkerText, stats.kitchenStats.needWorker.ToString(),
                           stats.kitchenStats.needWorker > 0 ? Color.red : Color.gray);

        UpdateTextWithColor(caseworkInUseText, stats.caseworkStats.inUse.ToString(),
                           stats.caseworkStats.inUse > 0 ? Color.green : Color.gray);
        UpdateTextWithColor(caseworkNeedWorkerText, stats.caseworkStats.needWorker.ToString(),
                           stats.caseworkStats.needWorker > 0 ? Color.red : Color.gray);

        UpdateTextWithColor(shelterInUseText, stats.shelterStats.inUse.ToString(),
                           stats.shelterStats.inUse > 0 ? Color.green : Color.gray);
        UpdateTextWithColor(shelterNeedWorkerText, stats.shelterStats.needWorker.ToString(),
                           stats.shelterStats.needWorker > 0 ? Color.red : Color.gray);

    }
    
    void UpdateTextWithColor(TextMeshProUGUI textComponent, string value, Color color)
    {
        if (textComponent != null)
        {
            textComponent.text = value;
            textComponent.color = color;
        }
    }
}