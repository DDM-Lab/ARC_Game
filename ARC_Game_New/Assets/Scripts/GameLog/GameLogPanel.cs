using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.Linq;
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using ARC.Replay;


public enum LogMessageType
{
    Normal,
    Debug,
    Error
}

public enum LogCategory
{
    All,
    Buildings,
    Resources,
    Workers,
    Tasks,
    Environment,
    Vehicles,
    Metrics,
    Player,
    UI
}

/// <summary>
/// Tier 3 of the replay/analysis data design: one structured record per state-changing
/// occurrence, player-driven or automatic. source = "player" | "system" lets a consumer
/// filter without a different parser for each. seq is a strict, monotonic counter — the
/// authoritative ordering for replay, independent of timestamp collisions.
///
/// targetJson/parametersJson hold JSON *objects* (e.g. {"worker_id":12,"facility_id":"shelter_03"})
/// as strings, not flat identifiers — built exclusively through JsonObj below, never by hand
/// string-interpolation, which breaks the moment a value contains a quote. JsonUtility (used
/// everywhere else in this codebase) cannot serialize a Dictionary or an arbitrary nested
/// object without one fixed C# type per action/event type, which is what this works around:
/// the receiver (save_game_logs.py) parses these two strings back into real nested JSON
/// objects before writing the final replay sidecar, so the file a replay tool or analyst
/// actually reads has true nested objects, exactly as if LedgerEntry had been typed that way.
/// </summary>
[System.Serializable]
public class LedgerEntry
{
    public int seq;
    public string source;       // "player" | "system"
    public string type;         // e.g. "Build", "AssignWorker", "TaskChoice", "FloodChanged"
    public string targetJson;
    public string parametersJson;
    public int day;
    public int round;
    public float timestamp;

    // Replay state (see Assets/Scripts/GameLog/Replay and tests/replay). captured = this entry
    // carries a state delta: the change from the previously captured state to the state right
    // after this entry. Uncaptured entries (clicks, anything before the initial anchor) have
    // no delta, and the next captured delta still covers every change since the last good state.
    public bool captured;
    public string captureError;   // set when an entry should have been captured but was not
    public string stateDeltaJson; // canonical JSON: {baseSeq, baseHash, resultHash, ops}
    public string stateJson;      // keyframes only: the full canonical state
    public string stateHash;      // keyframes only: SHA-256 of stateJson
}

/// <summary>
/// Minimal, safe JSON-object builder for LedgerEntry's target/parameters. Exists because
/// hand-interpolating a JSON string (what the first instrumentation pass did) silently breaks
/// the moment a value contains a quote, backslash or newline — a task's choiceText or an
/// agent message routinely does. Every Add() escapes its value; nothing downstream should ever
/// string-interpolate one of these fields directly.
/// </summary>
public class JsonObj
{
    readonly StringBuilder sb = new StringBuilder("{");
    bool first = true;
    void Comma() { if (!first) sb.Append(','); first = false; }
    JsonObj Key(string key) { Comma(); sb.Append('"').Append(Escape(key)).Append("\":"); return this; }
    public JsonObj Add(string key, string value) { Key(key); sb.Append(value == null ? "null" : "\"" + Escape(value) + "\""); return this; }
    public JsonObj Add(string key, int value) { Key(key); sb.Append(value); return this; }
    public JsonObj Add(string key, long value) { Key(key); sb.Append(value); return this; }
    public JsonObj Add(string key, float value) { Key(key); sb.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture)); return this; }
    public JsonObj Add(string key, bool value) { Key(key); sb.Append(value ? "true" : "false"); return this; }
    /// <summary>Embeds an already-built JSON fragment (object or array) verbatim — e.g. a task's
    /// list of choices, built with JsonArray below. Caller is responsible for it being valid JSON.</summary>
    public JsonObj AddRaw(string key, string rawJson) { Key(key); sb.Append(string.IsNullOrEmpty(rawJson) ? "null" : rawJson); return this; }
    public override string ToString() => sb.ToString() + "}";
    static string Escape(string s) => s == null ? "" :
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}

/// <summary>Companion to JsonObj for a JSON array of objects — e.g. a task's agentChoices, each
/// one a JsonObj, joined into "[{...},{...}]" for use with JsonObj.AddRaw.</summary>
public static class JsonArray
{
    public static string Of(System.Collections.Generic.IEnumerable<JsonObj> items)
        => "[" + string.Join(",", System.Linq.Enumerable.Select(items, i => i.ToString())) + "]";
}

/// <summary>
/// Tier 1 of the replay/analysis data design, periodic: a full GameSnapshot (already captures
/// every subsystem + RNG state, see GameSnapshot.cs) taken once per round, plus a hash of its
/// JSON for cheap validation — comparing hashes at matching rounds between an original session
/// and a replay attempt localizes a divergence to a specific round instead of only the end
/// state. Full snapshots are NOT taken per-action: since replay reconstructs intermediate
/// states by re-simulating the Tier 3 ledger (not by interpolating between stored snapshots),
/// per-action snapshots would be redundant data, not additional replay fidelity.
/// </summary>
[System.Serializable]
public class RoundCheckpoint
{
    public int day;
    public int round;
    // Why this checkpoint exists: "RoundEnd" (GlobalClock.OnRoundEnd) or "EndOfDay" (explicit,
    // fired once all of a day's management/report actions are done — the management/report
    // phase is not itself a simulated round, so OnRoundEnd never fires for it; relying on it
    // exclusively missed every worker assignment and training decision made during that phase,
    // found via the Day 1 cross-check). More triggers can be added the same way.
    public string trigger;
    // The keyframe itself lives in the ledger (LedgerEntry type "Keyframe", same sequence as the
    // actions). This row is an index into it, so the full state is not stored twice.
    public int ledgerSeq;
    public string stateHash;
}

[System.Serializable]
public class LogMessage
{
    public string content;
    public string messageType;
    public string category;
    public int day;
    public int round;
    public float timestamp;
    public string realTime;

    // Scoreboard state AT THE MOMENT this entry was logged — captured automatically for every
    // entry, not just budget/satisfaction/efficiency-specific ones, so any single log line can be
    // read on its own (for analysis) without first replaying the session up to that point.
    public int budget;
    public float satisfaction;
    public float efficiency;
    public int activeTaskCount;

    // Compact, human-readable "what's going on right now" summary — facility status, pending
    // self-walks/food deliveries, and the active task list. Only populated when it actually
    // differs from the last row that carried one (see lastStateSummary below), so a researcher
    // scanning the CSV sees a blank here most of the time and a populated cell exactly where
    // something notable changed. This is a separate, compact column, NOT the full nested
    // GameSnapshot — that stays in the JSON sidecar, which remains authoritative for replay.
    public string stateSummary;

    // Links this researcher-facing row to the structured ledger (see LedgerEntry / AddLedgerEntry).
    // -1 when this row has no corresponding ledger entry (e.g. a UI navigation step that changed
    // no game state). When set, LedgerEntry.seq == ledgerSeq is the structured record — including
    // its state delta — for the same event.
    public int ledgerSeq = -1;

    // Shared key across the rows of one task decision (opened -> choice selected -> confirmed ->
    // resolved), so a researcher can join them without parsing free text. Currently GameTask.stableTaskId.
    // Null when this row is not part of a task decision.
    public string taskDecisionId;

