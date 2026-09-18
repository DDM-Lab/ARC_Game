using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Records one fingerprint line per round so two builds can be compared for behavioural
/// parity — "does this branch, with the LLM teammates off, still play the game exactly like
/// main-bugfixes?"
///
/// WHY THE RNG CURSOR IS THE PRIMARY SIGNAL. Every stochastic system in this game — flood,
/// weather, task triggers, client stays, deliveries — draws from the ONE global
/// UnityEngine.Random stream. So `Random.state` is a running checksum of every draw taken so
/// far: if two builds agree on it at the end of round N, they have consumed identical draws in
/// identical order up to that point. If they disagree, the divergence happened during round N,
/// and you know that before diffing a single facility. It is far more sensitive than comparing
/// derived state, because a skipped or extra draw shows up immediately even when its visible
/// effect is rounds away (which is exactly the D1 casework bug).
///
/// Reading Random.state does NOT advance the stream — the same property EpisodeReproLog relies
/// on. This probe must never DRAW; a probe that perturbs the thing it measures is worse than no
/// probe, because it produces confident wrong answers.
///
/// DESIGNED TO COMPILE AND RUN ON MAIN-BUGFIXES UNCHANGED. It touches only symbols that exist
/// identically on both branches (GlobalClock.currentDay/currentTimeSegment,
/// SatisfactionAndBudget.GetCurrentBudget/GetCurrentSatisfaction, Building, TaskSystem) and
/// installs itself via RuntimeInitializeOnLoadMethod, so no scene edit is needed — which
/// matters because the scenes are LFS-tracked and conflict whole-file on merge.
///
/// OFF UNLESS ASKED FOR: -parity-probe on the command line, ARC_PARITY_PROBE=1, or ?parity=1
/// on a WebGL URL. With none of those, nothing below Install() runs.
/// </summary>
public class ParityProbe : MonoBehaviour
{
    const string FILE_PREFIX = "parity_trace";

    static ParityProbe instance;
    readonly List<string> lines = new List<string>();
    int lastDay = -1, lastSegment = -1;
    string path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        if (!Wanted()) return;
        var go = new GameObject("[ParityProbe]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ParityProbe>();
        Debug.Log("[ParityProbe] armed — one fingerprint per round.");
    }

