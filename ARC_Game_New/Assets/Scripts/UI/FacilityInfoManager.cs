using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;

public class FacilityInfoManager : MonoBehaviour
{
    [Header("Panel References")]
    public GameObject facilityInfoPanel;
    public Canvas uiCanvas;
    
    [Header("Hover Effects")]
    public float hoverScale = 1.2f;
    public float hoverAnimationSpeed = 5f;
    
    [Header("Click Detection")]
    public LayerMask facilityLayerMask = -1;
    public float clickRadius = 2f;
    
    [Header("Panel Positioning")]
    public Vector2 panelOffset = new Vector2(100, 0);
    public bool keepPanelInBounds = true;
    
    private Camera mainCamera;
    private MonoBehaviour currentHoveredFacility;
    private MonoBehaviour currentSelectedFacility;
    private Vector3 originalScale;
    private FacilityInfoPanel infoPanel;
    private bool isPanelOpen = false;
    
    // Singleton
    public static FacilityInfoManager Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        mainCamera = Camera.main;
        
        if (facilityInfoPanel != null)
        {
            infoPanel = facilityInfoPanel.GetComponent<FacilityInfoPanel>();
            facilityInfoPanel.SetActive(false);
        }
        
        if (uiCanvas == null)
            uiCanvas = FindObjectOfType<Canvas>();
    }
    
    void Update()
    {
        HandleMouseInput();
        HandleHoverEffects();
        HandleClickOutside();
    }
    
    void HandleMouseInput()
    {
        // Check if mouse is over UI - if yes, clear hover and return
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (currentHoveredFacility != null)
            {
                OnFacilityHoverExit();
                currentHoveredFacility = null;
            }
            return; // Don't process any facility interactions
        }

        Vector3 mousePos = Input.mousePosition;
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCamera.nearClipPlane));
        
        // Check for facility hover
        MonoBehaviour facility = GetFacilityAtPosition(worldPos);
        
        if (facility != currentHoveredFacility)
        {
            OnFacilityHoverExit();
            currentHoveredFacility = facility;
            OnFacilityHoverEnter();
        }
        
        // Handle click
        if (Input.GetMouseButtonDown(0))
        {
            // Check if clicking on UI
            if (EventSystem.current.IsPointerOverGameObject())
                return;
                
            if (facility != null)
            {
                OnFacilityClicked(facility);
            }
            else if (isPanelOpen)
            {
                CloseFacilityPanel();
            }
        }
    }
    
    MonoBehaviour GetFacilityAtPosition(Vector3 worldPos)
    {
        // Use Physics2D to check colliders instead of radius detection
        Collider2D hitCollider = Physics2D.OverlapPoint(worldPos, facilityLayerMask);
        
        if (hitCollider != null)
        {
            // Check if it's a Building
            Building building = hitCollider.GetComponent<Building>();
            if (building != null) return building;
            
            // Check if it's a PrebuiltBuilding
            PrebuiltBuilding prebuilt = hitCollider.GetComponent<PrebuiltBuilding>();
            if (prebuilt != null) return prebuilt;
        }
        
        // Fallback to radius detection if no collider found
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building building in buildings)
        {
            float distance = Vector2.Distance(worldPos, building.transform.position);
            if (distance <= clickRadius)
                return building;
        }
        
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            float distance = Vector2.Distance(worldPos, prebuilt.transform.position);
            if (distance <= clickRadius)
                return prebuilt;
        }
        
        return null;
    }
    
    void OnFacilityHoverEnter()
    {
        if (currentHoveredFacility == null) return;
        
        // Store original scale if not already stored
        SpriteRenderer renderer = currentHoveredFacility.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            originalScale = currentHoveredFacility.transform.localScale;
        }
    }
    
    void OnFacilityHoverExit()
    {
        if (currentHoveredFacility == null) return;
        
        // Reset scale
        SpriteRenderer renderer = currentHoveredFacility.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            currentHoveredFacility.transform.localScale = originalScale;
        }
    }
    
    void HandleHoverEffects()
    {
        if (currentHoveredFacility == null) return;
        
        SpriteRenderer renderer = currentHoveredFacility.GetComponent<SpriteRenderer>();
        if (renderer == null) return;
        
        Vector3 targetScale = originalScale * hoverScale;
        currentHoveredFacility.transform.localScale = Vector3.Lerp(
            currentHoveredFacility.transform.localScale,
            targetScale,
            Time.unscaledDeltaTime * hoverAnimationSpeed
        );
    }
    
    void OnFacilityClicked(MonoBehaviour facility)
    {
        currentSelectedFacility = facility;
        ShowFacilityPanel(facility);
    }
    
    void ShowFacilityPanel(MonoBehaviour facility)
    {
        if (facilityInfoPanel == null || infoPanel == null) return;
        
        // Position panel
        Vector3 facilityScreenPos = mainCamera.WorldToScreenPoint(facility.transform.position);
        Vector2 panelPosition = new Vector2(facilityScreenPos.x, facilityScreenPos.y) + panelOffset;
        
        // Keep panel in screen bounds
        if (keepPanelInBounds)
        {
            panelPosition = ClampPanelToBounds(panelPosition);
        }
        
        // Convert to canvas local position
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            uiCanvas.transform as RectTransform,
            panelPosition,
            uiCanvas.worldCamera,
            out localPos
        );
        
        RectTransform panelRect = facilityInfoPanel.GetComponent<RectTransform>();
        panelRect.localPosition = localPos;
        
        // Update panel content
        infoPanel.UpdateFacilityInfo(facility);
        
        // Show panel
        facilityInfoPanel.SetActive(true);
        isPanelOpen = true;
        
        Debug.Log($"Opened facility panel for: {facility.name}");
    }
    
    Vector2 ClampPanelToBounds(Vector2 panelPosition)
    {
        if (facilityInfoPanel == null) return panelPosition;
        
        RectTransform panelRect = facilityInfoPanel.GetComponent<RectTransform>();
        Vector2 panelSize = panelRect.sizeDelta;
        
        // Get screen bounds
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;
        
        // Clamp X
        if (panelPosition.x + panelSize.x > screenWidth)
            panelPosition.x = screenWidth - panelSize.x - 20;
        if (panelPosition.x < 20)
            panelPosition.x = 20;
            
        // Clamp Y
        if (panelPosition.y + panelSize.y > screenHeight)
            panelPosition.y = screenHeight - panelSize.y - 20;
        if (panelPosition.y < 20)
            panelPosition.y = 20;
            
        return panelPosition;
    }
    
    void HandleClickOutside()
    {
        if (!isPanelOpen) return;
        
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            
            // Check if click is inside panel
            if (facilityInfoPanel != null && facilityInfoPanel.activeInHierarchy)
            {
                RectTransform panelRect = facilityInfoPanel.GetComponent<RectTransform>();
                if (!RectTransformUtility.RectangleContainsScreenPoint(panelRect, mousePos, uiCanvas.worldCamera))
                {
                    // Check if clicking on same facility (to prevent immediate close)
                    Vector3 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCamera.nearClipPlane));
                    MonoBehaviour clickedFacility = GetFacilityAtPosition(worldPos);
                    
                    if (clickedFacility != currentSelectedFacility)
                    {
                        CloseFacilityPanel();
                    }
                }
            }
        }
    }
    
    public void CloseFacilityPanel()
    {
        if (facilityInfoPanel != null)
        {
            facilityInfoPanel.SetActive(false);
        }

        AudioManager.Instance.PlayCancelSFX();

        isPanelOpen = false;
        currentSelectedFacility = null;
        
        Debug.Log("Closed facility panel");
    }
    
    public bool IsPanelOpen()
    {
        return isPanelOpen;
    }
    
    public MonoBehaviour GetCurrentSelectedFacility()
    {
        return currentSelectedFacility;
    }
    
    // Called from Building/PrebuiltBuilding OnMouseEnter/OnMouseExit
    public void OnFacilityHover(MonoBehaviour facility, bool isEntering)
    {
        if (isEntering)
        {
            if (facility != currentHoveredFacility)
            {
                OnFacilityHoverExit();
                currentHoveredFacility = facility;
                OnFacilityHoverEnter();
            }
        }
        else
        {
            if (facility == currentHoveredFacility)
            {
                OnFacilityHoverExit();
                currentHoveredFacility = null;
            }
            }
    }

    // Called from Building/PrebuiltBuilding OnMouseDown
    public void OnFacilityClick(MonoBehaviour facility)
    {
        AudioManager.Instance.PlayClickSFX();
        OnFacilityClicked(facility);
    }

}