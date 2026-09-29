using UnityEngine;

/// <summary>
/// Records the random state an episode STARTED from, so a played session can be reproduced
/// later and replayed move-for-move.
///
/// WHY THIS IS NEEDED. Every stochastic system in the game — FloodSystem, WeatherSystem,
/// TaskTrigger, TaskSystem, ClientStayTracker, DeliverySystem, ResourceFlowManager — draws
/// from the single global UnityEngine.Random stream. Capturing that one state therefore
/// captures the whole scenario, with no per-system plumbing. Without it, a tester who hits a
/// confusing situation cannot be put back into it, and their session cannot be lined up
/// against an automated run of the same scenario.
///
/// THIS TAKES NO CONTROL OF ANYTHING. It never calls Random.InitState and never changes a
/// draw. It only READS Random.state, which does not advance the stream, and writes one line.
/// Removing this file restores the previous behaviour exactly.
///
/// TIMING IS THE SUBSTANCE. The flood layout and the opening task roll are decided inside
/// MainScene's Awake/Start chain, so the state has to be read BEFORE the scene loads — a
/// read afterwards describes a stream that has already produced the scenario, and replaying
/// from it yields a different game. Hence RuntimeInitializeLoadType.BeforeSceneLoad.
///
/// The line is emitted twice on purpose: once to the Unity log (visible in the browser
/// console / Player.log), and once through GameLogPanel so it rides along in the payload
/// LogSender uploads, which is what makes a tester's session reproducible without asking
/// them to send a file.
/// </summary>
public static class EpisodeReproLog
{
    /// <summary>The four-integer UnityEngine.Random.State as JSON, captured before the scene
    /// loaded. Empty only if capture somehow did not run.</summary>
    public static string RngStateJson { get; private set; } = "";

    /// <summary>Identifies the build the state was captured on. A state replayed on a
    /// different build, or under a different parameter sheet, silently produces a different
    /// game — so the state is only meaningful alongside this.</summary>
    public static string BuildGuid { get; private set; } = "";

    static bool reported;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Capture()
    {
        // JsonUtility round-trips Random.State, which has no public fields of its own.
        RngStateJson = JsonUtility.ToJson(UnityEngine.Random.state);
        BuildGuid = Application.buildGUID;
        reported = false;
        Debug.Log($"[EpisodeRepro] rngState={RngStateJson} build={BuildGuid} version={Application.version}");
    }

    /// <summary>Re-read the start state after EpisodeSeed replaced a URL seed (testMode). Runs
    /// on the title screen, before any scene draws randomness, so the state still describes the
    /// episode's start. The one-line report is written later, once MainScene's log exists.</summary>
    internal static void Recapture()
    {
        Capture();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallReporter()
    {
        var go = new GameObject("[EpisodeReproLog]");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Reporter>();
    }

    /// <summary>
    /// Waits for GameLogPanel to exist, then writes the captured state into the game log so
    /// it is part of the uploaded payload. A MonoBehaviour is needed only because the panel
    /// is not alive at BeforeSceneLoad; it does nothing else and stops after one line.
    /// </summary>
    class Reporter : MonoBehaviour
    {
        float elapsed;

        void Update()
        {
            if (reported) { enabled = false; return; }

            // The log only exists in MainScene, and a human reaches it after the title, info and
            // tutorial screens -- often minutes. The old 30 s limit gave up long before that, so
            // human runs never got this line. Wait up to an hour (a null check per frame).
            elapsed += Time.unscaledDeltaTime;
            if (elapsed > 3600f) { enabled = false; return; }

            if (GameLogPanel.Instance == null) return;

            GameLogPanel.Instance.LogPlayerAction(
                $"Episode repro: rngState={RngStateJson} build={BuildGuid}");
            // The same facts as one machine-readable record, first in the Data stream.
            GameLogPanel.Instance.LogData("episode_start", GameLogPanel.Json(
                "seed", EpisodeSeed.Seed, "seed_source", EpisodeSeed.Source,
                "rng_state", new GameLogPanel.RawJson(RngStateJson),
                "build_guid", BuildGuid, "game_version", Application.version,
                "platform", Application.platform.ToString()));
            reported = true;
            enabled = false;
        }
    }
}