    static bool Wanted()
    {
        try
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (a == "-parity-probe" || a == "--parity-probe") return true;
            string env = Environment.GetEnvironmentVariable("ARC_PARITY_PROBE");
            if (!string.IsNullOrEmpty(env) && env != "0") return true;
        }
        catch (Exception) { }
        try
        {
            string url = Application.absoluteURL;
            if (!string.IsNullOrEmpty(url) && url.Contains("parity=1")) return true;
        }
        catch (Exception) { }
        return false;
    }

    void Start()
    {
        // An explicit destination when one is given (ARC_PARITY_TRACE). The default writes a
        // timestamped file into persistentDataPath and the runner picks the newest — which is a
        // race the moment two runs overlap, and overlapping runs are the only way to make a
        // 64-episode suite finish in minutes rather than an hour. Two runs starting inside the
        // same second would also collide on the filename itself.
        string explicitPath = null;
        try { explicitPath = Environment.GetEnvironmentVariable("ARC_PARITY_TRACE"); }
        catch (Exception) { }
        path = !string.IsNullOrEmpty(explicitPath)
            ? explicitPath
            : System.IO.Path.Combine(Application.persistentDataPath,
                  $"{FILE_PREFIX}_{DateTime.UtcNow:yyyyMMdd-HHmmss}_{System.Diagnostics.Process.GetCurrentProcess().Id}.jsonl");
        Debug.Log($"[ParityProbe] writing {path}");
        Emit("boot");
    }

    void Update()
    {
        var clock = GlobalClock.Instance;
        if (clock == null) return;
        // Polled rather than event-driven ON PURPOSE. The obvious hook, OnRoundEnd, is itself
        // one of the divergences under test (this branch invokes it, main-bugfixes leaves it
        // commented out) — a probe that subscribes to it would fire on one build and not the
        // other and manufacture the very difference it is meant to detect. Day/segment are
        // plain fields present on both branches.
        if (clock.currentDay == lastDay && clock.currentTimeSegment == lastSegment) return;
        lastDay = clock.currentDay;
        lastSegment = clock.currentTimeSegment;
        Emit("round");
    }

    void OnApplicationQuit() { Flush(); }

    void Emit(string reason)
    {
        try
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"reason\":\"").Append(reason).Append('"');
            var clock = GlobalClock.Instance;
            sb.Append(",\"day\":").Append(clock != null ? clock.currentDay : -1);
            sb.Append(",\"segment\":").Append(clock != null ? clock.currentTimeSegment : -1);

            // THE CHECKSUM. JsonUtility round-trips Random.State, which has no public fields.
            sb.Append(",\"rng\":").Append(JsonUtility.ToJson(UnityEngine.Random.state));

            var econ = SatisfactionAndBudget.Instance;
            sb.Append(",\"budget\":").Append(econ != null ? econ.GetCurrentBudget() : -1);
            sb.Append(",\"satisfaction\":")
              .Append(econ != null ? econ.GetCurrentSatisfaction().ToString("R") : "-1");
            sb.Append(",\"efficiency\":")
              .Append(econ != null ? econ.GetCurrentEfficiency().ToString("R") : "-1");

            // Facilities, ordered deterministically by name so the line is comparable across
            // builds regardless of the order FindObjectsOfType happens to return.
            var buildings = FindObjectsOfType<Building>();
            var rows = new List<string>(buildings.Length);
            foreach (var b in buildings)
            {
                if (b == null) continue;
                rows.Add($"{b.name}|{b.GetBuildingType()}|{b.GetCurrentStatus()}");
            }
            // PREBUILTS TOO, WITH THEIR STOCK. Recording only constructed `Building`s meant that
            // in a no-action episode this list was EMPTY for every round, and the communities --
            // where the whole early game actually happens -- were invisible. Ledger D19/D20 cost
            // several rebuilds because of it: the two builds' community population differed by a
            // factor of ten from the first round, the trace could not see it, and the difference
            // only surfaced two days later when it finally perturbed the RNG. Population and food
            // packs are what the relocation and food-request triggers read, so they belong in the
            // fingerprint.
            foreach (var pb in FindObjectsOfType<PrebuiltBuilding>())
            {
                if (pb == null) continue;
                var st = pb.GetResourceStorage();
                string pop = st != null ? st.GetResourceAmount(ResourceType.Population).ToString() : "?";
                string food = st != null ? st.GetResourceAmount(ResourceType.FoodPacks).ToString() : "?";
                rows.Add($"{pb.name}|{pb.GetPrebuiltType()}|pop={pop}|food={food}");
            }
            rows.Sort(StringComparer.Ordinal);
            sb.Append(",\"facilities\":[");
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(rows[i].Replace("\"", "'")).Append('"');
            }
            sb.Append(']');

            // PROVENANCE. The first run of this harness produced a confident-looking divergence
            // that turned out to be two builds loading DIFFERENT MAPS and DIFFERENT PARAMETER
            // SHEETS, with one of them also connected to the live router. Nothing in the trace
            // said so, so the diff reported a game-logic finding that did not exist. These fields
            // exist so that can never happen silently again: diff_traces.py refuses to compare
            // two traces whose environment differs.
            sb.Append(",\"env\":").Append(EnvBlock());

            sb.Append('}');
            lines.Add(sb.ToString());
            if (lines.Count % 8 == 0) Flush();
        }
        catch (Exception e)
        {
            // A probe must never take the game down with it.
            Debug.LogWarning($"[ParityProbe] emit failed: {e.Message}");
        }
    }

    /// <summary>
    /// A JSON object describing the CONDITIONS this episode ran under, as opposed to what
    /// happened in it. Everything here is read by reflection and every read is individually
    /// guarded, because this file has to compile and run unchanged on main-bugfixes, where
    /// WebSocketManager does not exist at all and GameDataManager has a different field set.
    /// A field that cannot be read reports "?" rather than failing the run — an unknown
    /// condition still blocks the comparison, which is the behaviour we want.
    /// </summary>
    static string EnvBlock()
    {
        var sb = new StringBuilder(160);
        sb.Append("{\"params\":\"").Append(ParamFingerprint()).Append('"');
        sb.Append(",\"llm\":\"").Append(LlmState()).Append('"');
        sb.Append(",\"objects\":").Append(MapFingerprint());
        sb.Append(",\"hermetic\":").Append(HermeticFlag());
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>"true"/"false" via reflection so this file still compiles if ParityEnv is
    /// not grafted onto a build; an ungrafted build reports false and the diff refuses.</summary>
    static int mapFingerprint = 0;

    /// <summary>
    /// A fingerprint of the MAP, frozen once startup has settled. Two builds that disagree here
    /// are playing different geography and cannot be compared however well their code matches.
    ///
    /// It took three tries to make this measure the right thing, which is worth recording
    /// because each wrong version looked fine until it was tested against a known map swap:
    ///
    ///  * counting scene ROOT objects reported 21 for BOTH a built-in layout and a downloaded
    ///    67-object map — MapConfigApplier parents everything it spawns under existing objects,
    ///    so nothing new ever reaches the root.
    ///  * counting EVERY Transform saw the swap (1522 vs 1570), but also counted tasks, clients
    ///    and vehicles spawning during play, and counted this branch's extra LLM singletons —
    ///    reporting 1470 against upstream's 1469 on identical maps, which would have blocked
    ///    every future comparison for a reason that has nothing to do with the map.
    ///  * counting the descendants of the applier's own two content parents measures the map and
    ///    only the map. That is what this does.
    ///
    /// Frozen at five seconds, past the map coroutine on both branches. If a map ever landed
    /// later than that the two runs would report different counts and the comparison would be
    /// refused — the safe direction to fail in.
    /// </summary>
    static int MapFingerprint()
    {
        if (mapFingerprint > 0) return mapFingerprint;
        try
        {
            if (Time.realtimeSinceStartup < 5f) return 0;   // "not settled yet"
            int n = 0;
            foreach (var applier in FindObjectsOfType<MapConfigApplier>(true))
            {
                if (applier == null) continue;
                if (applier.treesParent != null)
                    n += applier.treesParent.GetComponentsInChildren<Transform>(true).Length;
                if (applier.prebuiltParent != null)
                    n += applier.prebuiltParent.GetComponentsInChildren<Transform>(true).Length;
            }
            // 0 would read as "not settled yet" forever; -1 says "asked and found nothing",
            // which is a real difference between builds if only one of them has an applier.
            mapFingerprint = n > 0 ? n : -1;
        }
        catch (Exception) { }
        return mapFingerprint;
    }

    static string HermeticFlag()
    {
        try
        {
            var t = Type.GetType("ParityEnv");
            if (t == null) return "false";
            object v = t.GetProperty("Active")?.GetValue(null);
            return (v is bool b && b) ? "true" : "false";
        }
        catch (Exception) { return "false"; }
    }

    static string ParamFingerprint()
    {
        try
        {
            var t = Type.GetType("GameDataManager");
            if (t == null) return "?";
            var inst = t.GetProperty("Instance")?.GetValue(null)
                    ?? t.GetField("Instance")?.GetValue(null);
            if (inst == null) return "none";
            string[] names = { "InitialBudget", "InitialSatisfaction", "InitialGameDays" };
            var parts = new List<string>(names.Length);
            foreach (string n in names)
            {
                object v = t.GetField(n)?.GetValue(inst) ?? t.GetProperty(n)?.GetValue(inst);
                parts.Add(v == null ? "?" : Convert.ToString(v,
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            return string.Join("/", parts.ToArray());
        }
        catch (Exception) { return "?"; }
    }

    static string LlmState()
    {
        try
        {
            var t = Type.GetType("WebSocketManager");
            if (t == null) return "absent";           // main-bugfixes: no LLM code in the build
            var inst = t.GetField("Instance")?.GetValue(null)
                    ?? t.GetProperty("Instance")?.GetValue(null);
            if (inst == null) return "off";
            object connected = t.GetField("isConnected")?.GetValue(inst)
                            ?? t.GetProperty("isConnected")?.GetValue(inst);
            return connected is bool b && b ? "CONNECTED" : "off";
        }
        catch (Exception) { return "?"; }
    }

    void Flush()
    {
        if (lines.Count == 0 || string.IsNullOrEmpty(path)) return;
        try
        {
            System.IO.File.AppendAllLines(path, lines);
            lines.Clear();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ParityProbe] flush failed: {e.Message}");
        }
    }
}
