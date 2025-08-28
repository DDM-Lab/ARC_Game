using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;
using System.Linq;
public enum BuildingType
{
    Kitchen,
    Shelter,
    CaseworkSite,
    Community,
    Motel
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

    [Header("Worker Requirements")]
    public int requiredWorkforce = 4; // Each building needs 4 workforce to operate

    [Header("Visual Components")]
    public SpriteRenderer buildingRenderer;
    public GameObject constructionProgressBar;
    public GameObject workerButton; // Button to open worker assignment UI

    [Header("Status Colors")]
    public Color constructionColor = Color.yellow;
    public Color needWorkerColor = Color.white;
    public Color inUseColor = Color.green;
    public Color disabledColor = Color.grey;

    [Header("System References")]
    public WorkerSystem workerSystem;
    [Header("UI Components")]
    public SpriteWorkforceIndicator mapWorkforceIndicator;

    private float constructionProgress = 0f;
    private Coroutine constructionCoroutine;

    public void Initialize(BuildingType type, int siteId)
    {
        buildingType = type;
        originalSiteId = siteId;
        currentStatus = BuildingStatus.UnderConstruction;

        // Ensure progress bar is visible for construction
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(true);

        // Hide worker button during construction
        if (workerButton != null)
            workerButton.SetActive(false);

        // Hide workforce indicator during construction
        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(false);

        // Start construction immediately
        StartConstruction();

        Debug.Log($"Building initialized: {buildingType} at original site {siteId}");
        ToastManager.ShowToast($"You chose to change an abandoned site at {originalSiteId} into {buildingType}", ToastType.Info, true);

    }

    void Start()
    {
        if (buildingRenderer == null)
            buildingRenderer = GetComponent<SpriteRenderer>();

        // Find WorkerSystem if not assigned
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();

        // Hide worker button initially (will be shown when construction completes)
        if (workerButton != null)
            workerButton.SetActive(false);

        // Hide workforce indicator initially (will be shown when construction completes)
        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(false);
    }

    public void StartConstruction(float constructionTime = 5f)
    {
        if (constructionCoroutine != null)
        {
            StopCoroutine(constructionCoroutine);
        }

        currentStatus = BuildingStatus.UnderConstruction;
        constructionProgress = 0f;

        // Show progress bar, hide worker button, hide workforce indicator
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(true);
        if (workerButton != null)
            workerButton.SetActive(false);
        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(false);

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

        // Hide progress bar, show worker button, show workforce indicator
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(false);
        if (workerButton != null)
            workerButton.SetActive(true);
        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(true);

        UpdateBuildingVisual();
        NotifyStatsUpdate();

        Debug.Log($"{buildingType} construction completed at site {originalSiteId} - Now needs worker assignment");
        ToastManager.ShowToast($"{buildingType} construction completed at site {originalSiteId} - Now needs worker assignment", ToastType.Success, true);

    }

    public void AssignWorker()
    {
        if (currentStatus == BuildingStatus.NeedWorker)
        {
            // Check if we have enough workforce assigned through WorkerSystem
            if (workerSystem != null)
            {
                var assignedWorkers = workerSystem.GetWorkersByBuildingId(originalSiteId);
                int totalWorkforce = 0;
                foreach (var worker in assignedWorkers)
                {
                    totalWorkforce += worker.WorkforceValue;
                }

                if (totalWorkforce >= requiredWorkforce)
                {
                    currentStatus = BuildingStatus.InUse;

                    // Hide worker button when in use
                    if (workerButton != null)
                        workerButton.SetActive(false);

                    UpdateBuildingVisual();
                    NotifyStatsUpdate();
                    Debug.Log($"{buildingType} at site {originalSiteId} is now in use with {totalWorkforce} workforce");
                    ToastManager.ShowToast($"{buildingType} at site {originalSiteId} is now operational with {totalWorkforce} workforce!", ToastType.Success, true);
                }
                else
                {
                    Debug.LogWarning($"Cannot activate {buildingType} - insufficient workforce. Required: {requiredWorkforce}, Available: {totalWorkforce}");
                    ToastManager.ShowToast($"Not enough workforce assigned! Required: {requiredWorkforce}, Available: {totalWorkforce}", ToastType.Warning, true);
                }
            }
            else
            {
                // Fallback for when WorkerSystem is not available
                currentStatus = BuildingStatus.InUse;
                if (workerButton != null)
                    workerButton.SetActive(false);

                UpdateBuildingVisual();
                NotifyStatsUpdate();
                Debug.LogWarning($"{buildingType} activated without WorkerSystem validation");
            }
        }
        else
        {
            Debug.LogWarning($"Cannot assign worker to {buildingType} - current status: {currentStatus}");
        }
    }