    // Shared across every LogMessage (there is only ever one GameLogPanel), so this lives as a
    // static cache rather than threading GameLogPanel's own instance through the constructor.
    static string lastStateSummary = "";

    public LogMessage(string content, LogMessageType type, LogCategory category, int ledgerSeq = -1, string taskDecisionId = null)
    {
        this.content = content;
        this.messageType = type.ToString();
        this.category = category.ToString();
        this.ledgerSeq = ledgerSeq;
        this.taskDecisionId = taskDecisionId;
        // Time.time freezes whenever GlobalClock pauses the simulation (timeScale=0) — which is
        // most of real play time by design (all of Day 1, and the "awaiting decision" stretch of
        // every other round between its ~10s Simulating burst). Time.unscaledTime tracks real
        // elapsed time regardless of pause state, matching what realTime (DateTime.Now) already does.
        this.timestamp = Time.unscaledTime;
        this.realTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (GlobalClock.Instance != null)
        {
            this.day = GlobalClock.Instance.GetCurrentDay();
            this.round = GlobalClock.Instance.GetCurrentTimeSegment() + 1;
        }
        else
        {
            this.day = 1;
            this.round = 1;
        }

        if (SatisfactionAndBudget.Instance != null)
        {
            this.budget = SatisfactionAndBudget.Instance.GetCurrentBudget();
            this.satisfaction = SatisfactionAndBudget.Instance.GetCurrentSatisfaction();
            this.efficiency = SatisfactionAndBudget.Instance.GetCurrentEfficiency();
        }

        // Only cheap, O(1) singleton lookups belong here — this constructor runs on every
        // single log call, so a scene-wide FindObjectsOfType (e.g. summing population across
        // every facility) does not belong in Tier 2. That level of detail belongs in the Tier 1
        // per-round GameSnapshot instead, which already captures it in full.
        if (TaskSystem.Instance != null)
            this.activeTaskCount = TaskSystem.Instance.activeTasks.Count;

        // stateSummary is the one exception to the "cheap only" rule above — it does iterate
        // every facility, by explicit request, since per-facility food/clients/capacity/status
        // is exactly what a researcher needs and a single aggregate number doesn't show it.
        // Bounded by facility count (~15-20), not a concern at that scale. Computed every call
        // (there's no way to know it's unchanged without computing it), but only WRITTEN when
        // different from last time, so the CSV column itself stays mostly blank.
        string summary = BuildStateSummaryString();
        if (summary != lastStateSummary)
        {
            this.stateSummary = summary;
            lastStateSummary = summary;
        }
    }

    static string BuildStateSummaryString()
    {
        var sb = new StringBuilder();

        // Per-facility status: name, food (if any), clients/capacity, status. Buildings and
        // prebuilt fixtures (Motel/Communities) use different APIs for "status" — a Building has
        // an explicit BuildingStatus; a PrebuiltBuilding doesn't construct/decay, so its
        // population figure IS its status.
        sb.Append("facilities=[");
        bool firstFacility = true;
        foreach (var b in UnityEngine.Object.FindObjectsOfType<Building>())
        {
            if (b == null) continue;
            var storage = b.GetComponent<BuildingResourceStorage>();
            int food = storage != null ? storage.GetResourceAmount(ResourceType.FoodPacks) : 0;
            int clients = storage != null ? storage.GetResourceAmount(ResourceType.Population) : 0;
            int capacity = storage != null ? storage.GetResourceCapacity(ResourceType.Population) : 0;
            if (!firstFacility) sb.Append(';');
            sb.Append(b.GetDisplayName()).Append(":food=").Append(food)
              .Append(",clients=").Append(clients).Append('/').Append(capacity)
              .Append(",status=").Append(b.GetCurrentStatus());
            firstFacility = false;
        }
        foreach (var pb in UnityEngine.Object.FindObjectsOfType<PrebuiltBuilding>())
        {
            if (pb == null) continue;
            var storage = pb.GetResourceStorage();
            int food = storage != null ? storage.GetResourceAmount(ResourceType.FoodPacks) : 0;
            int clients = storage != null ? storage.GetResourceAmount(ResourceType.Population) : 0;
            int capacity = storage != null ? storage.GetResourceCapacity(ResourceType.Population) : 0;
            if (!firstFacility) sb.Append(';');
            sb.Append(pb.GetBuildingName()).Append(":food=").Append(food)
              .Append(",clients=").Append(clients).Append('/').Append(capacity);
            firstFacility = false;
        }
        sb.Append(']');

        // Pending actions: self-walking clients and in-flight food deliveries, each its own
        // count — these are state a researcher would otherwise have to infer from several
        // scattered prose lines (relocation queued/arrived, delivery created/completed).
        int selfWalking = ClientRelocationHandler.Instance != null
            ? ClientRelocationHandler.Instance.GetPendingRelocations().Count : 0;
        int foodDeliveries = DeliverySystem.Instance != null
            ? DeliverySystem.Instance.GetActiveTasks().Count(t => t.cargoType == ResourceType.FoodPacks) : 0;
        sb.Append(" | selfWalking=").Append(selfWalking).Append(" | foodDeliveries=").Append(foodDeliveries);

        // Active task list: titles, not just the count already in activeTaskCount — a researcher
        // can see WHICH tasks are open right now without cross-referencing TaskGenerated events.
        sb.Append(" | tasks=[");
        if (TaskSystem.Instance != null)
            sb.Append(string.Join(";", TaskSystem.Instance.activeTasks.Select(t => t.taskTitle)));
        sb.Append(']');

        // Workforce, flood, weather — cheap O(1) reads via their own singletons.
        if (WorkerSystem.Instance != null)
        {
            var stats = WorkerSystem.Instance.GetWorkerStatistics();
            sb.Append(" | workers=").Append(stats.trainedFree + stats.untrainedFree).Append('i')
              .Append('/').Append(stats.trainedWorking + stats.untrainedWorking).Append('w')
              .Append('/').Append(stats.untrainedTraining).Append('t');
        }
        if (FloodSystem.Instance != null)
            sb.Append(" | flood=").Append(FloodSystem.Instance.GetFloodTileCount());
        if (WeatherSystem.Instance != null)
            sb.Append(" | weather=").Append(WeatherSystem.Instance.GetCurrentWeather());

        return sb.ToString();
    }
}

[System.Serializable]
public class LogExportData
{
    public string sessionId;
    public string playerName;
    public string gameVersion;
    public string exportTime;
    public int totalMessages;

    // "final" = the end-of-game upload; "checkpoint" = a cumulative snapshot taken at the end of an
    // earlier day (checkpointDay says which). Tells save_game_logs.py where to file it — see that script.
    public string uploadKind = "final";
    public int checkpointDay;

