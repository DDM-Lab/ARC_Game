using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which round each building's workers were last assigned.
/// Release is only allowed in the same round as assignment.
/// </summary>
public class WorkerAssignmentTracker : MonoBehaviour
{
    public static WorkerAssignmentTracker Instance { get; private set; }

    // buildingId → (day, segment) of last assignment
    private Dictionary<int, (int day, int segment)> assignmentRounds = new Dictionary<int, (int, int)>();

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    public void RecordAssignment(int buildingId)
    {
        if (GlobalClock.Instance == null) return;
        assignmentRounds[buildingId] = (GlobalClock.Instance.GetCurrentDay(), GlobalClock.Instance.GetCurrentTimeSegment());
    }

    /// <summary>
    /// Returns true if workers were assigned in a previous round — release is no longer allowed.
    /// </summary>
    public bool IsLockedForRelease(int buildingId)
    {
        if (!assignmentRounds.ContainsKey(buildingId) || GlobalClock.Instance == null) return false;
        var (day, segment) = assignmentRounds[buildingId];
        return day != GlobalClock.Instance.GetCurrentDay()
            || segment != GlobalClock.Instance.GetCurrentTimeSegment();
    }

    public bool HasBeenAssigned(int buildingId) => assignmentRounds.ContainsKey(buildingId);

    /// <summary>
    /// Call when a building is deconstructed. Site IDs (buildingId) are reused by whatever new
    /// building gets built on the same AbandonedSite afterward — without this, the new building
    /// would inherit the old one's assignment record and IsLockedForRelease would wrongly return
    /// true (locked) even though the new building has zero workers, capping both worker-count
    /// inputs at 0 and making it impossible to assign anyone.
    /// </summary>
    public void ClearBuilding(int buildingId) => assignmentRounds.Remove(buildingId);
}