    /// <summary>
    /// Force update the workforce indicator display (called from external systems)
    /// </summary>
    public void UpdateWorkforceDisplay()
    {
        if (mapWorkforceIndicator != null && workerSystem != null)
        {
            mapWorkforceIndicator.UpdateFromBuilding(this, workerSystem);
        }
    }

    public void DisableBuilding()
    {
        if (currentStatus == BuildingStatus.InUse)
        {
            currentStatus = BuildingStatus.Disabled;

            // Show worker button when disabled (for potential reassignment)
            if (workerButton != null)
                workerButton.SetActive(true);

            // Release workers from this building
            if (workerSystem != null)
            {
                workerSystem.ReleaseWorkersFromBuilding(originalSiteId);
            }

            UpdateBuildingVisual();
            NotifyStatsUpdate();
            Debug.Log($"{buildingType} at site {originalSiteId} has been disabled and workers released");
            ToastManager.ShowToast($"{buildingType} at site {originalSiteId} has been disabled! Please repair and reassign workers.", ToastType.Warning, true);
        }
        else
        {
            Debug.LogWarning($"Cannot disable {buildingType} - current status: {currentStatus}");
            ToastManager.ShowToast($"Cannot disable {buildingType} - current status: {currentStatus}", ToastType.Warning, true);
        }
    }

    public void RepairBuilding()
    {
        if (currentStatus == BuildingStatus.Disabled)
        {
            currentStatus = BuildingStatus.NeedWorker;

            // Keep worker button visible for reassignment
            if (workerButton != null)
                workerButton.SetActive(true);

            UpdateBuildingVisual();
            NotifyStatsUpdate();
            Debug.Log($"{buildingType} at site {originalSiteId} has been repaired and needs worker reassignment");
            ToastManager.ShowToast($"{buildingType} at site {originalSiteId} has been repaired! Please reassign workers.", ToastType.Info, true);
        }
        else
        {
            Debug.LogWarning($"Cannot repair {buildingType} - current status: {currentStatus}");
            ToastManager.ShowToast($"Cannot repair {buildingType} - current status: {currentStatus}", ToastType.Warning, true);
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

        // update the workforce indicator on the map
        if (mapWorkforceIndicator != null && workerSystem != null)
        {
            mapWorkforceIndicator.UpdateFromBuilding(this, workerSystem);
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
    void OnMouseEnter()
    {
        if (FacilityInfoManager.Instance != null)
            FacilityInfoManager.Instance.OnFacilityHover(this, true);
    }

    void OnMouseExit()
    {
        if (FacilityInfoManager.Instance != null)
            FacilityInfoManager.Instance.OnFacilityHover(this, false);
    }

    void OnMouseDown()
    {
        // Check if over UI
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        if (FacilityInfoManager.Instance != null)
            FacilityInfoManager.Instance.OnFacilityClick(this);
    }

    /*
    void OnMouseDown()
    {
        // Check if pointer is over UI - if so, ignore map input
        if (EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }
        // Handle building interaction based on current status
        switch (currentStatus)
        {
            case BuildingStatus.UnderConstruction:
                Debug.Log($"{buildingType} is still under construction ({constructionProgress:P0} complete)");
                break;
            case BuildingStatus.NeedWorker:
                Debug.Log($"Opening worker assignment UI for {buildingType}");
                OpenWorkerAssignmentUI();
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

    public void OpenWorkerAssignmentUI()
    {
        if (workerSystem != null)
        {
            workerSystem.ShowWorkerAssignmentUI(this);
        }
        else
        {
            Debug.LogWarning("WorkerSystem not found - cannot open worker assignment UI");
        }
    }*/

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
    public int GetRequiredWorkforce() => requiredWorkforce;

    // Worker-related getters
    public int GetAssignedWorkforce()
    {
        if (workerSystem != null)
        {
            var assignedWorkers = workerSystem.GetWorkersByBuildingId(originalSiteId);
            int totalWorkforce = 0;
            foreach (var worker in assignedWorkers)
            {
                totalWorkforce += worker.WorkforceValue;
            }
            return totalWorkforce;
        }
        return 0;
    }

    public bool HasSufficientWorkforce()
    {
        return GetAssignedWorkforce() >= requiredWorkforce;
    }

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
        Debug.Log($"Kitchen producing food for {capacity} people with {GetAssignedWorkforce()} workforce");
        // Food production logic will be implemented later
    }

    protected virtual void ProvideShelter()
    {
        Debug.Log($"Shelter housing {capacity} people with {GetAssignedWorkforce()} workforce");
        // Shelter management logic will be implemented later
    }

    protected virtual void HandleCasework()
    {
        Debug.Log($"Casework site handling {capacity} cases with {GetAssignedWorkforce()} workforce");
        // Casework processing logic will be implemented later
    }
    
    [Header("Manual Task Debug")]
    public bool enableManualTasks = true;

    [ContextMenu("Manual: Request Food Delivery")]
    public void DebugRequestFoodDelivery()
    {
        if (!enableManualTasks) return;
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null)
        {
            Debug.LogError("DeliverySystem not found!");
            return;
        }
        
        // Find a kitchen with food
        Building[] kitchens = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Kitchen).ToArray();
        Building sourceKitchen = null;
        
        foreach (Building kitchen in kitchens)
        {
            BuildingResourceStorage storage = kitchen.GetComponent<BuildingResourceStorage>();
            if (storage != null && storage.GetResourceAmount(ResourceType.FoodPacks) > 0)
            {
                sourceKitchen = kitchen;
                break;
            }
        }
        
        if (sourceKitchen != null)
        {
            int requestAmount = 5; // Request 5 food packs
            deliverySystem.CreateDeliveryTask(sourceKitchen, this, ResourceType.FoodPacks, requestAmount, 5);
            Debug.Log($"{name} requested {requestAmount} food packs from {sourceKitchen.name}");
        }
        else
        {
            Debug.LogWarning($"{name} cannot request food - no kitchens with food available");
        }
    }

