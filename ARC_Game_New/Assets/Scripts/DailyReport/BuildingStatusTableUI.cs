using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BuildingStatusTableUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject rowPrefab;      
    public Transform tableContent;    

    [Header("Debug")]
    public bool showDebugInfo = true;

    private Dictionary<MonoBehaviour, BuildingStatusRow> rows = new Dictionary<MonoBehaviour, BuildingStatusRow>();
    private Dictionary<MonoBehaviour, System.Action> storageHandlers = new Dictionary<MonoBehaviour, System.Action>();

    public static BuildingStatusTableUI Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Player-constructed buildings already in the scene
        foreach (Building b in FindObjectsOfType<Building>())
        {
            OnBuildingCreated(b);
        }

        // Static map fixtures — assumed not to spawn/despawn at runtime
        foreach (PrebuiltBuilding pb in FindObjectsOfType<PrebuiltBuilding>())
        {
            if (pb.GetPrebuiltType() == PrebuiltBuildingType.Motel) continue; 
            AddRow(pb);
        }

        if (GlobalClock.Instance != null)
            GlobalClock.OnRoundEnd += RefreshAllRows;
    }

    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
            GlobalClock.OnRoundEnd -= RefreshAllRows;

        foreach (var kvp in storageHandlers.ToList())
        {
            if (kvp.Key == null) continue;
            var storage = kvp.Key.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                storage.OnStorageUpdated -= kvp.Value;
        }
    }


    public void OnBuildingCreated(Building building)
    {
        if (building == null) return;
        BuildingType type = building.GetBuildingType();
        if (type == BuildingType.Kitchen || type == BuildingType.CaseworkSite || type == BuildingType.Motel) return;
        AddRow(building);
    }

    public void OnBuildingDestroyed(Building building)
    {
        if (building == null) return;
        RemoveRow(building);
    }


    void AddRow(MonoBehaviour facility)
    {
        if (facility == null || rowPrefab == null || tableContent == null) return;
        if (rows.ContainsKey(facility)) return; // already tracked

        GameObject rowObj = Instantiate(rowPrefab, tableContent);
        rowObj.name = $"Row_{facility.name}";

        BuildingStatusRow row = rowObj.GetComponent<BuildingStatusRow>();
        if (row == null)
        {
            Debug.LogError("BuildingStatusTableUI: rowPrefab is missing a BuildingStatusRow component");
            Destroy(rowObj);
            return;
        }

        row.Initialize(facility);
        rows[facility] = row;

        var storage = facility.GetComponent<BuildingResourceStorage>();
        if (storage != null)
        {
            System.Action handler = () => RefreshRow(facility);
            storageHandlers[facility] = handler;
            storage.OnStorageUpdated += handler;
        }

        if (showDebugInfo)
            Debug.Log($"[BuildingStatusTableUI] Added row for {facility.name}");
    }

    void RemoveRow(MonoBehaviour facility)
    {
        if (facility == null) return;

        var storage = facility.GetComponent<BuildingResourceStorage>();
        if (storage != null && storageHandlers.TryGetValue(facility, out System.Action handler))
        {
            storage.OnStorageUpdated -= handler;
            storageHandlers.Remove(facility);
        }

        if (rows.TryGetValue(facility, out BuildingStatusRow row))
        {
            if (row != null)
                Destroy(row.gameObject);
            rows.Remove(facility);

            if (showDebugInfo)
                Debug.Log($"[BuildingStatusTableUI] Removed row for {facility.name}");
        }
    }

    void RefreshRow(MonoBehaviour facility)
    {
        if (facility != null && rows.TryGetValue(facility, out BuildingStatusRow row) && row != null)
            row.Refresh();
    }

    void RefreshAllRows()
    {
        var deadKeys = rows.Keys.Where(f => f == null).ToList();
        foreach (var dead in deadKeys)
            rows.Remove(dead);

        foreach (var kvp in rows)
        {
            if (kvp.Key != null && kvp.Value != null)
                kvp.Value.Refresh();
        }
    }

    /// <summary>
    /// Reveal the table (e.g. once the Daily Report has finished displaying its content)
    /// and refresh every row so it shows current data the moment it becomes visible.
    /// </summary>
    public void ShowTable()
    {
        gameObject.SetActive(true);
        RefreshAllRows();

        if (showDebugInfo)
            Debug.Log("[BuildingStatusTableUI] Table shown");
    }

    /// <summary>
    /// Hide the table (e.g. when the Daily Report is dismissed). Row tracking and
    /// per-round refreshes keep running while hidden, since those are driven by
    /// events rather than Unity's Update loop.
    /// </summary>
    public void HideTable()
    {
        gameObject.SetActive(false);

        if (showDebugInfo)
            Debug.Log("[BuildingStatusTableUI] Table hidden");
        GameLogPanel.Instance?.LogUIInteraction("Building status table hidden");
    }

    /// <summary>
    /// Records every row's current content, in the same top-to-bottom order it
    /// appears in the UI (i.e. tableContent's child order, not dictionary order).
    /// Refreshes first so the logged data is current, independent of whether
    /// ShowTable() has revealed the table yet — this can run before that, since
    /// data logging must not wait on any visual reveal or animation.
    /// </summary>
    public void LogTableContents(int day)
    {
        if (tableContent == null) return;

        RefreshAllRows();

        int seq = 0;
        for (int i = 0; i < tableContent.childCount; i++)
        {
            BuildingStatusRow row = tableContent.GetChild(i).GetComponent<BuildingStatusRow>();
            if (row == null) continue;

            string location = row.locationText != null ? row.locationText.text : "";
            if (string.IsNullOrEmpty(location)) continue;

            string foodNeed = row.foodPackNeedText != null ? row.foodPackNeedText.text : "";
            string foodConsumed = row.foodPackConsumedText != null ? row.foodPackConsumedText.text : "";
            string occupancy = row.lodgingOccupancyText != null ? row.lodgingOccupancyText.text : "";
            string capacity = row.capacityText != null ? row.capacityText.text : "";

            seq++;
            GameLogPanel.Instance?.LogMetricsChange(
                $"DAILY_REPORT_BUILDING_STATUS | day={day} | #{seq} | {location}" +
                $" | Food Pack Need: {foodNeed}" +
                $" | Food Packs Consumed: {foodConsumed}" +
                $" | Occupancy: {occupancy}" +
                $" | Capacity: {capacity}");
        }

        if (showDebugInfo)
            Debug.Log($"[BuildingStatusTableUI] Logged {seq} row(s) for day {day}");
    }
}