using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WorkerUIController : MonoBehaviour
{
    [Header("UI References")]
    public Image facilityImage;
    public TextMeshProUGUI facilityNameText;
    public TextMeshProUGUI volunteerRequirementText;
    public TextMeshProUGUI workerCountText;
    public Button addWorkerButton;
    public Button removeWorkerButton;

    private int currentWorkerCount = 0;
    private int maxWorkersRequired = 0;
    private int availableWorkers = 0; // Track available workers internally

    // Delegate to notify worker count changes
    public delegate void WorkerCountChanged(string facilityId, int newCount);
    public event WorkerCountChanged OnWorkerCountChanged;

    private string facilityId;

    /// <summary>
    /// Initialize the UI with facility data
    /// </summary>
    public void Initialize(string id, Sprite facilitySprite, string facilityName, int volunteerRequirement, int initialWorkerCount, int totalAvailableWorkers)
    {
        facilityId = id;
        if (facilityImage != null)
            facilityImage.sprite = facilitySprite;
        if (facilityNameText != null)
            facilityNameText.text = facilityName;
        if (volunteerRequirementText != null)
            volunteerRequirementText.text = "Volunteers required: " + volunteerRequirement.ToString();

        maxWorkersRequired = volunteerRequirement;
        currentWorkerCount = Mathf.Clamp(initialWorkerCount, 0, maxWorkersRequired);
        availableWorkers = totalAvailableWorkers;

        UpdateWorkerCountText();

        // Setup button listeners
        if (addWorkerButton != null)
        {
            addWorkerButton.onClick.RemoveAllListeners();
            addWorkerButton.onClick.AddListener(OnAddWorkerClicked);
        }
        if (removeWorkerButton != null)
        {
            removeWorkerButton.onClick.RemoveAllListeners();
            removeWorkerButton.onClick.AddListener(OnRemoveWorkerClicked);
        }

        UpdateButtonInteractable();
    }

    private void OnAddWorkerClicked()
    {
        if (currentWorkerCount < maxWorkersRequired)
        {
            currentWorkerCount++;
            UpdateWorkerCountText();
            if (OnWorkerCountChanged != null)
                OnWorkerCountChanged(facilityId, currentWorkerCount);
        }
    }

    private void OnRemoveWorkerClicked()
    {
        if (currentWorkerCount > 0)
        {
            currentWorkerCount--;
            UpdateWorkerCountText();
            if (OnWorkerCountChanged != null)
                OnWorkerCountChanged(facilityId, currentWorkerCount);
        }
    }

    /// <summary>
    /// Update available workers count (called from WorkerAssignmentUI)
    /// </summary>
    public void UpdateAvailableWorkers(int totalAvailableWorkers)
    {
        availableWorkers = totalAvailableWorkers;
        UpdateButtonInteractable();
    }

    private void UpdateWorkerCountText()
    {
        if (workerCountText != null)
        {
            workerCountText.text = currentWorkerCount.ToString();
        }
    }

    private void UpdateButtonInteractable()
    {
        if (addWorkerButton != null)
        {
            addWorkerButton.interactable = currentWorkerCount < maxWorkersRequired && availableWorkers > 0;
        }
        if (removeWorkerButton != null)
        {
            removeWorkerButton.interactable = currentWorkerCount > 0;
        }
    }

    /// <summary>
    /// Set the current worker count externally (e.g., from game logic)
    /// </summary>
    public void SetWorkerCount(int count)
    {
        currentWorkerCount = Mathf.Clamp(count, 0, maxWorkersRequired);
        UpdateWorkerCountText();
        UpdateButtonInteractable();
    }

    /// <summary>
    /// Get current worker count
    /// </summary>
    public int GetWorkerCount()
    {
        return currentWorkerCount;
    }

    /// <summary>
    /// Get facility ID
    /// </summary>
    public string GetFacilityId()
    {
        return facilityId;
    }
}
