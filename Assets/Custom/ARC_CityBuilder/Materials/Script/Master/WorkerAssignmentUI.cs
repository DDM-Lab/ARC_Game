using System.Collections;
using System.Collections.Generic;
using CityBuilderCore;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WorkerAssignmentUI : MonoBehaviour
{
    [Header("UI References")]
    public Transform workerListContainer;
    public GameObject workerEntryPrefab;
    public TextMeshProUGUI totalAvailableWorkersText;
    public GameObject workerAssignmentPanel;

    [Header("Facility Settings")]
    public Sprite defaultFacilitySprite;

    [Header("UI Elements to Hide During Worker Assignment")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();
    
    [Header("Transition Settings")]
    public float transitionDuration = 0.3f;
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Debug Options")]
    public bool showDebugMessages = true;

    private MasterGameManager _gameManager;
    private WorkerManager _workerManager;
    private List<WorkerUIController> _workerEntries = new List<WorkerUIController>();
    private int _totalAvailableWorkers;
    
    // For smooth transitions
    private List<CanvasGroup> _hiddenUICanvasGroups = new List<CanvasGroup>();
    private Coroutine _transitionCoroutine;

    private void Start()
    {
        _gameManager = MasterGameManager.Instance;

        if (_gameManager != null)
        {
            // Subscribe to phase changes
            _gameManager.OnPhaseChanged += OnPhaseChanged;
            
            // Get worker manager reference
            _workerManager = _gameManager.workerManager;
            
            if (_workerManager != null)
            {
                // Subscribe to worker assignment changes
                _workerManager.OnWorkerAssignmentChanged += OnWorkerAssignmentChanged;
            }
        }

        // Initially hide the panel
        if (workerAssignmentPanel != null)
        {
            workerAssignmentPanel.SetActive(false);
        }
        
        // Prepare UI elements for smooth transitions
        PrepareUIElementsForTransitions();
        
        DebugLog("WorkerAssignmentUI initialized");
    }

    private void OnDestroy()
    {
        if (_gameManager != null)
        {
            _gameManager.OnPhaseChanged -= OnPhaseChanged;
        }
        
        if (_workerManager != null)
        {
            _workerManager.OnWorkerAssignmentChanged -= OnWorkerAssignmentChanged;
        }
        
        // Stop any running transition
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
        }
    }

    /// <summary>
    /// Prepare UI elements by adding CanvasGroup components for smooth transitions
    /// </summary>
    private void PrepareUIElementsForTransitions()
    {
        _hiddenUICanvasGroups.Clear();
        
        foreach (GameObject uiElement in uiElementsToHide)
        {
            if (uiElement != null)
            {
                // Add CanvasGroup if it doesn't exist
                CanvasGroup canvasGroup = uiElement.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = uiElement.AddComponent<CanvasGroup>();
                }
                
                // Ensure initial state is visible
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
                
                _hiddenUICanvasGroups.Add(canvasGroup);
            }
        }
        
        DebugLog($"Prepared {_hiddenUICanvasGroups.Count} UI elements for smooth transitions");
    }

    private void OnPhaseChanged(GlobalEnums.GamePhase newPhase)
    {
        // Show/hide this UI based on phase
        bool shouldShow = newPhase == GlobalEnums.GamePhase.WorkerAssignment;
    
        DebugLog($"Phase changed to {newPhase}, showing worker UI: {shouldShow}");
    
        if (workerAssignmentPanel != null)
        {
            workerAssignmentPanel.SetActive(shouldShow);
        }

        if (shouldShow)
        {
            // Only populate if we don't have entries or if facilities changed
            if (_workerEntries.Count == 0 || FacilitiesChanged())
            {
                PopulateFacilityList();
            }
            else
            {
                // Just refresh existing entries with current data
                RefreshExistingEntries();
            }
            UpdateTotalAvailableWorkersDisplay();
        }
    }
    
    /// <summary>
    /// Check if facilities have changed since last population
    /// </summary>
    private bool FacilitiesChanged()
    {
        if (_gameManager?.buildingSystem == null) return false;
    
        var currentFacilities = _gameManager.buildingSystem.GetAllFacilities();
        return currentFacilities.Count != _workerEntries.Count;
    }
    
    /// <summary>
    /// Refresh existing entries with current worker counts
    /// </summary>
    private void RefreshExistingEntries()
    {
        foreach (var entry in _workerEntries)
        {
            if (entry != null && _workerManager != null)
            {
                string facilityId = entry.GetFacilityId();
                int currentWorkerCount = _workerManager.GetCurrentWorkerCount(facilityId);
                entry.SetWorkerCount(currentWorkerCount);
            }
        }
    
        UpdateAllWorkerEntries();
        DebugLog("Refreshed existing worker entries with current data");
    }

    /// <summary>
    /// Smoothly transition other UI elements in or out
    /// </summary>
    private IEnumerator TransitionOtherUIElements(bool show)
    {
        float startAlpha = show ? 0f : 1f;
        float targetAlpha = show ? 1f : 0f;
        bool startInteractable = show ? false : true;
        bool targetInteractable = show ? true : false;
        
        DebugLog($"Starting smooth transition: show={show}, from alpha {startAlpha} to {targetAlpha}");
        
        // Set initial state
        foreach (CanvasGroup canvasGroup in _hiddenUICanvasGroups)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = startAlpha;
                canvasGroup.interactable = startInteractable;
                canvasGroup.blocksRaycasts = startInteractable;
            }
        }
        
        // Animate transition
        float elapsedTime = 0f;
        
        while (elapsedTime < transitionDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / transitionDuration;
            float easedProgress = transitionCurve.Evaluate(progress);
            float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, easedProgress);
            
            foreach (CanvasGroup canvasGroup in _hiddenUICanvasGroups)
            {
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = currentAlpha;
                }
            }
            
            yield return null;
        }
        
        // Ensure final state
        foreach (CanvasGroup canvasGroup in _hiddenUICanvasGroups)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = targetAlpha;
                canvasGroup.interactable = targetInteractable;
                canvasGroup.blocksRaycasts = targetInteractable;
            }
        }
        
        DebugLog($"Transition completed: final alpha={targetAlpha}, interactable={targetInteractable}");
        _transitionCoroutine = null;
    }

    private void OnWorkerAssignmentChanged()
    {
        // Update UI when worker assignments change
        UpdateTotalAvailableWorkersDisplay();
        UpdateAllWorkerEntries();
    }

    private void PopulateFacilityList()
    {
        // Clear existing entries
        ClearWorkerEntries();

        if (_gameManager == null || _gameManager.buildingSystem == null)
        {
            Debug.LogWarning("[WorkerAssignmentUI] GameManager or BuildingSystem not available");
            CreateTestFacilities();
            return;
        }

        // Get all facilities that need workers
        var facilities = _gameManager.buildingSystem.GetAllFacilities();
        
        if (facilities == null || facilities.Count == 0)
        {
            Debug.LogWarning("[WorkerAssignmentUI] No facilities found, creating test facilities");
            CreateTestFacilities();
            return;
        }

        DebugLog($"Found {facilities.Count} facilities to populate");

        foreach (var facility in facilities)
        {
            if (facility != null)
            {
                CreateWorkerEntry(facility);
            }
        }

        _totalAvailableWorkers = _workerManager != null ? _workerManager.AvailableWorkers : _gameManager.AvailableWorkers;
        UpdateAllWorkerEntries();
    }

    private void CreateWorkerEntry(Building facility)
    {
        if (workerEntryPrefab == null || workerListContainer == null)
        {
            Debug.LogError("[WorkerAssignmentUI] Worker entry prefab or container not assigned");
            return;
        }

        GameObject entryObject = Instantiate(workerEntryPrefab, workerListContainer);
        WorkerUIController entryController = entryObject.GetComponent<WorkerUIController>();

        if (entryController == null)
        {
            entryController = entryObject.AddComponent<WorkerUIController>();
        }

        // Get facility info
        string facilityId = facility.GetInstanceID().ToString();
        string facilityName = facility.name;
        int volunteerRequirement = GetFacilityWorkerRequirement(facility);
        
        // Get current worker count from WorkerManager
        int currentWorkerCount = _workerManager != null ? _workerManager.GetCurrentWorkerCount(facilityId) : 0;
        
        // Initialize the entry
        entryController.Initialize(
            facilityId,
            defaultFacilitySprite,
            facilityName,
            volunteerRequirement,
            currentWorkerCount,
            _totalAvailableWorkers
        );

        // Subscribe to worker count changes
        entryController.OnWorkerCountChanged += OnWorkerCountChanged;

        _workerEntries.Add(entryController);
        
        DebugLog($"Created worker entry for {facilityName} (ID: {facilityId}) requiring {volunteerRequirement} workers");
    }

    private void CreateTestFacilities()
    {
        // Create test facilities for testing purposes
        string[] testFacilities = { "Shelter 1", "Shelter 2", "Kitchen 1", "Community Center" };
        int[] workerRequirements = { 2, 3, 2, 1 };

        DebugLog("Creating test facilities for worker assignment");

        for (int i = 0; i < testFacilities.Length; i++)
        {
            GameObject entryObject = Instantiate(workerEntryPrefab, workerListContainer);
            WorkerUIController entryController = entryObject.GetComponent<WorkerUIController>();

            if (entryController == null)
            {
                entryController = entryObject.AddComponent<WorkerUIController>();
            }

            string facilityId = "test_facility_" + i;
            int currentWorkerCount = _workerManager != null ? _workerManager.GetCurrentWorkerCount(facilityId) : 0;

            entryController.Initialize(
                facilityId,
                defaultFacilitySprite,
                testFacilities[i],
                workerRequirements[i],
                currentWorkerCount,
                _gameManager != null ? _gameManager.AvailableWorkers : 20
            );

            entryController.OnWorkerCountChanged += OnWorkerCountChanged;
            _workerEntries.Add(entryController);
        }

        _totalAvailableWorkers = _workerManager != null ? _workerManager.AvailableWorkers : 
                                (_gameManager != null ? _gameManager.AvailableWorkers : 20);
        UpdateAllWorkerEntries();
    }

    private int GetFacilityWorkerRequirement(Building facility)
    {
        // Try to get requirement from WorkerManager first
        if (_workerManager != null)
        {
            try
            {
                var method = _workerManager.GetType().GetMethod("GetMinimumWorkersRequired", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    return (int)method.Invoke(_workerManager, new object[] { facility });
                }
            }
            catch (System.Exception e)
            {
                DebugLog($"Could not get worker requirement from WorkerManager: {e.Message}");
            }
        }

        // Fallback: Default worker requirement based on facility type
        if (facility.name.ToLower().Contains("shelter"))
            return 2;
        else if (facility.name.ToLower().Contains("kitchen"))
            return 2;
        else if (facility.name.ToLower().Contains("community"))
            return 1;
        else
            return 1;
    }

    private void OnWorkerCountChanged(string facilityId, int newCount)
    {
        DebugLog($"Worker count changed for {facilityId}: {newCount}");

        // Use WorkerManager directly (no need to go through MasterGameManager)
        if (_workerManager != null)
        {
            bool success = _workerManager.SetWorkersForFacility(facilityId, newCount);
        
            if (!success)
            {
                Debug.LogWarning($"Failed to assign {newCount} workers to {facilityId}");
                // Revert UI to previous state if assignment failed
                var entry = _workerEntries.Find(e => e.GetFacilityId() == facilityId);
                if (entry != null)
                {
                    int currentCount = _workerManager.GetCurrentWorkerCount(facilityId);
                    entry.SetWorkerCount(currentCount);
                    DebugLog($"Reverted worker count for {facilityId} to {currentCount}");
                }
            }
            else
            {
                DebugLog($"Successfully assigned {newCount} workers to {facilityId}");
            }
        }

        // Update available workers count
        UpdateTotalAvailableWorkersDisplay();
        UpdateAllWorkerEntries();
    }
    private void UpdateTotalAvailableWorkersDisplay()
    {
        if (_workerManager != null)
        {
            _totalAvailableWorkers = _workerManager.AvailableWorkers;
        }
        else if (_gameManager != null)
        {
            _totalAvailableWorkers = _gameManager.AvailableWorkers;
        }

        if (totalAvailableWorkersText != null)
        {
            totalAvailableWorkersText.text = "Total Available Workers: " + _totalAvailableWorkers.ToString();
        }
    }

    private void UpdateAllWorkerEntries()
    {
        foreach (var entry in _workerEntries)
        {
            if (entry != null)
            {
                entry.UpdateAvailableWorkers(_totalAvailableWorkers);
            }
        }
    }

    /// <summary>
    /// Only clear entries when actually needed (facilities destroyed)
    /// </summary>
    private void ClearWorkerEntries()
    {
        foreach (var entry in _workerEntries)
        {
            if (entry != null)
            {
                entry.OnWorkerCountChanged -= OnWorkerCountChanged;
                Destroy(entry.gameObject);
            }
        }
        _workerEntries.Clear();
    
        DebugLog("Cleared all worker entries");
    }

    /// <summary>
    /// Validate that all facilities meet minimum worker requirements
    /// </summary>
    private bool ValidateWorkerAssignments()
    {
        bool allValid = true;
        
        foreach (var entry in _workerEntries)
        {
            if (entry != null)
            {
                DebugLog($"Facility {entry.GetFacilityId()} has {entry.GetWorkerCount()} workers assigned");
            }
        }
        
        return allValid;
    }

    /// <summary>
    /// Get total workers assigned across all facilities
    /// </summary>
    public int GetTotalAssignedWorkers()
    {
        if (_workerManager != null)
        {
            return _workerManager.AssignedWorkers;
        }
        
        // Fallback: count from UI entries
        int total = 0;
        foreach (var entry in _workerEntries)
        {
            if (entry != null)
            {
                total += entry.GetWorkerCount();
            }
        }
        return total;
    }

    /// <summary>
    /// Get worker assignment statistics
    /// </summary>
    public WorkerStatistics GetWorkerStatistics()
    {
        if (_workerManager != null)
        {
            return _workerManager.GetWorkerStatistics();
        }
        
        return new WorkerStatistics
        {
            totalWorkers = _gameManager != null ? _gameManager.totalWorkersPerRound : 20,
            assignedWorkers = GetTotalAssignedWorkers(),
            availableWorkers = _totalAvailableWorkers,
            facilitiesStaffed = 0,
            totalFacilities = _workerEntries.Count,
            assignmentRate = 0f
        };
    }

    /// <summary>
    /// Force refresh the UI (useful for debugging)
    /// </summary>
    [ContextMenu("Refresh Worker UI")]
    public void RefreshUI()
    {
        if (_gameManager != null && _gameManager.CurrentPhase == GlobalEnums.GamePhase.WorkerAssignment)
        {
            PopulateFacilityList();
            UpdateTotalAvailableWorkersDisplay();
        }
    }

    /// <summary>
    /// Add UI element to hide during worker assignment (useful for runtime additions)
    /// </summary>
    public void AddUIElementToHide(GameObject uiElement)
    {
        if (uiElement != null && !uiElementsToHide.Contains(uiElement))
        {
            uiElementsToHide.Add(uiElement);
            
            // Add CanvasGroup for smooth transitions
            CanvasGroup canvasGroup = uiElement.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = uiElement.AddComponent<CanvasGroup>();
            }
            
            if (!_hiddenUICanvasGroups.Contains(canvasGroup))
            {
                _hiddenUICanvasGroups.Add(canvasGroup);
            }
            
            DebugLog($"Added {uiElement.name} to UI elements to hide");
        }
    }
    
    private void DebugLog(string message)
    {
        if (showDebugMessages)
        {
            Debug.Log($"[WorkerAssignmentUI] {message}");
        }
    }
}