    [ContextMenu("Manual: Send Food to Shelter")]
    public void DebugSendFoodToShelter()
    {
        if (!enableManualTasks || GetBuildingType() != BuildingType.Kitchen) return;
        
        BuildingResourceStorage storage = GetComponent<BuildingResourceStorage>();
        if (storage == null || storage.GetResourceAmount(ResourceType.FoodPacks) <= 0)
        {
            Debug.LogWarning($"{name} has no food to send");
            return;
        }
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        // Find a shelter that needs food
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        Building targetShelter = null;
        
        foreach (Building shelter in shelters)
        {
            BuildingResourceStorage shelterStorage = shelter.GetComponent<BuildingResourceStorage>();
            if (shelterStorage != null && shelterStorage.GetAvailableSpace(ResourceType.FoodPacks) > 0)
            {
                targetShelter = shelter;
                break;
            }
        }
        
        if (targetShelter != null)
        {
            int sendAmount = Mathf.Min(storage.GetResourceAmount(ResourceType.FoodPacks), 3);
            deliverySystem.CreateDeliveryTask(this, targetShelter, ResourceType.FoodPacks, sendAmount, 5);
            Debug.Log($"{name} sending {sendAmount} food packs to {targetShelter.name}");
        }
        else
        {
            Debug.LogWarning($"{name} cannot send food - no shelters available");
        }
    }

    [ContextMenu("Manual: Request Population")]
    public void DebugRequestPopulation()
    {
        if (!enableManualTasks) return;
        
        BuildingResourceStorage storage = GetComponent<BuildingResourceStorage>();
        if (storage == null || storage.GetAvailableSpace(ResourceType.Population) <= 0)
        {
            Debug.LogWarning($"{name} has no space for more population");
            return;
        }
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        // Find a community with people
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Community).ToArray();
        PrebuiltBuilding sourceCommunity = null;
        
        foreach (PrebuiltBuilding community in communities)
        {
            if (community.GetCurrentPopulation() > 0)
            {
                sourceCommunity = community;
                break;
            }
        }
        
        if (sourceCommunity != null)
        {
            int requestAmount = Mathf.Min(3, storage.GetAvailableSpace(ResourceType.Population), sourceCommunity.GetCurrentPopulation());
            deliverySystem.CreateDeliveryTask(sourceCommunity, this, ResourceType.Population, requestAmount, 5);
            Debug.Log($"{name} requested {requestAmount} people from {sourceCommunity.GetBuildingName()}");
        }
        else
        {
            Debug.LogWarning($"{name} cannot request population - no communities with people available");
        }
    }

    [ContextMenu("Manual: Print Building Status")]
    public void DebugPrintBuildingStatus()
    {
        Debug.Log($"=== {name} DEBUG STATUS ===");
        Debug.Log($"Building Type: {GetBuildingType()}");
        Debug.Log($"Current Status: {GetCurrentStatus()}");
        Debug.Log($"Workforce: {GetAssignedWorkforce()}/{GetRequiredWorkforce()}");
        
        BuildingResourceStorage storage = GetComponent<BuildingResourceStorage>();
        if (storage != null)
        {
            Debug.Log($"Resources: {storage.GetResourceSummary()}");
        }
        
        RoadConnection roadConn = GetComponent<RoadConnection>();
        if (roadConn != null)
        {
            Debug.Log($"Road Connected: {roadConn.IsConnectedToRoad}");
        }
    }

}