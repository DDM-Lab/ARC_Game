using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuildingUIOverlay : MonoBehaviour
{
    [Header("UI Settings")]
    public float uiElementSize = 100f; // Size of UI overlay elements
    public Vector2 uiOffset = Vector2.zero; // Offset from building position
    public float toastDurationTime = 5f; // Duration to show toast messages
    
    [Header("UI Prefab")]
    public GameObject uiOverlayPrefab; // Single prefab with InfoText and worker button children
    
    [Header("UI References")]
    public Transform uiContainer; // Container in Canvas that holds all UI overlays

    public GlobalWorkerManagementUI workerAssignmentUI;
    public Camera mainCamera; // Main camera for world to screen conversion
    
    [Header("Debug")]
    public bool showDebugVisuals = false; // Show colored squares for debugging
    public Color debugColor = new Color(1, 0, 0, 0.3f); // Red semi-transparent
    
    // Dictionary to track building-to-UI mapping
    private Dictionary<Building, GameObject> buildingUIMap = new Dictionary<Building, GameObject>();
    
    // Singleton
    public static BuildingUIOverlay Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (workerAssignmentUI == null)
        {
            workerAssignmentUI = FindObjectOfType<GlobalWorkerManagementUI>();
        }
            
        if (uiContainer == null)
        {
            Debug.LogError("BuildingUIOverlay: UI Container not assigned!");
        }
        
        // Subscribe to building system events if available
        BuildingSystem buildingSystem = FindObjectOfType<BuildingSystem>();
        if (buildingSystem != null)
        {
            // We'll hook into building creation through public methods
        }
    }
    
    // Called when a new building is created
    public void OnBuildingCreated(Building building)
    {
        if (building == null || uiContainer == null) return;
        
        // Create UI overlay for this building
        GameObject uiOverlay = CreateUIOverlay(building);
        
        if (uiOverlay != null)
        {
            buildingUIMap[building] = uiOverlay;
            
            // Position the UI overlay
            UpdateUIPosition(building, uiOverlay);
            
            // Set initial state for construction
            SetConstructionState(uiOverlay);
            
            // Subscribe to building status changes
            StartCoroutine(MonitorBuildingStatus(building));
        }
    }
    
    private GameObject CreateUIOverlay(Building building)
    {
        GameObject uiOverlay = null;
        
        if (uiOverlayPrefab != null)
        {
            // Use the prefab
            uiOverlay = Instantiate(uiOverlayPrefab, uiContainer);
            
            if (showDebugVisuals)
            {
                // Turn the color of the prefab's Image component to debug color
                Image img = uiOverlay.GetComponent<Image>();
                if (img != null)
                {
                    img.color = debugColor;
                }
            }
            
            // Find the TextBG > InfoText structure
            Transform textBG = uiOverlay.transform.Find("TextBG");
            if (textBG != null)
            {
                Transform infoText = textBG.Find("InfoText");
                if (infoText != null)
                {
                    infoText.gameObject.SetActive(true);
                }
                textBG.gameObject.SetActive(true);
            }
            
            // Find and hide the worker button initially
            Transform workerButton = uiOverlay.transform.Find("WorkerButton");
            if (workerButton == null)
            {
                // Try other common names
                workerButton = uiOverlay.transform.Find("Worker Button");
                if (workerButton == null)
                    workerButton = uiOverlay.transform.Find("AssignButton");
            }
            
            if (workerButton != null)
            {
                workerButton.gameObject.SetActive(false);
                
                // Set up button click handler
                Button btnComponent = workerButton.GetComponent<Button>();
                if (btnComponent != null)
                {
                    btnComponent.onClick.RemoveAllListeners(); // Clear any existing listeners
                    btnComponent.onClick.AddListener(() => OnAssignWorkerClicked(building));
                }
            }
            
            // Find and hide the deconstruct button initially
            Transform deconstructButton = uiOverlay.transform.Find("DeconstructButton");
            if (deconstructButton != null)
            {
                deconstructButton.gameObject.SetActive(false);
                
                // Set up button click handler
                Button deconstructBtnComponent = deconstructButton.GetComponent<Button>();
                if (deconstructBtnComponent != null)
                {
                    deconstructBtnComponent.onClick.RemoveAllListeners();
                    deconstructBtnComponent.onClick.AddListener(() => OnDeconstructClicked(building));
                }
            }
            
            // Find and hide ToastText initially
            Transform toastText = uiOverlay.transform.Find("ToastText");
            if (toastText != null)
            {
                toastText.gameObject.SetActive(false);
            }

            // Find the label text and set it to the facility's name
            Transform labelText = uiOverlay.transform.Find("BuildingLabelText");
            if (labelText != null)
            {
                TextMeshProUGUI labelTextComponent = labelText.GetComponent<TextMeshProUGUI>();
                if (labelTextComponent != null)
                {
                    // get the facility name and id
                    labelTextComponent.text = $"{building.GetBuildingType()} ({building.GetOriginalSiteId()})";
                }
            }
        }
        else
        {
            Debug.LogError("BuildingUIOverlay: uiOverlayPrefab is not assigned!");
            return null;
        }
        
        // Ensure proper RectTransform setup
        RectTransform rt = uiOverlay.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
        }
        
        // Set the name for easy identification
        uiOverlay.name = $"UI_{building.GetBuildingType()}_{building.GetOriginalSiteId()}";
        
        // Add click handler to the main overlay if it has a button component
        Button overlayButton = uiOverlay.GetComponent<Button>();
        if (overlayButton != null)
        {
            overlayButton.onClick.AddListener(() => OnBuildingUIClicked(building));
        }
        
        return uiOverlay;
    }
    
    private void SetConstructionState(GameObject uiOverlay)
    {
        // Set InfoText to "Under Construction"
        Transform textBG = uiOverlay.transform.Find("TextBG");
        if (textBG != null)
        {
            Transform infoText = textBG.Find("InfoText");
            if (infoText != null)
            {
                TextMeshProUGUI textComponent = infoText.GetComponent<TextMeshProUGUI>();
                if (textComponent != null)
                {
                    textComponent.text = "Under Conversion";
                }
            }
        }
    }
    
    private void UpdateUIPosition(Building building, GameObject uiOverlay)
    {
        if (building == null || uiOverlay == null || mainCamera == null) return;
        
        // Get the building's world position
        Vector3 worldPos = building.transform.position;
        
        // Convert world position to viewport position (0-1 range)
        Vector3 viewportPos = mainCamera.WorldToViewportPoint(worldPos);
        
        // Check if building is visible on screen
        if (viewportPos.z > 0 && viewportPos.x >= 0 && viewportPos.x <= 1 && viewportPos.y >= 0 && viewportPos.y <= 1)
        {
            // Make sure overlay is visible
            if (!uiOverlay.activeSelf)
                uiOverlay.SetActive(true);
            
            // Get canvas RectTransform
            Canvas canvas = uiContainer.GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            
            // Convert viewport position to canvas position
            Vector2 canvasSize = canvasRect.sizeDelta;
            Vector2 screenPos = new Vector2(
                viewportPos.x * canvasSize.x - canvasSize.x * 0.5f,
                viewportPos.y * canvasSize.y - canvasSize.y * 0.5f
            );
            
            // If canvas is Screen Space - Overlay
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                screenPos = mainCamera.WorldToScreenPoint(worldPos);
            }
            
            // Apply offset
            screenPos += uiOffset;
            
            // Set position
            RectTransform rectTransform = uiOverlay.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = screenPos;
            }
        }
        else
        {
            // Building is off-screen, hide UI
            if (uiOverlay.activeSelf)
                uiOverlay.SetActive(false);
        }
    }
    
    private IEnumerator MonitorBuildingStatus(Building building)
    {
        if (building == null) yield break;
        
        BuildingStatus previousStatus = building.GetCurrentStatus();
        
        while (building != null && buildingUIMap.ContainsKey(building))
        {
            BuildingStatus currentStatus = building.GetCurrentStatus();
            
            // Check if status changed from UnderConstruction to NeedWorker
            if (previousStatus == BuildingStatus.UnderConstruction && 
                currentStatus == BuildingStatus.NeedWorker)
            {
                OnConstructionCompleted(building);
            }
            // Check if workers were assigned
            else if (previousStatus == BuildingStatus.NeedWorker && 
                     currentStatus == BuildingStatus.InUse)
            {
                OnWorkersAssigned(building);
            }
            // Check if building entered deconstructing state
            else if (currentStatus == BuildingStatus.Deconstructing)
            {
                OnDeconstructionStarted(building);
            }
            
            previousStatus = currentStatus;
            
            // Update UI position in case building moved
            if (buildingUIMap.ContainsKey(building))
            {
                UpdateUIPosition(building, buildingUIMap[building]);
            }
            
            // Wait a bit before checking again
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
    
    private void OnConstructionCompleted(Building building)
    {
        if (!buildingUIMap.ContainsKey(building)) return;
        
        GameObject uiOverlay = buildingUIMap[building];
        
        // Update InfoText to "Need Worker"
        Transform textBG = uiOverlay.transform.Find("TextBG");
        if (textBG != null)
        {
            Transform infoText = textBG.Find("InfoText");
            if (infoText != null)
            {
                TextMeshProUGUI textComponent = infoText.GetComponent<TextMeshProUGUI>();
                if (textComponent != null)
                {
                    textComponent.text = "Need Worker";
                }
            }
        }
        
        // Show the worker button
        Transform workerButton = uiOverlay.transform.Find("WorkerButton");
        if (workerButton == null)
        {
            workerButton = uiOverlay.transform.Find("Worker Button");
            if (workerButton == null)
                workerButton = uiOverlay.transform.Find("AssignButton");
        }
        
        if (workerButton != null)
        {
            workerButton.gameObject.SetActive(true);
        }
        
        // Show ToastText with fade animation
        Transform toastText = uiOverlay.transform.Find("ToastText");
        if (toastText != null)
        {
            TextMeshProUGUI toastTextComponent = toastText.GetComponent<TextMeshProUGUI>();
            if (toastTextComponent != null)
            {
                toastTextComponent.text = "Construction Complete!";
            }
            StartCoroutine(ShowToastText(toastText.gameObject));
        }
        
        Debug.Log($"Building {building.name} construction completed - UI updated");
    }
    
    private IEnumerator ShowToastText(GameObject toastTextObj)
    {
        if (toastTextObj == null) yield break;
        
        toastTextObj.SetActive(true);
        CanvasGroup canvasGroup = toastTextObj.GetComponent<CanvasGroup>();
        
        // If no CanvasGroup, add one for fade effect
        if (canvasGroup == null)
        {
            canvasGroup = toastTextObj.AddComponent<CanvasGroup>();
        }
        
        // Fade in
        float fadeTime = 0.5f;
        float elapsedTime = 0f;
        while (elapsedTime < fadeTime)
        {
            elapsedTime += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsedTime / fadeTime);
            yield return null;
        }
        canvasGroup.alpha = 1f;
        
        // Wait for a few seconds
        yield return new WaitForSecondsRealtime(toastDurationTime);
        
        // Fade out
        elapsedTime = 0f;
        while (elapsedTime < fadeTime)
        {
            elapsedTime += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeTime);
            yield return null;
        }
        
        toastTextObj.SetActive(false);
    }
    
    private void OnWorkersAssigned(Building building)
    {
        if (!buildingUIMap.ContainsKey(building)) return;
        
        GameObject uiOverlay = buildingUIMap[building];
        
        // Hide the TextBG (which contains InfoText)
        Transform textBG = uiOverlay.transform.Find("TextBG");
        if (textBG != null)
        {
            textBG.gameObject.SetActive(false);
        }

        // Change the text of worker button to "Manage"
        Transform workerButton = uiOverlay.transform.Find("WorkerButton");
        if (workerButton == null)
        {
            workerButton = uiOverlay.transform.Find("Worker Button");
            if (workerButton == null)
                workerButton = uiOverlay.transform.Find("AssignButton");
        }
        if (workerButton != null)
        {
            TextMeshProUGUI btnText = workerButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
            {
                btnText.text = "Manage";
            }
        }

        // Show the deconstruct button
        Transform deconstructButton = uiOverlay.transform.Find("DeconstructButton");
        if (deconstructButton != null)
        {
            deconstructButton.gameObject.SetActive(true);
        }

        Debug.Log($"Building {building.name} workers assigned - UI updated");
    }
    
    private void OnDeconstructionStarted(Building building)
    {
        if (!buildingUIMap.ContainsKey(building)) return;
        
        GameObject uiOverlay = buildingUIMap[building];
        
        // Show TextBG with "Deconstructing" message
        Transform textBG = uiOverlay.transform.Find("TextBG");
        if (textBG != null)
        {
            textBG.gameObject.SetActive(true);
            Transform infoText = textBG.Find("InfoText");
            if (infoText != null)
            {
                TextMeshProUGUI textComponent = infoText.GetComponent<TextMeshProUGUI>();
                if (textComponent != null)
                {
                    textComponent.text = "Deconstructing...";
                }
            }
        }
        
        // Hide worker button
        Transform workerButton = uiOverlay.transform.Find("WorkerButton");
        if (workerButton == null)
        {
            workerButton = uiOverlay.transform.Find("Worker Button");
            if (workerButton == null)
                workerButton = uiOverlay.transform.Find("AssignButton");
        }
        if (workerButton != null)
        {
            workerButton.gameObject.SetActive(false);
        }
        
        // Hide deconstruct button
        Transform deconstructButton = uiOverlay.transform.Find("DeconstructButton");
        if (deconstructButton != null)
        {
            deconstructButton.gameObject.SetActive(false);
        }
        
        Debug.Log($"Building {building.name} deconstruction started - UI updated");
    }
    
    private void OnAssignWorkerClicked(Building building)
    {
        // Open worker assignment UI
        WorkerSystem workerSystem = FindObjectOfType<WorkerSystem>();
        if (workerSystem != null)
        {
            Debug.Log($"Opening worker assignment for building {building.name}");

            if (workerAssignmentUI != null)
            {
                workerAssignmentUI.OnManageButtonClicked(building);
            }
        }
    }
    
    private void OnDeconstructClicked(Building building)
    {
        Debug.Log($"Deconstruct button clicked for building {building.name}");
        
        if (building != null && building.IsOperational())
        {
            // Show confirmation popup instead of immediately deconstructing
            if (ConfirmationPopup.Instance != null)
            {
                string message = $"Are you sure you want to deconstruct {building.GetBuildingType()} at site {building.GetOriginalSiteId()}?\n\nThis action cannot be undone.";
                
                ConfirmationPopup.Instance.ShowPopup(
                    message: message,
                    onConfirm: () => {
                        // This executes when user clicks Confirm
                        building.StartDeconstruction();
                        Debug.Log($"User confirmed deconstruction of {building.name}");
                    },
                    onCancel: () => {
                        // This executes when user clicks Cancel (optional)
                        Debug.Log($"User cancelled deconstruction of {building.name}");
                    },
                    title: "Deconstruct Building"
                );
            }
            else
            {
                Debug.LogError("ConfirmationPopup not found in scene!");
                // Fallback: deconstruct immediately if popup system not available
                building.StartDeconstruction();
            }
        }
    }
    
    private void OnBuildingUIClicked(Building building)
    {
        // Handle general building UI click
        Debug.Log($"Building UI clicked: {building.name}");
        
        // You can open building info panel or other interactions here
        FacilityInfoManager facilityManager = FindObjectOfType<FacilityInfoManager>();
        if (facilityManager != null)
        {
            facilityManager.OnFacilityClick(building);
        }
    }
    
    // Called when a building is destroyed
    public void OnBuildingDestroyed(Building building)
    {
        if (building == null) return;
        
        // Clean up UI element
        if (buildingUIMap.ContainsKey(building))
        {
            Destroy(buildingUIMap[building]);
            buildingUIMap.Remove(building);
        }
    }
    
    void Update()
    {
        // Update all UI positions each frame to handle camera movement
        foreach (var kvp in buildingUIMap)
        {
            if (kvp.Key != null && kvp.Value != null)
            {
                UpdateUIPosition(kvp.Key, kvp.Value);
            }
        }
    }
    
    // Helper method to get UI overlay for a specific building
    public GameObject GetUIOverlayForBuilding(Building building)
    {
        if (buildingUIMap.ContainsKey(building))
        {
            return buildingUIMap[building];
        }
        return null;
    }
}