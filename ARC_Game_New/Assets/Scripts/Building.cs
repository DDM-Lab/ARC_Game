using UnityEngine;
using System.Collections;

public enum BuildingType
{
    Kitchen,
    Shelter,
    CaseworkSite
}

public enum BuildingStatus
{
    UnderConstruction,
    NeedWorker,
    InUse,
    Disabled
}

public class Building : MonoBehaviour
{
    [Header("Building Information")]
    [SerializeField] private BuildingType buildingType;
    [SerializeField] private int originalSiteId;
    [SerializeField] private BuildingStatus currentStatus = BuildingStatus.UnderConstruction;
    
    [Header("Building Stats")]
    public int capacity = 10;
    public float operationalEfficiency = 1.0f;
    
    [Header("Visual Components")]
    public SpriteRenderer buildingRenderer;
    public GameObject constructionProgressBar;
    
    [Header("Status Colors")]
    public Color constructionColor = Color.yellow;
    public Color needWorkerColor = Color.white;
    public Color inUseColor = Color.green;
    public Color disabledColor = Color.grey;
    
    private float constructionProgress = 0f;
    private Coroutine constructionCoroutine;
    
    void Start()
    {
        if (buildingRenderer == null)
            buildingRenderer = GetComponent<SpriteRenderer>();
        
        // Don't hide progress bar in Start() - let Initialize() handle it
        // Progress bar visibility will be controlled by construction state
    }
    
    public void Initialize(BuildingType type, int siteId)
    {
        buildingType = type;
        originalSiteId = siteId;
        currentStatus = BuildingStatus.UnderConstruction;
        
        // Set building-specific properties
        SetBuildingTypeProperties();
        
        // Ensure progress bar is visible for construction
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(true);
        
        // Start construction immediately
        StartConstruction();
        
        Debug.Log($"Building initialized: {buildingType} at original site {siteId}");
    }
    
    public void StartConstruction(float constructionTime = 5f)
    {
        if (constructionCoroutine != null)
        {
            StopCoroutine(constructionCoroutine);
        }
        
        currentStatus = BuildingStatus.UnderConstruction;
        constructionProgress = 0f;
        
        // Show progress bar
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(true);
        
        // Start construction
        constructionCoroutine = StartCoroutine(ConstructionCoroutine(constructionTime));
        
        UpdateBuildingVisual();
    }
    
    IEnumerator ConstructionCoroutine(float constructionTime)
    {
        float elapsedTime = 0f;
        
        while (elapsedTime < constructionTime)
        {
            elapsedTime += Time.deltaTime;
            constructionProgress = elapsedTime / constructionTime;
            
            // Update progress bar
            UpdateConstructionProgress(constructionProgress);
            UpdateBuildingVisual();
            
            yield return null;
        }
        
        // Construction completed
        CompleteConstruction();
    }
    
    void UpdateConstructionProgress(float progress)
    {
        constructionProgress = Mathf.Clamp01(progress);
        
        // Update progress bar visual if exists
        if (constructionProgressBar != null)
        {
            Transform progressFill = constructionProgressBar.transform.Find("Fill");
            if (progressFill != null)
            {
                // Scale from left edge instead of center
                progressFill.localScale = new Vector3(constructionProgress, 1f, 1f);
                
                // Adjust position to make it grow from left edge
                float offset = (1f - constructionProgress) * 0.5f; // Half of the missing width
                Vector3 originalPos = progressFill.localPosition;
                progressFill.localPosition = new Vector3(-offset, originalPos.y, originalPos.z);
            }
        }
    }
    
    void CompleteConstruction()
    {
        currentStatus = BuildingStatus.NeedWorker;
        constructionProgress = 1f;
        
        // Hide progress bar
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(false);
        
        UpdateBuildingVisual();
        NotifyStatsUpdate();
        
        Debug.Log($"{buildingType} construction completed at site {originalSiteId} - Now needs worker assignment");
    }
    
    public void AssignWorker()
    {
        if (currentStatus == BuildingStatus.NeedWorker)
        {
            currentStatus = BuildingStatus.InUse;
            UpdateBuildingVisual();
            NotifyStatsUpdate();
            Debug.Log($"{buildingType} at site {originalSiteId} is now in use");
        }
        else
        {
            Debug.LogWarning($"Cannot assign worker to {buildingType} - current status: {currentStatus}");
        }
    }
    
