using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serialisable capture of the authoritative game state, so any position can be saved to a
/// JSON file and restored later -- on another machine, in another wing (RL / benchmark /
/// GUI), or as a branch point for tree search.
///
/// WHY THIS EXISTS ALONGSIDE cora_search.py
/// The Python side can already restore a position by REPLAY: reset to a seed and re-send
/// the recorded actions. That is exact (verified: reload equivalence, branch isolation,
/// JSON round-trip) but it is O(rounds) per restore and, more importantly, it can only
/// express positions REACHED THROUGH recorded gym RPCs. A GUI-played game goes through
/// WebSocketManager and produces no such journal, so it can never be handed to RL that way.
/// A true snapshot is wing-agnostic: it describes where the pieces ARE, not how they got
/// there, so any position round-trips regardless of who produced it.
///
/// RANDOM STATE IS PART OF THE STATE.
/// UnityEngine.Random.state must be captured and restored or two branches taken from the
/// same snapshot draw from a stream that has already been advanced by the other branch,
/// and the search is silently contaminated. Random.State is a struct of four uints; it is
/// stored here as those four ints so it survives JSON.
/// </summary>
[Serializable]
public class GameSnapshot
{
    public const int FORMAT_VERSION = 1;

    public int formatVersion = FORMAT_VERSION;
    public string createdUtc;
    public int seed = -1;                 // the seed the episode was started with, if any

    public RngState rng = new RngState();
    public ClockState clock = new ClockState();
    public EconomyState economy = new EconomyState();

    // Systems are appended here as their capture support lands. Keeping them as explicit
    // typed sections (rather than a reflection dump) means a missing field is a visible
    // gap in this file rather than a silent difference at restore time.
    public List<Building.Snapshot> buildings = new List<Building.Snapshot>();

    // Cumulative accumulators. These are the ones most likely to be forgotten and the
    // most damaging to miss: they are the numerator/denominator of every score component,
    // they are private to their owning classes, and a gym reset zeroes them.
    public WorkerSystem.Snapshot workforce;
    public TaskSystem.Snapshot tasks;
    public WeatherSystem.Snapshot weather;
    public RewardMetricsTracker.Snapshot rewardMetrics;
    public SatisfactionAndBudget.SpendSnapshot spend;

    [Serializable]
    public class RngState
    {
        // UnityEngine.Random.State has no public fields; JsonUtility round-trips it, so it
        // is carried as its JSON form rather than being picked apart.
        public string unityRandomStateJson;
        public bool captured;
    }

    [Serializable]
    public class ClockState
    {
        public int currentDay;
        public int currentTimeSegment;
        public int lastDay;
        public int roundsPerDay;
        public bool isWaitingForReport;
    }

    [Serializable]
    public class EconomyState
    {
        public float currentSatisfaction;
        public float currentEfficiency;
        public int currentBudget;
    }

}

/// <summary>
/// Captures and restores <see cref="GameSnapshot"/> against the live scene.
///
/// THREADING: every method here touches Unity objects and JsonUtility, so it must run on
/// the main thread. GymServerManager queues these through mainThreadActions -- calling them
/// from the TCP thread is a native-crash class in this project, not merely unsafe.
/// </summary>
public static class GameSnapshotManager
{
    public static GameSnapshot Capture()
    {
        var s = new GameSnapshot
        {
            createdUtc = DateTime.UtcNow.ToString("o"),
            seed = GymServerManager.ActiveSeed,
        };

        // RNG first: everything below may allocate, and allocation must not perturb the
        // stream between reading it and the caller acting on the snapshot.
        s.rng.unityRandomStateJson = JsonUtility.ToJson(UnityEngine.Random.state);
        s.rng.captured = true;

        var clock = GlobalClock.Instance;
        if (clock != null)
        {
            s.clock.currentDay = clock.currentDay;
            s.clock.currentTimeSegment = clock.currentTimeSegment;
            s.clock.lastDay = clock.lastDay;
            s.clock.roundsPerDay = clock.roundsPerDay;
            s.clock.isWaitingForReport = clock.isWaitingForReport;
        }

        var econ = SatisfactionAndBudget.Instance;
        if (econ != null)
        {
            s.economy.currentSatisfaction = econ.currentSatisfaction;
            s.economy.currentEfficiency = econ.currentEfficiency;
            s.economy.currentBudget = econ.currentBudget;
        }

        foreach (var b in UnityEngine.Object.FindObjectsOfType<Building>())
        {
            if (b != null) s.buildings.Add(b.CaptureState());
        }

        var ws = UnityEngine.Object.FindObjectOfType<WorkerSystem>();
        if (ws != null) s.workforce = ws.CaptureState();

        var ts = TaskSystem.Instance;
        if (ts != null) s.tasks = ts.CaptureState();
        var weather = WeatherSystem.Instance;
        if (weather != null) s.weather = weather.CaptureState();

        var rmt = RewardMetricsTracker.Instance;
        if (rmt != null) s.rewardMetrics = rmt.CaptureState();
        if (econ != null) s.spend = econ.CaptureSpend();

        return s;
    }