    // ── Episode reproduction ──
    // The random state the episode started from (see EpisodeReproLog). With the build id
    // and the parameters below, this is everything needed to reproduce the scenario.
    public string rngState;
    public string buildGuid;
    // The human-readable seed and where it came from (EpisodeSeed) — every episode has one now
    // ("auto" when nothing was explicitly supplied). rngState above is the raw post-seed
    // Random.state; this is what a replay tool would actually pass to Random.InitState.
    public int seed = -1;
    public string seedSource = "none";
    // Identifies which map layout this session ran on. mapConfigServerUrl is fetched live at
    // startup (see GameConfigLoader) — if its response ever differs from what this session
    // actually got, replay needs to know, not silently assume the default scene layout.
    public bool mapFromServer;
    public string mapConfigHash;
    // The RESOLVED map config content (grid size, tile layers, every placed object), not just
    // its hash — MapConfig is already [Serializable] (InstructorConfig/MapConfigData.cs), so
    // this is a direct JsonUtility.ToJson of the exact object GameConfigLoader resolved, not a
    // re-derivation. Empty when mapFromServer is false (the default scene layout was used,
    // which is static and shared across every session, identified by buildGuid instead).
    public string mapConfigJson;

    // ── Authoritative runtime configuration ──
    // initial* above and mapConfigJson.parameters are what two DIFFERENT, independent sources
    // (GameConfigLoader's sheet fetch; the server map config's own embedded ScenarioParameters)
    // each CLAIM the configuration was — and this build's known split (SatisfactionAndBudget
    // reads the sheet directly; GameDataManager's own sheet connection is unwired and silently
    // falls back to its Inspector defaults; map/population size comes from the scene, not any
    // config) means those claims can disagree with each other AND with what the game actually
    // ran on. Confirmed disagreeing on a real Day 1 session: budget logged as 8000 (sheet) and
    // 7500 (map config) while the game actually ran on 10000; community count logged as 4 and 7
    // while only 3 communities ever existed. authoritativeConfigJson is not a third guess — it's
    // read directly from the live objects every other system actually consults at the moment
    // this is captured (SatisfactionAndBudget for budget/satisfaction, WeatherSystem for
    // starting weather, MapSystem for community count — the three fields caught disagreeing —
    // GameDataManager for the rest, since its resolved value, whatever its own source, IS what
    // every other system that reads GameDataManager.Instance.InitialXxx operates on). Replay
    // must prefer this over initial*/mapConfigJson.parameters when they disagree.
    public string authoritativeConfigJson;

    // ── Replay data (Tiers 1 & 2 & 3) ──
    // Tier 1, t=0: the true initial state, before any player action and before the first
    // round-end checkpoint (which only exists after round 1 has already happened).
    public string initialSnapshotJson;
    public string initialSnapshotHash;
    // SHA-256 of the canonical form of initialSnapshotJson (CanonicalState). This is the anchor the
    // first captured delta is based on, so the verifier checks it against a re-canonicalisation.
    public string initialStateHash;
    public int canonicalSchemaVersion;
    // Tier 1, periodic: one full GameSnapshot + hash per round (see RoundCheckpoint) — the
    // authoritative state used to seed a replay and to validate one against the original session.
    public List<RoundCheckpoint> roundCheckpoints = new List<RoundCheckpoint>();
    // Tier 3: every player action and automatic system event, in strict sequence order —
    // the input stream a replay re-simulates against the Tier 1 initial state.
    public List<LedgerEntry> ledger = new List<LedgerEntry>();

    // ── Environment Config ──
    // The full parameter set, not just budget/satisfaction: a replay under a different sheet
    // silently produces a different game, and the difference is invisible after the fact.
    public int initialBudget;
    public int initialSatisfaction;
    public int initialCommunityCount;
    public int initialResidentsPerCommunity;
    public int initialNumDays;
    public int initialRoundsPerDay;
    public int initialTrainedVolunteers;
    public int initialUntrainedVolunteers;
    public int initialBudgetDailyAdditions;
    public string initialWeather;
    public int initialKitchenCapacity;
    public int initialShelterCapacity;
    public int initialCaseworkCapacity;
    public int initialNeededWorkersPerLoc;
    public float initialFoodDemandFrequency;
    public int initialERVCount;
    public int initialExternalRelationFrequency;
    public int initialEmergencyTaskFrequency;
    public int initialShelterFloodThreshold;
    public int initialShelterFloodRadius;
    public string initialShelterFloodComparison;
    public float[] floodExpansionRates;      // sunny, smallRain, mediumRain, heavyRain, storm
    public float[] floodSpreadMultipliers;   // same order

    public List<LogMessage> messages;

    public LogExportData(List<LogMessage> messages)
    {
        this.sessionId = PlayerSession.SessionId;
        this.playerName = PlayerSession.PlayerName;
        this.gameVersion = Application.version;
        this.exportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        this.totalMessages = messages.Count;
        this.messages = messages;
    }
}

