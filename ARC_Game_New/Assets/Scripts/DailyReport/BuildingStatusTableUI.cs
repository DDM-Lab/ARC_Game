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
        ScanForUntrackedFacilities();

        if (GlobalClock.Instance != null)
            GlobalClock.OnRoundEnd += RefreshAllRows;
    }

    void ScanForUntrackedFacilities()
    {
        foreach (Building b in FindObjectsOfType<Building>())
        {
            if (rows.ContainsKey(b)) continue;
            try { OnBuildingCreated(b); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BuildingStatusTableUI] Failed to add row for building '{b.name}': {ex}");
            }
        }

        foreach (PrebuiltBuilding pb in FindObjectsOfType<PrebuiltBuilding>())
        {
            if (rows.ContainsKey(pb)) continue;
            try { AddRow(pb); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BuildingStatusTableUI] Failed to add row for prebuilt '{pb.name}': {ex}");
            }
        }
    }

    void RefreshAllRows()
    {
        ScanForUntrackedFacilities();

        var deadEntries = rows.Where(kvp => kvp.Key == null).ToList();
        foreach (var kvp in deadEntries)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value.gameObject);
            rows.Remove(kvp.Key);
        }

        foreach (var kvp in rows.ToList())
        {
            if (kvp.Key == null || kvp.Value == null) continue;

            try { kvp.Value.Refresh(); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BuildingStatusTableUI] Row refresh failed for '{kvp.Key.name}' — continuing with the rest of the table. {ex}");
            }

            if (kvp.Value.IsFacilityGone)
            {
                Destroy(kvp.Value.gameObject);
                rows.Remove(kvp.Key);
            }
        }
    }

    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
            GlobalClock.OnRoundEnd -= RefreshAllRows;
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

        if (showDebugInfo)
            Debug.Log($"[BuildingStatusTableUI] Added row for {facility.name}");
    }

    void RemoveRow(MonoBehaviour facility)
    {
        if (facility == null) return;

        if (rows.TryGetValue(facility, out BuildingStatusRow row))
        {
            if (row != null)
                Destroy(row.gameObject);
            rows.Remove(facility);

            if (showDebugInfo)
                Debug.Log($"[BuildingStatusTableUI] Removed row for {facility.name}");
        }
    }

    public void ShowTable()
    {
        gameObject.SetActive(true);
        RefreshAllRows();

        if (showDebugInfo)
            Debug.Log("[BuildingStatusTableUI] Table shown");
    }

    public void HideTable()
    {
        gameObject.SetActive(false);

        if (showDebugInfo)
            Debug.Log("[BuildingStatusTableUI] Table hidden");
        GameLogPanel.Instance?.LogUIInteraction("Building status table hidden");
    }

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