    /// <summary>
    /// Write a snapshot back onto the live scene. Assumes the scene has already been
    /// rebuilt (reset_game) so that singletons are fresh -- restoring onto a mid-game scene
    /// would leave stale objects the snapshot does not mention.
    /// </summary>
    public static void Restore(GameSnapshot s)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (s.formatVersion != GameSnapshot.FORMAT_VERSION)
            throw new InvalidOperationException(
                $"snapshot formatVersion {s.formatVersion} != {GameSnapshot.FORMAT_VERSION}");

        var clock = GlobalClock.Instance;
        if (clock != null)
        {
            clock.currentDay = s.clock.currentDay;
            clock.currentTimeSegment = s.clock.currentTimeSegment;
            clock.lastDay = s.clock.lastDay;
            clock.roundsPerDay = s.clock.roundsPerDay;
            clock.isWaitingForReport = s.clock.isWaitingForReport;
        }

        // BUILDINGS BEFORE ECONOMY, DELIBERATELY. The scene rebuild leaves only the
        // pre-existing fixtures, so player-built facilities have to be RE-CREATED via the
        // same path a construction action uses -- and that path charges the budget. The
        // economy restore below then overwrites those charges with the saved figures, so
        // recreation cannot double-bill. Reversing this order silently corrupts the budget.
        RestoreBuildings(s.buildings);

        // After buildings: workers carry assignedBuildingId, so the buildings they point
        // at must already exist or the roster restores into dangling references.
        var ws = UnityEngine.Object.FindObjectOfType<WorkerSystem>();
        if (ws != null && s.workforce != null) ws.RestoreState(s.workforce);

        // Tasks reference facilities by name, so buildings must already be back.
        var ts = TaskSystem.Instance;
        if (ts != null && s.tasks != null) ts.RestoreState(s.tasks);
        var weather = WeatherSystem.Instance;
        if (weather != null && s.weather != null) weather.RestoreState(s.weather);

        var econ = SatisfactionAndBudget.Instance;
        if (econ != null)
        {
            econ.currentSatisfaction = s.economy.currentSatisfaction;
            econ.currentEfficiency = s.economy.currentEfficiency;
            econ.currentBudget = s.economy.currentBudget;
        }

        // Accumulators are written back AFTER the reset path has already called
        // ResetForNewEpisode()/rebuilt the scene, which is exactly why they are restored
        // here rather than being left to the reset to preserve.
        var rmt = RewardMetricsTracker.Instance;
        if (rmt != null && s.rewardMetrics != null) rmt.RestoreState(s.rewardMetrics);
        if (econ != null && s.spend != null) econ.RestoreSpend(s.spend);

        // RNG LAST. The scene rebuild's Awake/Start chain consumes random numbers, so
        // restoring the stream before that work would immediately be overwritten by it.
        if (s.rng.captured && !string.IsNullOrEmpty(s.rng.unityRandomStateJson))
        {
            UnityEngine.Random.state =
                JsonUtility.FromJson<UnityEngine.Random.State>(s.rng.unityRandomStateJson);
        }
    }

    /// <summary>
    /// Re-create the buildings a snapshot describes, then write their internal counters
    /// back. Fixtures that survive a scene rebuild (Communities, Motel) are matched by
    /// site id and only have their state applied; anything else is constructed first.
    /// </summary>
    static void RestoreBuildings(List<Building.Snapshot> snaps)
    {
        if (snaps == null || snaps.Count == 0) return;

        var live = new Dictionary<int, Building>();
        foreach (var b in UnityEngine.Object.FindObjectsOfType<Building>())
        {
            if (b != null) live[b.GetOriginalSiteId()] = b;
        }

        var system = UnityEngine.Object.FindObjectOfType<BuildingSystem>();
        foreach (var snap in snaps)
        {
            if (live.TryGetValue(snap.originalSiteId, out Building existing))
            {
                existing.RestoreState(snap);
                continue;
            }
            if (system == null)
            {
                Debug.LogWarning($"[Snapshot] no BuildingSystem; cannot recreate site {snap.originalSiteId}");
                continue;
            }
            AbandonedSite site = null;
            foreach (var candidate in system.RegisteredSites)
            {
                if (candidate != null && candidate.GetId() == snap.originalSiteId) { site = candidate; break; }
            }
            if (site == null)
            {
                Debug.LogWarning($"[Snapshot] site {snap.originalSiteId} not found; building not restored");
                continue;
            }
            if (!Enum.TryParse(snap.buildingType, out BuildingType bt))
            {
                Debug.LogWarning($"[Snapshot] unknown building type '{snap.buildingType}'");
                continue;
            }
            if (!system.CreateBuildingImmediately(site, bt))
            {
                Debug.LogWarning($"[Snapshot] failed to recreate {bt} at site {snap.originalSiteId}");
                continue;
            }
            foreach (var b in UnityEngine.Object.FindObjectsOfType<Building>())
            {
                if (b != null && b.GetOriginalSiteId() == snap.originalSiteId) { b.RestoreState(snap); break; }
            }
        }
    }

    public static string ToJson(GameSnapshot s, bool pretty = true) => JsonUtility.ToJson(s, pretty);
    public static GameSnapshot FromJson(string json) => JsonUtility.FromJson<GameSnapshot>(json);
}
