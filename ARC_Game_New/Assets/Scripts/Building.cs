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

    private float constructionProgress = 0f;
    private Coroutine constructionCoroutine;

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

        // Hide worker button during construction
        if (workerButton != null)
            workerButton.SetActive(false);

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

        // Show progress bar, hide worker button
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(true);
        if (workerButton != null)
            workerButton.SetActive(false);

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

        // Hide progress bar, show worker button
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(false);
        if (workerButton != null)
            workerButton.SetActive(true);

        UpdateBuildingVisual();
        NotifyStatsUpdate();

        Debug.Log($"{buildingType} construction completed at site {originalSiteId} - Now needs worker assignment");
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
                }
                else
                {
                    Debug.LogWarning($"Cannot activate {buildingType} - insufficient workforce. Required: {requiredWorkforce}, Available: {totalWorkforce}");
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
            currentStatus = BuildingStatus.NeedWorker;

            // Keep worker button visible for reassignment
            if (workerButton != null)
                workerButton.SetActive(true);

            UpdateBuildingVisual();
            NotifyStatsUpdate();
            Debug.Log($"{buildingType} at site {originalSiteId} has been repaired and needs worker reassignment");
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
    
    /*
    void OnMouseDown()
    {
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

}