    public void DisableBuilding()
    {
        if (currentStatus == BuildingStatus.InUse)
        {
            currentStatus = BuildingStatus.Disabled;
            UpdateBuildingVisual();
            NotifyStatsUpdate();
            Debug.Log($"{buildingType} at site {originalSiteId} has been disabled");
        }
        else
        {
            Debug.LogWarning($"Cannot disable {buildingType} - current status: {currentStatus}");
        }
    }
    
    public void RepairBuilding()
    {
        if (currentStatus == BuildingStatus.Disabled)
        {
            currentStatus = BuildingStatus.InUse;
            UpdateBuildingVisual();
            NotifyStatsUpdate();
            Debug.Log($"{buildingType} at site {originalSiteId} has been repaired and is back in use");
        }
        else
        {
            Debug.LogWarning($"Cannot repair {buildingType} - current status: {currentStatus}");
        }
    }
    
    void NotifyStatsUpdate()
    {
        // Find and notify BuildingStatsUI to update
        BuildingStatsUI statsUI = FindObjectOfType<BuildingStatsUI>();
        if (statsUI != null)
        {
            statsUI.ForceUpdateStats();
        }
    }
    
    void SetBuildingTypeProperties()
    {
        switch (buildingType)
        {
            case BuildingType.Kitchen:
                capacity = 20; // Can serve 20 people
                break;
            case BuildingType.Shelter:
                capacity = 15; // Can house 15 people
                break;
            case BuildingType.CaseworkSite:
                capacity = 8; // Can handle 8 cases simultaneously
                break;
        }
    }
    
    void UpdateBuildingVisual()
    {
        if (buildingRenderer == null) return;
        
        switch (currentStatus)
        {
            case BuildingStatus.UnderConstruction:
                // Lerp between construction color and need worker color based on progress
                buildingRenderer.color = Color.Lerp(constructionColor, needWorkerColor, constructionProgress);
                break;
            case BuildingStatus.NeedWorker:
                buildingRenderer.color = needWorkerColor;
                break;
            case BuildingStatus.InUse:
                buildingRenderer.color = inUseColor;
                break;
            case BuildingStatus.Disabled:
                buildingRenderer.color = disabledColor;
                break;
        }
    }
    
    void OnMouseDown()
    {
        // Handle building interaction based on current status
        switch (currentStatus)
        {
            case BuildingStatus.UnderConstruction:
                Debug.Log($"{buildingType} is still under construction ({constructionProgress:P0} complete)");
                break;
            case BuildingStatus.NeedWorker:
                Debug.Log($"Assigning worker to {buildingType}");
                AssignWorker();
                break;
            case BuildingStatus.InUse:
                Debug.Log($"Disabling {buildingType} (simulating event)");
                DisableBuilding();
                break;
            case BuildingStatus.Disabled:
                Debug.Log($"Repairing {buildingType}");
                RepairBuilding();
                break;
        }
    }
    
    // Getters
    public BuildingType GetBuildingType() => buildingType;
    public int GetOriginalSiteId() => originalSiteId;
    public BuildingStatus GetCurrentStatus() => currentStatus;
    public bool IsOperational() => currentStatus == BuildingStatus.InUse;
    public bool IsUnderConstruction() => currentStatus == BuildingStatus.UnderConstruction;
    public bool NeedsWorker() => currentStatus == BuildingStatus.NeedWorker;
    public bool IsDisabled() => currentStatus == BuildingStatus.Disabled;
    public float GetConstructionProgress() => constructionProgress;
    public int GetCapacity() => capacity;
    public float GetEfficiency() => operationalEfficiency;
    
    // Building functionality methods (to be expanded later)
    public virtual void PerformBuildingFunction()
    {
        if (currentStatus != BuildingStatus.InUse) return;
        
        switch (buildingType)
        {
            case BuildingType.Kitchen:
                ProduceFood();
                break;
            case BuildingType.Shelter:
                ProvideShelter();
                break;
            case BuildingType.CaseworkSite:
                HandleCasework();
                break;
        }
    }
    
    protected virtual void ProduceFood()
    {
        Debug.Log($"Kitchen producing food for {capacity} people");
        // Food production logic will be implemented later
    }
    
    protected virtual void ProvideShelter()
    {
        Debug.Log($"Shelter housing {capacity} people");
        // Shelter management logic will be implemented later
    }
    
    protected virtual void HandleCasework()
    {
        Debug.Log($"Casework site handling {capacity} cases");
        // Casework processing logic will be implemented later
    }
    
}