public class GameLogPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private TextMeshProUGUI logText;

    [Header("Filter UI")]
    [SerializeField] private TMP_Dropdown messageTypeDropdown;
    [SerializeField] private TMP_Dropdown categoryDropdown;
    [SerializeField] private TMP_Dropdown timePeriodDropdown;

    [Header("Colors")]
    [SerializeField] private Color normalTextColor = Color.white;
    [SerializeField] private Color debugTextColor = Color.cyan;
    [SerializeField] private Color errorTextColor = Color.red;
    [SerializeField] private bool enableDebugMessages = true;

    [Header("Settings")]
    [SerializeField] private bool autoScrollToBottom = true;
    [SerializeField] private int maxDisplayedMessages = 100;

    [Header("Export")]
    [SerializeField] private Button exportCurrentButton;
    [SerializeField] private Button exportAllButton;

    private List<LogMessage> allMessages = new List<LogMessage>();
    private Queue<string> displayQueue = new Queue<string>();
    private bool isDisplayingMessage = false;

    // Tier 3 (player action + system event ledger) and Tier 1 (per-round full snapshot +
    // hash). See LedgerEntry/RoundCheckpoint for why these are separate from allMessages.
    private List<LedgerEntry> ledger = new List<LedgerEntry>();
    private int nextLedgerSeq = 0;
    private List<RoundCheckpoint> roundCheckpoints = new List<RoundCheckpoint>();

    // Replay chain. replayBase* is the last state that was captured successfully. Every captured
    // entry stores the window delta from it. replayBaseSeq is -1 for the initial anchor.
    // replayBaseState is null until the anchor exists, so earlier entries are uncaptured.
    private JObject replayBaseState;
    private string replayBaseHash;
    private int replayBaseSeq = -1;
    private string initialStateHash;

    // private LogMessageType currentTypeFilter = LogMessageType.Normal; // Reserved for future filtering
    private LogCategory currentCategoryFilter = LogCategory.All;
    private int currentTimePeriodFilter = 0;

    public static GameLogPanel Instance { get; private set; }
    public static bool IsDisplayingText { get; private set; } = false;

    // Standalone on/off switch for the log pipeline (collection + sending), read
    // directly from config.json's "dataCollectionEnabled" field. Deliberately
    // independent of WebSocketManager/config loading, since that's toggled on/off
    // for the LLM connection and shouldn't control whether we collect game logs.
    // Defaults to true (collect) until the config finishes loading, and stays true
    // if config.json is missing the field or fails to load.
    public static bool DataCollectionEnabled { get; private set; } = true;

    [System.Serializable]
    private class DataCollectionConfig
    {
        public bool dataCollectionEnabled = true;
    }

    // Set on OnApplicationQuit (fires before the object-teardown cascade begins, both on a
    // real quit and when stopping Play mode in the Editor). Guards AddLogMessage so nothing
    // tries StartCoroutine on a component that's mid-destruction — Instance?.LogXxx(...) call
    // sites elsewhere don't reliably short-circuit on a destroyed-but-not-yet-null Unity Object
    // via the ?. operator, so the guard has to live here rather than at each call site.
    private static bool isQuitting = false;

    private void Awake()
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

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        GlobalClock.OnRoundEnd -= CaptureRoundCheckpoint;
    }

    private void Start()
    {
        InitializeUI();
        SetupDropdowns();
        RefreshDisplay();
        StartCoroutine(LoadDataCollectionSetting());
        LogPlayerAction("Game started");
        LogClientTimeZone();
        GlobalClock.OnRoundEnd += CaptureRoundCheckpoint;
        StartCoroutine(CaptureInitialSnapshotWhenReady());
    }

    private string initialSnapshotJson;
    private string initialSnapshotHash;

    /// <summary>
    /// The true t=0 state — before any player action and before the first round-end checkpoint
    /// (which only fires after round 1 has already happened).
    ///
    /// A fixed one-frame wait is not enough: the map config server fetch is a network coroutine
    /// that can take far longer than a frame, and when it resolves AFTER that one frame, the
    /// game re-initializes things built from it (e.g. the vehicle fleet went from 3 vehicles
    /// named "Vehicle1" to 4 named "Vehicle 1" between the one-frame snapshot and the very next
    /// round-end checkpoint) — so the "initial" snapshot was capturing a transient state the
    /// game was about to discard, not the one everything else in the session builds on. Found
    /// via the Day 1 cross-check.
    ///
    /// Waits on the actual readiness signals instead: the parameter sheet fetch
    /// (GameConfigLoader.IsConfigLoaded), the map config fetch (IsMapConfigLoaded), and
    /// GameDataManager applying whichever of those resolved to the scene (IsDataReady). Capped
    /// at 10s, matching the timeout SatisfactionAndBudget already uses for the same sheet fetch
    /// — if a network fetch hangs, the alternative is waiting forever, not a better snapshot.
    /// A short additional settle window follows: readiness going true is when re-initialization
    /// (e.g. respawning vehicles) STARTS, not necessarily finished within that same frame.
    /// </summary>
    IEnumerator CaptureInitialSnapshotWhenReady()
    {
        float waited = 0f;
        const float timeout = 10f;
        const float settleWindow = 0.5f;
        while (waited < timeout)
        {
            bool configReady = GameConfigLoader.Instance == null || GameConfigLoader.Instance.IsConfigLoaded();
            bool mapReady = GameConfigLoader.Instance == null || GameConfigLoader.Instance.IsMapConfigLoaded();
            bool dataReady = GameDataManager.Instance == null || GameDataManager.Instance.IsDataReady;
            if (configReady && mapReady && dataReady) break;
            yield return new WaitForSecondsRealtime(0.1f);
            waited += 0.1f;
        }
        if (waited >= timeout)
            Debug.LogWarning("[GameLogPanel] Initial snapshot: config/map readiness timed out after 10s — capturing anyway.");

        // Let whatever reacts to the readiness flags (e.g. map-driven re-initialization) finish
        // what it starts this frame, rather than snapshotting mid-rebuild.
        yield return new WaitForSecondsRealtime(settleWindow);

        try
        {
            initialSnapshotJson = GameSnapshotManager.ToJson(GameSnapshotManager.Capture(), pretty: false);
            initialSnapshotHash = ComputeHash(initialSnapshotJson);

            // The anchor for the replay chain. If it cannot be canonicalised, no record gets a
            // delta, which the verifier reports, instead of a chain built on a wrong base.
            CanonicalResult anchor = CanonicalState.FromSnapshotJson(initialSnapshotJson);
            initialStateHash = anchor.Hash;
            replayBaseState = anchor.Tree;
            replayBaseHash = anchor.Hash;
            replayBaseSeq = -1;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameLogPanel] Initial snapshot capture failed: {e.Message}");
        }
    }

    /// <summary>
    /// Tier 1, periodic: one full GameSnapshot + hash per round. Subscribed at the same point
    /// DailyReportData.AccumulateRoundMetrics is, so this fires once per round regardless of
    /// which day it falls on — matches the "round granularity, not day granularity" call made
    /// after the replay-architecture review.
    /// </summary>
    void CaptureRoundCheckpoint() => CaptureCheckpoint("RoundEnd");

    /// <summary>
    /// Public so any system can request an explicit checkpoint outside the normal round-end
    /// cadence — e.g. DailyReportManager, once the player confirms leaving the report and every
    /// management action for that day is done. See the RoundCheckpoint.trigger field comment.
    /// </summary>
    public void CaptureCheckpoint(string trigger)
    {
        if (!DataCollectionEnabled) return;

        // A keyframe is a ledger entry like any other, so it shares the sequence and carries the
        // window delta as well as the full state. Replay can start at any keyframe, and the
        // verifier can check the chain against it.
        LedgerEntry entry = NewLedgerEntry("system", "Keyframe", new JsonObj().Add("trigger", trigger), null);
        ledger.Add(entry);

        CanonicalResult state = RecordDelta(entry);
        if (state != null)
        {
            entry.stateJson = state.Json;
            entry.stateHash = state.Hash;
        }

        roundCheckpoints.Add(new RoundCheckpoint
        {
            trigger = trigger,
            day = entry.day,
            round = entry.round,
            ledgerSeq = entry.seq,
            stateHash = entry.stateHash,
        });
    }

    static string ComputeHash(string json)
    {
        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    /// <summary>
    /// Tier 3, player-driven: one structured record per granular action a participant took.
    /// actorTarget should be a stable id (facility name/site id, task id, worker id, group id),
    /// not a display label, so a replay/analysis tool can match it without string-parsing.
    /// paramsJson is a small JSON blob of whatever that action type needs — kept as a plain
    /// string so this method doesn't need a different signature per action type.
    /// captureState = false records the action without a state delta. Use it only for actions
    /// that change no game state (clicks), so the chain does not pay for a capture on each one.
    /// Returns the new ledger entry's seq (LedgerEntry.seq), or -1 if nothing was logged (data
    /// collection off). Pass that seq to a paired CSV log call (see the Log* methods above) so
    /// the researcher-facing row can be joined to this structured entry and its state delta.
    /// </summary>
    public int LogAction(string actionType, JsonObj target, JsonObj parameters = null, bool captureState = true)
        => AddLedgerEntry("player", actionType, target, parameters, captureState);

    /// <summary>
    /// Tier 3, automatic/system-driven: the same shape as LogAction, for state changes the
    /// game makes on its own (task generation, flood/weather transitions, auto-expiry,
    /// auto-resolution, client arrivals/departures). Logged explicitly rather than left to be
    /// inferred from replaying the RNG stream — see the replay-architecture review, point 2.
    /// Returns the new ledger entry's seq — see LogAction.
    /// </summary>
    public int LogSystemEvent(string eventType, JsonObj target, JsonObj parameters = null)
        => AddLedgerEntry("system", eventType, target, parameters, captureState: true);

    int AddLedgerEntry(string source, string type, JsonObj target, JsonObj parameters, bool captureState)
    {
        if (!DataCollectionEnabled) return -1;
        LedgerEntry entry = NewLedgerEntry(source, type, target, parameters);
        ledger.Add(entry);
        if (captureState)
        {
            RecordDelta(entry);
        }
        else
        {
            entry.captured = false;
        }
        return entry.seq;
    }

    LedgerEntry NewLedgerEntry(string source, string type, JsonObj target, JsonObj parameters)
    {
        var entry = new LedgerEntry
        {
            seq = nextLedgerSeq++,
            source = source,
            type = type,
            targetJson = (target ?? new JsonObj()).ToString(),
            parametersJson = (parameters ?? new JsonObj()).ToString(),
            timestamp = Time.unscaledTime,
        };
        if (GlobalClock.Instance != null)
        {
            entry.day = GlobalClock.Instance.GetCurrentDay();
            entry.round = GlobalClock.Instance.GetCurrentTimeSegment() + 1;
        }
        else
        {
            entry.day = 1;
            entry.round = 1;
        }
        return entry;
    }

    /// <summary>
    /// Captures the full state now, diffs it against the last captured state and attaches the
    /// window delta to the entry. It returns the captured state, or null when nothing was captured.
    ///
    /// On failure the entry stays uncaptured, with the reason in captureError, and the chain does
    /// not advance. The next captured delta then still covers every change since the last good
    /// state, so one failed capture cannot break the chain. The verifier reports the gap.
    /// </summary>
    CanonicalResult RecordDelta(LedgerEntry entry)
    {
        if (replayBaseState == null)
        {
            entry.captured = false;
            entry.captureError = "no initial state anchor yet";
            return null;
        }
        try
        {
            CanonicalResult now = CanonicalState.FromSnapshotJson(
                GameSnapshotManager.ToJson(GameSnapshotManager.Capture(), pretty: false));
            List<JObject> ops = StateDiff.Diff(replayBaseState, now.Tree);
            entry.stateDeltaJson = JsonCanon.ToCanonicalString(
                StateDiff.BuildDelta(replayBaseSeq, replayBaseHash, now.Hash, ops));
            entry.captured = true;

            replayBaseState = now.Tree;
            replayBaseHash = now.Hash;
            replayBaseSeq = entry.seq;
            return now;
        }
        catch (Exception e)
        {
            entry.captured = false;
            entry.captureError = e.Message;
            Debug.LogWarning($"[GameLogPanel] Replay capture for seq {entry.seq} ({entry.type}) failed: {e.Message}");
            return null;
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // See Assets/Plugins/WebGL/BrowserRedirect.jslib
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern string GetBrowserTimeInfo();
#endif

    // Every message's realTime is DateTime.Now with no zone attached, so on its own it can't be
    // lined up across participants. Record the participant's time zone once, at the start: the
    // browser's own answer (authoritative — .NET's TimeZoneInfo.Local is unreliable in WebGL) next
    // to what .NET thinks, which also shows whether realTime is really local time or UTC.
    void LogClientTimeZone()
    {
        try
        {
            TimeSpan offset = DateTimeOffset.Now.Offset;
            string offsetText = (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString("hh\\:mm");
            string dotNetView = $"dotnet: zone={TimeZoneInfo.Local.Id}, utcOffset={offsetText}, " +
                                $"DateTime.Now={DateTime.Now:yyyy-MM-dd HH:mm:ss}, DateTime.UtcNow={DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
#if UNITY_WEBGL && !UNITY_EDITOR
            LogPlayerAction($"Client time zone | browser: {GetBrowserTimeInfo()} | {dotNetView}");
#else
            LogPlayerAction($"Client time zone | {dotNetView}");
#endif
        }
        catch (Exception e)
        {
            // Never let a diagnostic line break game start.
            LogPlayerAction($"Client time zone unavailable: {e.GetType().Name}");
        }
    }

    void InitializeUI()
    {
        if (logText == null)
            Debug.LogError("GameLogPanel: logText reference missing!");

        if (exportCurrentButton != null)
            exportCurrentButton.onClick.AddListener(() => ExportMessages(false));

        if (exportAllButton != null)
            exportAllButton.onClick.AddListener(() => ExportMessages(true));
    }

    void SetupDropdowns()
    {
        if (messageTypeDropdown != null)
        {
            messageTypeDropdown.ClearOptions();
            messageTypeDropdown.AddOptions(new List<string> { "All", "Normal", "Debug", "Error" });
            messageTypeDropdown.onValueChanged.AddListener(OnMessageTypeFilterChanged);
        }

        if (categoryDropdown != null)
        {
            categoryDropdown.ClearOptions();
            var categoryNames = System.Enum.GetNames(typeof(LogCategory)).ToList();
            categoryDropdown.AddOptions(categoryNames);
            categoryDropdown.onValueChanged.AddListener(OnCategoryFilterChanged);
        }

        if (timePeriodDropdown != null)
        {
            timePeriodDropdown.ClearOptions();
            timePeriodDropdown.AddOptions(new List<string> { "Current Round", "Today", "All Time" });
            timePeriodDropdown.onValueChanged.AddListener(OnTimePeriodFilterChanged);
        }
    }

    IEnumerator LoadDataCollectionSetting()
    {
        string configPath = Application.streamingAssetsPath + "/config.json";
        using (UnityWebRequest req = UnityWebRequest.Get(configPath))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var config = JsonUtility.FromJson<DataCollectionConfig>(req.downloadHandler.text);
                if (config != null)
                {
                    DataCollectionEnabled = config.dataCollectionEnabled;
                    Debug.Log($"[GameLogPanel] Data collection {(DataCollectionEnabled ? "enabled" : "disabled")} (from config.json)");
                }
            }
            else
            {
                Debug.Log("[GameLogPanel] config.json not found - data collection stays enabled by default.");
            }
        }
    }

    #region Public Logging Methods

    // ledgerSeq links this row to the structured ledger entry for the same event (LedgerEntry.seq);
    // leave it -1 when there is none. taskDecisionId is the shared key across one task decision's
    // rows (opened/selected/confirmed/resolved) — see LogMessage for both.
    public void LogBuildingStatus(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Buildings, ledgerSeq, taskDecisionId);
    public void LogResourceChange(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Resources, ledgerSeq, taskDecisionId);
    public void LogWorkerAction(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Workers, ledgerSeq, taskDecisionId);
    public void LogTaskEvent(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Tasks, ledgerSeq, taskDecisionId);
    public void LogEnvironmentChange(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Environment, ledgerSeq, taskDecisionId);
    public void LogVehicleEvent(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Vehicles, ledgerSeq, taskDecisionId);
    public void LogMetricsChange(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Metrics, ledgerSeq, taskDecisionId);
    public void LogPlayerAction(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Player, ledgerSeq, taskDecisionId);
    public void LogDebug(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Debug, LogCategory.Player, ledgerSeq, taskDecisionId);
    public void LogError(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Error, LogCategory.Player, ledgerSeq, taskDecisionId);
    public void LogUIInteraction(string message, int ledgerSeq = -1, string taskDecisionId = null) => AddLogMessage(message, LogMessageType.Normal, LogCategory.UI, ledgerSeq, taskDecisionId);

    /// <summary>
    /// Structured UI interaction: logs locally AND forwards a semantic
    /// ui_interaction event to the router (per-actor unified log), correlated to
    /// the current click via GuiInteractionRecorder.LastClickSeq. Use this for
    /// decision-support interactions (open agent conversation, switch officer,
    /// select/switch a choice package, confirm, open metrics, inspect facility).
    /// </summary>
    public void LogUIInteraction(string category, string name, string detail = null, int ledgerSeq = -1, string taskDecisionId = null)
    {
        AddLogMessage(detail != null ? $"{name} | {detail}" : name,
                      LogMessageType.Normal, LogCategory.UI, ledgerSeq, taskDecisionId);
        WebSocketManager.Instance?.SendClientEvent(
            category, name, detail, GuiInteractionRecorder.LastClickSeq);
    }

    #endregion

    void AddLogMessage(string content, LogMessageType type, LogCategory category, int ledgerSeq = -1, string taskDecisionId = null)
    {
        if (isQuitting)
            return;

        if (!DataCollectionEnabled)
            return;

        if (type == LogMessageType.Debug && !enableDebugMessages)
            return;

        LogMessage message = new LogMessage(content, type, category, ledgerSeq, taskDecisionId);
        allMessages.Add(message);

        if (PassesCurrentFilters(message))
        {
            string formattedMessage = FormatMessageForDisplay(message);
            displayQueue.Enqueue(formattedMessage);

            if (!isDisplayingMessage)
            {
                StartCoroutine(DisplayNextMessage());
            }
        }
    }

    bool PassesCurrentFilters(LogMessage message)
    {
        int typeDropdownValue = messageTypeDropdown != null ? messageTypeDropdown.value : 0;
        if (typeDropdownValue != 0)
        {
            LogMessageType expectedType = LogMessageType.Normal;
            switch (typeDropdownValue)
            {
                case 1: expectedType = LogMessageType.Normal; break;
                case 2: expectedType = LogMessageType.Debug; break;
                case 3: expectedType = LogMessageType.Error; break;
            }

            LogMessageType msgType = (LogMessageType)System.Enum.Parse(typeof(LogMessageType), message.messageType);
            if (msgType != expectedType) return false;
        }

        LogCategory msgCategory = (LogCategory)System.Enum.Parse(typeof(LogCategory), message.category);
        if (currentCategoryFilter != LogCategory.All && msgCategory != currentCategoryFilter)
            return false;

        if (GlobalClock.Instance != null)
        {
            int currentDay = GlobalClock.Instance.GetCurrentDay();
            int currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + 1;

            switch (currentTimePeriodFilter)
            {
                case 0:
                    if (message.day != currentDay || message.round != currentRound) return false;
                    break;
                case 1:
                    if (message.day != currentDay) return false;
                    break;
                case 2:
                    break;
            }
        }

        return true;
    }

    string FormatMessageForDisplay(LogMessage message)
    {
        LogMessageType msgType = (LogMessageType)System.Enum.Parse(typeof(LogMessageType), message.messageType);
        Color messageColor = GetMessageColor(msgType);
        string timeStamp = $"[Day {message.day}, Round {message.round}]";
        string categoryTag = $"[{message.category}]";

        string colorHex = ColorUtility.ToHtmlStringRGB(messageColor);
        return $"<color=#AAAAAA>{timeStamp}</color> <color=#{colorHex}>{categoryTag} {message.content}</color>";
    }

    Color GetMessageColor(LogMessageType type)
    {
        switch (type)
        {
            case LogMessageType.Debug: return debugTextColor;
            case LogMessageType.Error: return errorTextColor;
            default: return normalTextColor;
        }
    }

    IEnumerator DisplayNextMessage()
    {
        isDisplayingMessage = true;
        IsDisplayingText = true;

        while (displayQueue.Count > 0)
        {
            string message = displayQueue.Dequeue();
            logText.text += message + "\n";

            string[] lines = logText.text.Split('\n');
            if (lines.Length > maxDisplayedMessages)
            {
                logText.text = string.Join("\n", lines.Skip(lines.Length - maxDisplayedMessages));
            }

            // Only update mesh if logText has valid font/material references
            if (logText != null && logText.font != null)
            {
                logText.ForceMeshUpdate();

                if (contentRect != null)
                {
                    contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, logText.preferredHeight + 20);
                }
            }
            else if (logText != null)
            {
                Debug.LogWarning("[GameLogPanel] TextMeshPro component missing font asset. Skipping mesh update.");
            }

            if (autoScrollToBottom && scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }

            yield return null;
        }

        isDisplayingMessage = false;
        IsDisplayingText = false;
    }

    // Auto-send on Day 8 daily report method — should call this from DailyReportManager when showing Day 8 report
    public void TriggerEndGameLogSend()
    {
        LogPlayerAction("Day 8 report reached — auto-sending all logs");
        if (LogSender.Instance != null)
            LogSender.Instance.SendAllLogs();
    }

    // Called from DailyReportManager when a day's report is shown (every day except the last, which
    // uses TriggerEndGameLogSend). Uploads the whole log so far as a cumulative checkpoint, so a
    // participant who drops out or whose final upload fails still leaves data up to their last day.
    public void TriggerDayCheckpointSend(int day)
    {
        LogPlayerAction($"Day {day} report reached — uploading checkpoint");
        if (LogSender.Instance != null)
            LogSender.Instance.SendDayCheckpoint(day);
    }

    #region Filter Event Handlers

    void OnMessageTypeFilterChanged(int value)
    {
        // Type filtering currently not implemented
        // switch (value)
        // {
        //     case 0: currentTypeFilter = LogMessageType.Normal; break;
        //     case 1: currentTypeFilter = LogMessageType.Normal; break;
        //     case 2: currentTypeFilter = LogMessageType.Debug; break;
        //     case 3: currentTypeFilter = LogMessageType.Error; break;
        // }
        RefreshDisplay();
    }

    void OnCategoryFilterChanged(int value)
    {
        currentCategoryFilter = (LogCategory)value;
        RefreshDisplay();
    }

    void OnTimePeriodFilterChanged(int value)
    {
        currentTimePeriodFilter = value;
        RefreshDisplay();
    }

    #endregion

    void RefreshDisplay()
    {
        logText.text = "";
        displayQueue.Clear();

        var filteredMessages = allMessages.Where(PassesCurrentFilters).ToList();

        foreach (var message in filteredMessages.TakeLast(maxDisplayedMessages))
        {
            string formattedMessage = FormatMessageForDisplay(message);
            logText.text += formattedMessage + "\n";
        }

        // Only update mesh if logText has valid font/material references
        if (logText != null && logText.font != null)
        {
            logText.ForceMeshUpdate();

            if (contentRect != null)
            {
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, logText.preferredHeight + 20);
            }
        }

        if (autoScrollToBottom && scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    /// <summary>
    /// Reads configuration directly from the live objects that actually operate on it, rather
    /// than from GameConfigLoader's sheet fetch or the map config's own embedded parameters —
    /// see the authoritativeConfigJson field comment for why those two can disagree with each
    /// other and with what the game actually used. Three fields (budget, satisfaction, weather,
    /// community count) are overridden from their more-specific live owner; facility capacities,
    /// casework departures and construction costs come from the BuildingSystem prefabs, and the
    /// motel rate from MotelCostManager, since those are what buildings actually run on (D20);
    /// everything else comes from GameDataManager, since its resolved value is what every other system that
    /// reads GameDataManager.Instance.InitialXxx actually operates on, regardless of which
    /// upstream source that value itself came from.
    /// </summary>
    string BuildAuthoritativeConfigJson()
    {
        try
        {
            var o = new JsonObj();
            var gdm = GameDataManager.Instance;

            // Facility capacities as built: the sheet/GameDataManager capacities are not applied to
            // buildings (parity ledger D20) -- a new facility takes what its prefab says, so read the
            // prefabs BuildingSystem instantiates, falling back to GameDataManager only if missing.
            var bs = FindObjectOfType<BuildingSystem>();
            int? Cap(GameObject prefab, ResourceType type)
            {
                var st = prefab != null ? prefab.GetComponentInChildren<BuildingResourceStorage>(true) : null;
                if (st == null) return null;
                foreach (var c in st.resourceCapacities)
                    if (c.resourceType == type) return c.maxCapacity;
                return null;
            }
            int? shelterBeds = bs != null ? Cap(bs.shelterPrefab, ResourceType.Population) : null;
            int? caseworkClients = bs != null ? Cap(bs.caseworkSitePrefab, ResourceType.Population) : null;

            if (gdm != null)
            {
                o.Add("gameDurationDays", gdm.InitialGameDays)
                 .Add("roundsPerDay", gdm.InitialRoundsPerDay)
                 .Add("dailyBudgetAddition", gdm.InitialDailyBudgetAddition)
                 .Add("trainedVolunteers", gdm.InitialTrainedVolunteerCount)
                 .Add("untrainedVolunteers", gdm.InitialUntrainedVolunteerCount)
                 .Add("residentsPerCommunity", gdm.InitialResidentsPerCommunityNumber)
                 .Add("kitchenCapacity", gdm.InitialKitchenCapacity)
                 .Add("shelterCapacity", shelterBeds ?? gdm.InitialShelterCapacity)
                 .Add("caseworkCapacity", caseworkClients ?? gdm.InitialCaseworkCapacity)
                 .Add("requiredWorkersPerLoc", gdm.InitialRequiredWorkersPerLoc)
                 .Add("foodDemandFrequency", gdm.InitialFoodDemandFrequency)
                 .Add("ervCount", gdm.InitialERVCount)
                 .Add("externalRelationFrequency", gdm.InitialExternalRelationFrequency)
                 .Add("emergencyTaskFrequency", gdm.InitialEmergencyTaskFrequency)
                 .Add("shelterFloodThreshold", gdm.InitialShelterFloodThreshold)
                 .Add("shelterFloodRadius", gdm.InitialShelterFloodRadius)
                 .Add("shelterFloodComparison", gdm.InitialShelterFloodComparison.ToString());
            }
            // Overrides: the three fields the Day 1 cross-check actually caught disagreeing,
            // each read from the specific live system that owns it operationally.
            if (SatisfactionAndBudget.Instance != null)
                o.Add("budget", SatisfactionAndBudget.Instance.GetCurrentBudget())
                 .Add("satisfaction", SatisfactionAndBudget.Instance.GetCurrentSatisfaction());
            if (WeatherSystem.Instance != null)
                o.Add("weather", WeatherSystem.Instance.GetCurrentWeather().ToString());
            var mapSystem = FindObjectOfType<MapSystem>();
            if (mapSystem != null)
                o.Add("communityCount", mapSystem.numberOfCommunities);

            // Facility food capacities, casework turnover and construction costs, as built.
            if (bs != null)
            {
                int? kitchenFood = Cap(bs.kitchenPrefab, ResourceType.FoodPacks);
                int? shelterFood = Cap(bs.shelterPrefab, ResourceType.FoodPacks);
                if (kitchenFood.HasValue) o.Add("kitchenFoodCapacity", kitchenFood.Value);
                if (shelterFood.HasValue) o.Add("shelterFoodCapacity", shelterFood.Value);

                var caseworkStorage = bs.caseworkSitePrefab != null
                    ? bs.caseworkSitePrefab.GetComponentInChildren<BuildingResourceStorage>(true) : null;
                if (caseworkStorage != null)
                    o.Add("caseworkDeparturesPerRound", caseworkStorage.caseworkDeparturesPerRound);

                o.Add("kitchenConstructionCost", bs.kitchenConstructionCost)
                 .Add("shelterConstructionCost", bs.shelterConstructionCost)
                 .Add("caseworkSiteConstructionCost", bs.caseworkSiteConstructionCost);
            }
            var motelCost = FindObjectOfType<MotelCostManager>();
            if (motelCost != null)
                o.Add("motelCostPerPersonPerDay", motelCost.costPerPersonPerDay)
                 .Add("motelCostPerPersonPerRound", motelCost.GetCostPerPersonPerRound());
            return o.ToString();
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameLogPanel] Authoritative config capture failed: {e.Message}");
            return "{}";
        }
    }

    #region Export Methods

    public void ExportMessages(bool exportAll = false)
    {
        List<LogMessage> messagesToExport;

        if (exportAll)
        {
            messagesToExport = allMessages.ToList();
        }
        else
        {
            if (GlobalClock.Instance != null)
            {
                int currentDay = GlobalClock.Instance.GetCurrentDay();
                int currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + 1;

                messagesToExport = allMessages.Where(m =>
                    m.day == currentDay && m.round == currentRound).ToList();
            }
            else
            {
                messagesToExport = allMessages.ToList();
            }
        }

        LogExportData exportData = new LogExportData(messagesToExport);
        string json = JsonUtility.ToJson(exportData, true);

        Debug.Log("=== LOG EXPORT ===");
        Debug.Log(json);

        LogPlayerAction($"Exported {messagesToExport.Count} log messages");
    }

    public string GetMessagesAsJson(bool exportAll = false, string uploadKind = "final", int checkpointDay = 0)
    {
        List<LogMessage> messagesToExport = exportAll ?
            allMessages.ToList() :
            allMessages.Where(m => GlobalClock.Instance != null &&
                m.day == GlobalClock.Instance.GetCurrentDay() &&
                m.round == GlobalClock.Instance.GetCurrentTimeSegment() + 1).ToList();

        LogExportData exportData = new LogExportData(messagesToExport);
        exportData.uploadKind = uploadKind;
        exportData.checkpointDay = checkpointDay;

        // Inject the episode's random state (captured before the scene loaded)
        exportData.rngState  = EpisodeReproLog.RngStateJson;
        exportData.buildGuid = EpisodeReproLog.BuildGuid;
        exportData.seed       = EpisodeSeed.Seed;
        exportData.seedSource = EpisodeSeed.Source;
        exportData.mapFromServer = GameConfigLoader.Instance != null && GameConfigLoader.Instance.HasServerMapConfig();
        exportData.mapConfigHash = GameConfigLoader.MapHash;
        if (exportData.mapFromServer)
        {
            try { exportData.mapConfigJson = JsonUtility.ToJson(GameConfigLoader.Instance.GetMapConfig()); }
            catch (Exception e) { Debug.LogError($"[GameLogPanel] Map config serialization failed: {e.Message}"); }
        }
        exportData.authoritativeConfigJson = BuildAuthoritativeConfigJson();

        // Full cumulative lists each upload (same lifetime as allMessages/messages above) —
        // simpler and safer than trying to track what a previous checkpoint already sent, at
        // the cost of some redundant bytes on later checkpoints.
        exportData.initialSnapshotJson = initialSnapshotJson;
        exportData.initialSnapshotHash = initialSnapshotHash;
        exportData.initialStateHash = initialStateHash;
        exportData.canonicalSchemaVersion = CanonicalState.SchemaVersion;
        exportData.roundCheckpoints = roundCheckpoints;
        exportData.ledger = ledger;

        // Inject environment config
        if (GameConfigLoader.Instance != null)
        {
            GameConfigLoader c = GameConfigLoader.Instance;
            exportData.initialBudget      = c.GetInitialBudget();
            exportData.initialSatisfaction = c.GetInitialSatisfaction();
            exportData.initialCommunityCount        = c.GetInitialCommunityCount();
            exportData.initialResidentsPerCommunity = c.GetInitialResidentCountPerCommunity();
            exportData.initialNumDays               = c.GetInitialNumDays();
            exportData.initialRoundsPerDay          = c.GetInitialNumRoundsPerGame();
            exportData.initialTrainedVolunteers     = c.GetInitialTrainedVolunteerCount();
            exportData.initialUntrainedVolunteers   = c.GetInitialUntrainedVolunteerCount();
            exportData.initialBudgetDailyAdditions  = c.GetInitialBudgetDailyAdditions();
            exportData.initialWeather               = c.GetInitialWeather().ToString();
            exportData.initialKitchenCapacity       = c.GetInitialKitchenCapacity();
            exportData.initialShelterCapacity       = c.GetInitialShelterCapacity();
            exportData.initialCaseworkCapacity      = c.GetInitialCaseworkCapacity();
            exportData.initialNeededWorkersPerLoc   = c.GetInitialNeededWorkersPerLoc();
            exportData.initialFoodDemandFrequency   = c.GetInitialFoodDemandFrequency();
            exportData.initialERVCount              = c.GetInitialERVCount();
            exportData.initialExternalRelationFrequency = c.GetInitialExternalRelationFrequency();
            exportData.initialEmergencyTaskFrequency    = c.GetInitialEmergencyTaskFrequency();
            exportData.initialShelterFloodThreshold  = c.GetInitialShelterFloodThreshold();
            exportData.initialShelterFloodRadius     = c.GetInitialShelterFloodRadius();
            exportData.initialShelterFloodComparison = c.GetInitialShelterFloodComparison().ToString();
            exportData.floodExpansionRates = new[] {
                c.GetInitialSunnyFloodExpansionRate(),
                c.GetInitialSmallRainFloodExpansionRate(),
                c.GetInitialMediumRainFloodExpansionRate(),
                c.GetInitialHeavyRainFloodExpansionRate(),
                c.GetInitialStormFloodExpansionRate(),
            };
            exportData.floodSpreadMultipliers = new[] {
                c.GetInitialSunnyFloodSpreadChanceMultiplier(),
                c.GetInitialSmallRainFloodSpreadChanceMultiplier(),
                c.GetInitialMediumRainFloodSpreadChanceMultiplier(),
                c.GetInitialHeavyRainFloodSpreadChanceMultiplier(),
                c.GetInitialStormFloodSpreadChanceMultiplier(),
            };
        }

        return JsonUtility.ToJson(exportData, true);
    }

    public LogExportData GetExportData(bool exportAll = false)
    {
        List<LogMessage> messagesToExport = GetMessagesToExport(exportAll);
        return new LogExportData(messagesToExport);
    }

    public void ExportToCSV(bool exportAll = false)
    {
        List<LogMessage> messagesToExport = GetMessagesToExport(exportAll);

        string fileName = exportAll ?
            $"game_logs_all_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv" :
            $"game_logs_current_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";

        string filePath = Path.Combine(Application.persistentDataPath, fileName);

        try
        {
            List<string> csvLines = new List<string>();

            csvLines.Add("Timestamp,RealTime,Day,Round,Budget,Satisfaction,Efficiency,ActiveTaskCount,StateSummary,Category,MessageType,Content,LedgerSeq,TaskDecisionId");

            foreach (LogMessage message in messagesToExport)
            {
                string cleanContent = CleanContentForCSV(message.content);

                string csvLine = string.Join(",", new string[]
                {
                    message.timestamp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    QuoteAndEscape(message.realTime),
                    message.day.ToString(),
                    message.round.ToString(),
                    message.budget.ToString(),
                    message.satisfaction.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    message.efficiency.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    message.activeTaskCount.ToString(),
                    QuoteAndEscape(message.stateSummary),
                    message.category,
                    message.messageType,
                    QuoteAndEscape(cleanContent),
                    message.ledgerSeq >= 0 ? message.ledgerSeq.ToString() : "",
                    QuoteAndEscape(message.taskDecisionId)
                });

                csvLines.Add(csvLine);
            }

            File.WriteAllLines(filePath, csvLines, System.Text.Encoding.UTF8);

            Debug.Log($"CSV exported successfully to: {filePath}");
            LogPlayerAction($"Exported {messagesToExport.Count} messages to CSV: {fileName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to export CSV: {e.Message}");
            LogError($"CSV export failed: {e.Message}");
        }
    }

    private string CleanContentForCSV(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "";

        content = content.Replace("\r\n", " ")
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Replace("\t", " ")
                        .Replace("\"", "\"\"");

        while (content.Contains("  "))
        {
            content = content.Replace("  ", " ");
        }

        return content.Trim();
    }

    private string QuoteAndEscape(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "\"\"";

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    private List<LogMessage> GetMessagesToExport(bool exportAll)
    {
        if (exportAll)
        {
            return allMessages.ToList();
        }

        if (GlobalClock.Instance != null)
        {
            int currentDay = GlobalClock.Instance.GetCurrentDay();
            int currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + 1;

            return allMessages.Where(m => m.day == currentDay && m.round == currentRound).ToList();
        }

        return allMessages.ToList();
    }

    #endregion

    public void ClearLog()
    {
        allMessages.Clear();
        displayQueue.Clear();
        logText.text = "";
        isDisplayingMessage = false;
        IsDisplayingText = false;

        LogPlayerAction("Game log cleared");
    }

    [ContextMenu("Test Log Message")]
    void TestLogMessage()
    {
        LogBuildingStatus("Kitchen started food production");
        LogBuildingStatus("Shelter damaged by flood");
        LogResourceChange("Produced 10 meals");
        LogResourceChange("Consumed 5 meals");
        LogWorkerAction("Assigned 2 trained workers to Kitchen");
        LogWorkerAction("Worker training completed");
        LogTaskEvent("Emergency food task completed");
        LogTaskEvent("Population transport task generated");
        LogEnvironmentChange("Weather changed to Rainy");
        LogEnvironmentChange("Flood expanded to 5 tiles");
        LogVehicleEvent("Vehicle completed food delivery");
        LogVehicleEvent("Vehicle damaged by flood");
        LogMetricsChange("Satisfaction increased by 10");
        LogMetricsChange("Budget decreased by $500");
        LogPlayerAction("Player opened task center");
        LogPlayerAction("Player selected emergency response");
        LogDebug("Pathfinding calculation completed");
        LogError("Failed to create delivery task");
    }

    [ContextMenu("Export Current to CSV")]
    void TestExportCurrentCSV()
    {
        ExportToCSV(false);
    }

    [ContextMenu("Export All to CSV")]
    void TestExportAllCSV()
    {
        ExportToCSV(true);
    }
}