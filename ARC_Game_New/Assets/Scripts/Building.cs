using UnityEngine;
using System.Collections;

public enum BuildingType
{
    Kitchen,
    Shelter,
    CaseworkSite
}

public class Building : MonoBehaviour
{
    [Header("Building Information")]
    [SerializeField] private BuildingType buildingType;
    [SerializeField] private int originalSiteId;
    [SerializeField] private bool isOperational = false; // Start as non-operational during construction
    [SerializeField] private bool isUnderConstruction = true;
    
    [Header("Building Stats")]
    public int capacity = 10;
    public float operationalEfficiency = 1.0f;
    
    [Header("Visual Components")]
    public SpriteRenderer buildingRenderer;
    public GameObject constructionProgressBar;
    
    [Header("Construction Settings")]
    public Color constructionColor = Color.yellow;
    public Color operationalColor = Color.green;
    
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
        isUnderConstruction = true;
        isOperational = false;
        
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
        
        isUnderConstruction = true;
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
        isUnderConstruction = false;
        isOperational = true;
        constructionProgress = 1f;
        
        // Hide progress bar
        if (constructionProgressBar != null)
            constructionProgressBar.SetActive(false);
        
        UpdateBuildingVisual();
        
        Debug.Log($"{buildingType} construction completed at site {originalSiteId}");
    }
    
    void SetBuildingTypeProperties()
    {
        switch (buildingType)
        {
            case BuildingType.Kitchen:
                capacity = 20; // Can serve 20 people
                operationalColor = Color.red;
                break;
            case BuildingType.Shelter:
                capacity = 15; // Can house 15 people
                operationalColor = Color.blue;
                break;
            case BuildingType.CaseworkSite:
                capacity = 8; // Can handle 8 cases simultaneously
                operationalColor = Color.green;
                break;
        }
    }
    
    void UpdateBuildingVisual()
    {
        if (buildingRenderer == null) return;
        
        if (isUnderConstruction)
        {
            // Lerp between construction color and final color based on progress
            buildingRenderer.color = Color.Lerp(constructionColor, operationalColor, constructionProgress);
        }
        else if (isOperational)
        {
            buildingRenderer.color = operationalColor;
        }
        else
        {
            // Building exists but not operational (shouldn't happen normally)
            Color disabledColor = operationalColor;
            disabledColor.a = 0.5f;
            buildingRenderer.color = disabledColor;
        }
    }
    
    void OnMouseDown()
    {
        // Handle building interaction
        if (isOperational)
        {
            Debug.Log($"Clicked on operational {buildingType} (Site {originalSiteId}) - Capacity: {capacity}");
            PerformBuildingFunction();
        }
        else if (isUnderConstruction)
        {
            Debug.Log($"{buildingType} is still under construction ({constructionProgress:P0} complete)");
        }
    }
    
    // Getters
    public BuildingType GetBuildingType() => buildingType;
    public int GetOriginalSiteId() => originalSiteId;
    public bool IsOperational() => isOperational;
    public bool IsUnderConstruction() => isUnderConstruction;
    public float GetConstructionProgress() => constructionProgress;
    public int GetCapacity() => capacity;
    public float GetEfficiency() => operationalEfficiency;
    
    // Building functionality methods (to be expanded later)
    public virtual void PerformBuildingFunction()
    {
        if (!isOperational) return;
        
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
    
    // Debug visualization
    void OnDrawGizmosSelected()
    {
        Gizmos.color = isOperational ? Color.cyan : Color.yellow;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.7f);
        
        if (Application.isPlaying)
        {
            Vector3 labelPos = transform.position + Vector3.up * 1.2f;
            string status = isUnderConstruction ? $"Building ({constructionProgress:P0})" : 
                           isOperational ? "Operational" : "Inactive";
            UnityEditor.Handles.Label(labelPos, $"{buildingType}\nCapacity: {capacity}\n{status}");
        }
    }
}