using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

public class VehicleInfoPanel : MonoBehaviour
{
    [Header("Panel References")]
    public GameObject infoPanel;
    public Button closeButton;

    [Header("Info Display")]
    public TextMeshProUGUI vehicleNameText;
    public TextMeshProUGUI missionText;
    public TextMeshProUGUI destinationText;

    [Header("Route Visualization")]
    public DeliveryRouteVisualizer routeVisualizer;

    [Header("Click Detection")]
    public float clickRadius = 1f;

    [Header("Panel Positioning")]
    public Canvas uiCanvas;

    public static VehicleInfoPanel Instance { get; private set; }

    private Vehicle         currentVehicle;
    private RectTransform   panelRect;
    private Vector3         lastClickPosition;
    private Vector3         lastMouseScreenPos;
    private List<Vehicle>   vehiclesAtClickPosition = new List<Vehicle>();
    private int             currentVehicleIndex = 0;
    private int             vehicleClickFrame   = -1; // frame in which a vehicle was last clicked

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);

        if (infoPanel != null)
        {
            infoPanel.SetActive(false);
            panelRect = infoPanel.GetComponent<RectTransform>();
        }

        if (uiCanvas == null)
            uiCanvas = FindObjectOfType<Canvas>();

        if (routeVisualizer == null)
            routeVisualizer = FindObjectOfType<DeliveryRouteVisualizer>();
    }

    // ── Vehicle click entry (called from Vehicle.OnMouseDown) ─────────

    public void OnVehicleClicked(Vector3 clickPosition)
    {
        if (Time.timeScale != 0f) return;

        vehicleClickFrame  = Time.frameCount;
        lastMouseScreenPos = Input.mousePosition;

        bool samePosition = Vector3.Distance(clickPosition, lastClickPosition) < 0.1f;

        if (!samePosition)
        {
            lastClickPosition = clickPosition;
            vehiclesAtClickPosition = GetVehiclesAtPosition(clickPosition);
            currentVehicleIndex = 0;
            if (vehiclesAtClickPosition.Count == 0) { ClosePanel(); return; }
        }
        else
        {
            if (vehiclesAtClickPosition.Count == 0) { ClosePanel(); return; }
            currentVehicleIndex = (currentVehicleIndex + 1) % vehiclesAtClickPosition.Count;
        }

        ShowVehicleInfo(vehiclesAtClickPosition[currentVehicleIndex]);
    }

    List<Vehicle> GetVehiclesAtPosition(Vector3 position)
    {
        var vehicles = new List<Vehicle>();
        foreach (Vehicle v in FindObjectsOfType<Vehicle>())
            if (Vector3.Distance(v.transform.position, position) <= clickRadius)
                vehicles.Add(v);
        return vehicles;
    }

    // ── Show / update ─────────────────────────────────────────────────

    public void ShowVehicleInfo(Vehicle vehicle)
    {
        if (vehicle == null) { ClosePanel(); return; }

        currentVehicle = vehicle;

        if (infoPanel != null) infoPanel.SetActive(true);

        UpdatePanelContent();
        PositionAtClick();
        ShowRouteVisualization();

        GameLogPanel.Instance?.LogUIInteraction(
            $"Opened vehicle panel: Vehicle #{currentVehicle.vehicleId}" +
            $" | destination={GetBuildingName(currentVehicle.destinationBuilding)}");
    }

    void UpdatePanelContent()
    {
        if (currentVehicle == null) return;

        if (vehicleNameText != null)
            vehicleNameText.text = $"Id: Vehicle #{currentVehicle.vehicleId}";

        if (missionText != null)
        {
            int    qty  = currentVehicle.GetTotalCargo();
            string type = currentVehicle.GetPrimaryCargoType() == ResourceType.Population ? "clients" : "meals";
            string src  = GetBuildingName(currentVehicle.sourceBuilding);
            missionText.text = qty > 0
                ? $"Mission: {qty}x {type} from {src}"
                : $"Mission: Picking up from {src}";
        }

        if (destinationText != null)
            destinationText.text = $"Destination: {GetBuildingName(currentVehicle.destinationBuilding)}";
    }

    // ── Panel positioning ─────────────────────────────────────────────

    void PositionAtClick()
    {
        if (panelRect == null || uiCanvas == null) return;

        RectTransform canvasRect = uiCanvas.transform as RectTransform;

        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, lastMouseScreenPos, uiCanvas.worldCamera, out Vector2 localPos);

        // Clamp so the panel stays within canvas bounds
        Vector2 panelSize  = panelRect.rect.size;
        Vector2 pivot      = panelRect.pivot;
        Vector2 canvasMin  = canvasRect.rect.min;
        Vector2 canvasMax  = canvasRect.rect.max;
        const float pad    = 10f;

        float leftEdge   = localPos.x - pivot.x         * panelSize.x;
        float rightEdge  = localPos.x + (1f - pivot.x)  * panelSize.x;
        float bottomEdge = localPos.y - pivot.y         * panelSize.y;
        float topEdge    = localPos.y + (1f - pivot.y)  * panelSize.y;

        if (rightEdge  > canvasMax.x - pad) localPos.x -= rightEdge  - (canvasMax.x - pad);
        if (leftEdge   < canvasMin.x + pad) localPos.x -= leftEdge   - (canvasMin.x + pad);
        if (topEdge    > canvasMax.y - pad) localPos.y -= topEdge    - (canvasMax.y - pad);
        if (bottomEdge < canvasMin.y + pad) localPos.y -= bottomEdge - (canvasMin.y + pad);

        panelRect.localPosition = localPos;
    }

    // ── Route visualization ───────────────────────────────────────────

    void ShowRouteVisualization()
    {
        if (routeVisualizer == null || currentVehicle == null) return;

        List<Vector3> path = currentVehicle.currentPath;
        if (path == null || path.Count == 0) { routeVisualizer.HideRoute(); return; }

        Vector3 sourcePos = currentVehicle.sourceBuilding != null
            ? currentVehicle.sourceBuilding.transform.position : path[0];
        Vector3 destPos = currentVehicle.destinationBuilding != null
            ? currentVehicle.destinationBuilding.transform.position : path[path.Count - 1];

        routeVisualizer.ShowRoute(path, sourcePos, destPos);
    }

    // ── Close ─────────────────────────────────────────────────────────

    public void ClosePanel()
    {
        if (infoPanel != null) infoPanel.SetActive(false);
        routeVisualizer?.HideRoute();
        currentVehicle = null;
        vehiclesAtClickPosition.Clear();
        currentVehicleIndex = 0;
        lastClickPosition = Vector3.positiveInfinity;
        GameLogPanel.Instance?.LogUIInteraction("Closed vehicle panel");
    }

    // ── Update ────────────────────────────────────────────────────────

    void Update()
    {
        if (infoPanel == null || !infoPanel.activeSelf) return;

        // Auto-close if vehicle disappears
        if (currentVehicle == null) { ClosePanel(); return; }

        // Refresh content every frame (status changes while watching)
        UpdatePanelContent();

        // Close when player clicks anywhere that isn't the panel or a vehicle
        if (Input.GetMouseButtonDown(0))
        {
            bool clickedVehicle = vehicleClickFrame == Time.frameCount;
            bool clickedUI      = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!clickedVehicle && !clickedUI)
                ClosePanel();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    string GetBuildingName(MonoBehaviour building)
    {
        if (building == null) return "Unknown";
        Building b = building.GetComponent<Building>();
        if (b != null) return b.GetDisplayName();
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetBuildingName();
        return building.name;
    }

    public bool IsPanelOpen() => infoPanel != null && infoPanel.activeSelf;
}
