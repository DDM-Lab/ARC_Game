using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GlobalFacilityButton : MonoBehaviour
{
    [Header("Button")]
    public Button facilityButton;
    
    [Header("UI References")]
    public BuildingSelectionUI buildingSelectionUI;
    
    [Header("Prompt Settings")]
    public float promptDisplayDuration = 3f;
    
    void Start()
    {
        if (facilityButton != null)
            facilityButton.onClick.AddListener(OnFacilityButtonClicked);
    }
    
    void OnFacilityButtonClicked()
    {
        buildingSelectionUI.ToggleUI(Vector3.zero);
        
        Debug.Log($"Global facility button clicked - panel {(buildingSelectionUI.IsUIOpen() ? "opened" : "closed")}");
    }
    
}