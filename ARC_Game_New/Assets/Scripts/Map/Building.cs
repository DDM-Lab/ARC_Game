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
    Disabled,
    Deconstructing  // Added deconstruction status
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

    [Header("Status Colors")]
    public Color constructionColor = Color.yellow;
    public Color needWorkerColor = Color.white;
    public Color inUseColor = Color.green;
    public Color disabledColor = Color.grey;

    [Header("Deconstruction Visuals")]
    public SpriteRenderer deconstructionStartingSpriteRenderer;
    public Color deconstructionColor = new Color(1f, 0.3f, 0.3f, 1f); // Red for deconstruction

    [Header("System References")]
    public WorkerSystem workerSystem;
    
    [Header("UI Components")]
    public SpriteWorkforceIndicator mapWorkforceIndicator;

    [Header("Deconstruction Settings")] // NEW SECTION
    public float deconstructionTime = 3f;
    private float deconstructionProgress = 0f;
    private Coroutine deconstructionCoroutine;

    private float constructionProgress = 0f;
    private Coroutine constructionCoroutine;
    private AbandonedSite abandonedSiteComponent; // Reference to AbandonedSite

    public void Initialize(BuildingType type, int siteId)
    {
        buildingType = type;
        originalSiteId = siteId;
        currentStatus = BuildingStatus.UnderConstruction;

        // Ensure progress bar is visible for construction
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(true);

        // Hide workforce indicator during construction
        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(false);

        // Start construction immediately
        StartConstruction();

        Debug.Log($"Player chose to convert site {originalSiteId} into {buildingType}. Construction will start during simulation period.");
        GameLogPanel.Instance.LogBuildingStatus($"Player chose to convert site {originalSiteId} into {buildingType}. Construction will start during simulation period.");
        //ToastManager.ShowToast($"You chose to change an abandoned site at {originalSiteId} into {buildingType}. Currently Under Construction.", ToastType.Info, true);
    }

    void Start()
    {
        if (buildingRenderer == null)
            buildingRenderer = GetComponent<SpriteRenderer>();

        // Find WorkerSystem if not assigned
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();

        // Hide workforce indicator initially (will be shown when construction completes)
        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(false);

        if (WorkerSystem.Instance != null)
        {
            WorkerSystem.Instance.OnWorkerStatsChanged += UpdateWorkforceIndicator;
        }
    }
    void UpdateWorkforceIndicator()
    {
        SpriteWorkforceIndicator indicator = GetComponentInChildren<SpriteWorkforceIndicator>();
        if (indicator != null && WorkerSystem.Instance != null)
        {
            indicator.UpdateFromBuilding(this, WorkerSystem.Instance);
        }
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

        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(true);

        UpdateBuildingVisual();
        NotifyStatsUpdate();

        // Report to daily tracking
        if (DailyReportData.Instance != null)
        {
            DailyReportData.Instance.RecordBuildingConstructed();
        }

        Debug.Log($"{buildingType} construction completed at site {originalSiteId} - Now needs worker assignment");
        GameLogPanel.Instance.LogBuildingStatus($"{buildingType} construction completed at site {originalSiteId} - Now needs worker assignment");
        //ToastManager.ShowToast($"{buildingType} construction completed at site {originalSiteId} - Now needs worker assignment", ToastType.Success, true);
    }

    // Start Deconstruction
    public void StartDeconstruction()
    {
        if (currentStatus != BuildingStatus.InUse)
        {
            Debug.LogWarning($"Cannot deconstruct {buildingType}: building is not in use (current status: {currentStatus})");
            return;
        }

        // Release all workers immediately
        ReleaseAllWorkers();

        // Change status to deconstructing
        currentStatus = BuildingStatus.Deconstructing;
        deconstructionProgress = 0f;

        // Show progress bar
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(true);

        // Hide workforce indicator
        if (mapWorkforceIndicator != null)
            mapWorkforceIndicator.gameObject.SetActive(false);

        // Start deconstruction coroutine
        if (deconstructionCoroutine != null)
        {
            StopCoroutine(deconstructionCoroutine);
        }
        deconstructionCoroutine = StartCoroutine(DeconstructionCoroutine());

        UpdateBuildingVisual();

        Debug.Log($"{buildingType} at site {originalSiteId} deconstruction started");
        GameLogPanel.Instance.LogBuildingStatus($"{buildingType} at site {originalSiteId} deconstruction started");
        ToastManager.ShowToast($"{buildingType} deconstruction started - responders released. ", ToastType.Info, true);
    }

    // Deconstruction Coroutine
    IEnumerator DeconstructionCoroutine()
    {
        float elapsedTime = 0f;

        while (elapsedTime < deconstructionTime)
        {
            elapsedTime += Time.deltaTime;
            deconstructionProgress = elapsedTime / deconstructionTime;

            // Update progress bar
            UpdateDeconstructionProgress(deconstructionProgress);
            UpdateBuildingVisual();

            yield return null;
        }

        // Deconstruction completed
        CompleteDeconstruction();
    }

    // Update Deconstruction Progress
    void UpdateDeconstructionProgress(float progress)
    {
        deconstructionProgress = Mathf.Clamp01(progress);

        // Update progress bar visual if exists (same visual as construction)
        if (constructionProgressBar != null)
        {
            Transform progressFill = constructionProgressBar.transform.Find("Fill");
            if (progressFill != null)
            {
                // Scale from left edge
                progressFill.localScale = new Vector3(deconstructionProgress, 1f, 1f);

                // Adjust position to make it grow from left edge
                float offset = (1f - deconstructionProgress) * 0.5f;
                Vector3 originalPos = progressFill.localPosition;
                progressFill.localPosition = new Vector3(-offset, originalPos.y, originalPos.z);

                // Change color to deconstruction color
                SpriteRenderer fillRenderer = progressFill.GetComponent<SpriteRenderer>();
                if (fillRenderer != null)
                {
                    fillRenderer.color = deconstructionColor;
                    if (deconstructionStartingSpriteRenderer != null)
                    {
                        deconstructionStartingSpriteRenderer.color = deconstructionColor;
                    }
                }
            }
        }
    }

    // Complete Deconstruction
    void CompleteDeconstruction()
    {
        Debug.Log($"{buildingType} at site {originalSiteId} deconstruction completed - reverted to AbandonedSite");
        GameLogPanel.Instance.LogBuildingStatus($"{buildingType} at site {originalSiteId} deconstruction completed - reverted to AbandonedSite");
        //ToastManager.ShowToast($"{buildingType} deconstructed - site is now abandoned again", ToastType.Info, true);

        // Find the BuildingSystem to handle deconstruction properly
        BuildingSystem buildingSystem = FindObjectOfType<BuildingSystem>();
        if (buildingSystem != null)
        {
            // Use BuildingSystem's existing deconstruction method
            // which knows how to find and restore the original AbandonedSite
            buildingSystem.DeconstructBuilding(this);

            Debug.Log("Deconstruction handled by BuildingSystem");
            return; // BuildingSystem will handle destroying this building
        }
        else
        {
            Debug.LogError("BuildingSystem not found! Cannot properly deconstruct building.");

            // Fallback: just destroy the building GameObject
            if (constructionProgressBar != null)
                Destroy(constructionProgressBar);

            if (mapWorkforceIndicator != null)
                Destroy(mapWorkforceIndicator.gameObject);

            // Notify UI before destroying
            BuildingSystemUIIntegration uiIntegration = FindObjectOfType<BuildingSystemUIIntegration>();
            if (uiIntegration != null)
            {
                uiIntegration.NotifyBuildingDestroyed(this);
            }

            Destroy(gameObject); // Destroy the entire building GameObject
        }
    }

    // Release All Workers
    void ReleaseAllWorkers()
    {
        if (workerSystem == null)
        {
            Debug.LogWarning("WorkerSystem not found - cannot release workers");
            return;
        }

        // Use existing WorkerSystem method to release workers
        workerSystem.ReleaseWorkersFromBuilding(originalSiteId);
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

                    UpdateBuildingVisual();
                    NotifyStatsUpdate();
                    Debug.Log($"{buildingType} at site {originalSiteId} is now in use with {totalWorkforce} workforce");
                    GameLogPanel.Instance.LogBuildingStatus($"Player assigned workers. {buildingType} at site {originalSiteId} is now operational with {totalWorkforce} workforce");
                    //ToastManager.ShowToast($"{buildingType} at site {originalSiteId} is now operational with {totalWorkforce} workforce!", ToastType.Success, true);
                }
                else
                {
                    Debug.LogWarning($"Cannot activate {buildingType} - insufficient workforce. Required: {requiredWorkforce}, Available: {totalWorkforce}");
                    GameLogPanel.Instance.LogError($"Player cannot activate {buildingType} - insufficient workforce. Required: {requiredWorkforce}, Available: {totalWorkforce}");
                    //ToastManager.ShowToast($"Not enough workforce assigned! Required: {requiredWorkforce}, Available: {totalWorkforce}", ToastType.Warning, true);
                }
            }
        }
    }

    public void UpdateWorkerStatus()
    {
        if (currentStatus == BuildingStatus.InUse || currentStatus == BuildingStatus.NeedWorker)
        {
            if (workerSystem != null)
            {
                var assignedWorkers = workerSystem.GetWorkersByBuildingId(originalSiteId);
                int totalWorkforce = 0;
                foreach (var worker in assignedWorkers)
                {
                    totalWorkforce += worker.WorkforceValue;
                }

                if (totalWorkforce >= requiredWorkforce && currentStatus == BuildingStatus.NeedWorker)
                {
                    currentStatus = BuildingStatus.InUse;
                    UpdateBuildingVisual();
                    NotifyStatsUpdate();
                    Debug.Log($"{buildingType} at site {originalSiteId} activated with {totalWorkforce} workforce");
                    GameLogPanel.Instance.LogBuildingStatus($"{buildingType} at site {originalSiteId} activated with {totalWorkforce} workforce");
                }
                else if (totalWorkforce < requiredWorkforce && currentStatus == BuildingStatus.InUse)
                {
                    currentStatus = BuildingStatus.NeedWorker;
                    UpdateBuildingVisual();
                    NotifyStatsUpdate();
                    Debug.LogWarning($"{buildingType} at site {originalSiteId} deactivated - insufficient workforce");
                    GameLogPanel.Instance.LogBuildingStatus($"{buildingType} at site {originalSiteId} deactivated - insufficient workforce");
                    ToastManager.ShowToast($"{buildingType} is not functional - it needs more responders to operate!", ToastType.Warning, true);
                }
            }
        }
    }

    void NotifyStatsUpdate()
    {
        // Try to find the stats manager - use a more flexible approach
        MonoBehaviour[] allComponents = FindObjectsOfType<MonoBehaviour>();
        foreach (var component in allComponents)
        {
            if (component.GetType().Name == "BuildingStatsUIManager")
            {
                component.SendMessage("UpdateBuildingStats", SendMessageOptions.DontRequireReceiver);
                break;
            }
        }
    }

    void UpdateBuildingVisual()
    {
        if (buildingRenderer == null) return;

        switch (currentStatus)
        {
            case BuildingStatus.UnderConstruction:
                buildingRenderer.color = constructionColor;
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
            case BuildingStatus.Deconstructing: // NEW
                buildingRenderer.color = deconstructionColor;
                break;
        }
    }

    void OnMouseDown()
    {
        // Check if over UI
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        if (FacilityInfoManager.Instance != null)
        {
            FacilityInfoManager.Instance.OnFacilityClick(this);
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
    public bool IsDeconstructing() => currentStatus == BuildingStatus.Deconstructing; // NEW
    public float GetConstructionProgress() => constructionProgress;
    public float GetDeconstructionProgress() => deconstructionProgress; // NEW
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

    void OnDestroy()
    {
        if (WorkerSystem.Instance != null)
        {
            WorkerSystem.Instance.OnWorkerStatsChanged -= UpdateWorkforceIndicator;
        }
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
            Debug.LogError("DeliverySystem not found in Building.cs");
            GameLogPanel.Instance.LogError("DeliverySystem not found in Building.cs");
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

    // Debug method for testing deconstruction
    [ContextMenu("Manual: Start Deconstruction")]
    public void DebugStartDeconstruction()
    {
        if (enableManualTasks)
        {
            StartDeconstruction();
        }
    }
}