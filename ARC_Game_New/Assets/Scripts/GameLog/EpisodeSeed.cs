using System;
using UnityEngine;

/// <summary>
/// Controls the random seed every episode starts from — the companion to
/// <see cref="EpisodeReproLog"/>, which records the seed but never sets it itself.
/// Together they close the loop: the log tells you which episode a participant played, this
/// lets you play it again.
///
/// EVERY EPISODE IS SEEDED. An explicit seed (-seed / ARC_SEED / ?seed=) is used if supplied;
/// otherwise one is generated and logged with source "auto". Replayability is a core
/// requirement for this build, so there is no unseeded path left — the alternative (leaving a
/// run's starting state unrecorded) would make that run's RNG stream permanently
/// unreproducible, no matter what else the log captures.
///
/// TIMING IS THE SUBSTANCE, AND IT IS WHY THIS IS NOT INSIDE EpisodeReproLog.
/// The flood layout and the opening task roll are decided in MainScene's Awake/Start chain, so
/// the seed has to be set before the scene loads — the same argument EpisodeReproLog makes for
/// reading the state there. But several things already hook BeforeSceneLoad (EpisodeReproLog's
/// own capture, GymServerManager's bootstrap), and Unity does not define the order among them.
/// SubsystemRegistration runs strictly EARLIER than every BeforeSceneLoad hook, so seeding here
/// is guaranteed to land before anything reads or consumes the stream. Putting it on
/// BeforeSceneLoad would work most of the time and silently log a pre-seed state the rest.
///
/// TWO GENERATORS, NOT ONE. UnityEngine.Random drives the flood, weather, tasks, client stays
/// and deliveries. GameConfigLoader separately uses System.Random for one coin flip, and a
/// System.Random constructed with no argument is seeded from the clock — so Random.InitState
/// alone does NOT make an episode reproducible. <see cref="NextSystemRandom"/> hands out a
/// deterministic System.Random derived from the same seed for that use, WITHOUT drawing from
/// the Unity stream (which would shift every downstream roll and change the game for everyone
/// running unseeded).
/// </summary>
public static class EpisodeSeed
{
    /// <summary>The seed in effect, or -1 when none was supplied and startup was left alone.</summary>
    public static int Seed { get; private set; } = -1;

    /// <summary>True when this episode was started from an explicit seed.</summary>
    public static bool IsSeeded => Seed >= 0;

    /// <summary>How the seed arrived, for the log ("-seed", "ARC_SEED", "url", "none").</summary>
    public static string Source { get; private set; } = "none";

    static int systemRandomCounter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Apply()
    {
        int seed = Resolve(out string source);
        if (seed < 0)
        {
            // Replayability is now a core requirement (not just an opt-in diagnostic), so every
            // episode gets a seed — auto-generated when nothing was explicitly supplied, rather
            // than leaving the run on Unity's own non-reproducible default state. This also
            // cascades to NextSystemRandom() and ParityPolicy's tie-break stream, both of which
            // already check IsSeeded and only fall back to clock-seeded randomness when it's false.
            seed = unchecked(Guid.NewGuid().GetHashCode() & 0x7FFFFFFF);
            source = "auto";
        }

        Seed = seed;
        Source = source;
        UnityEngine.Random.InitState(seed);
        Debug.Log($"[EpisodeSeed] seeded UnityEngine.Random with {seed} (from {source}). "
                + "Episode should replay identically on this build and parameter sheet.");
    }

    /// <summary>
    /// A System.Random for code that needs one (GameConfigLoader's advisory/emergency split).
    /// Deterministic when the episode is seeded, clock-seeded otherwise — so unseeded runs keep
    /// today's behaviour exactly. Each call gets a distinct stream, so two call sites cannot
    /// accidentally share a sequence.
    /// </summary>
    public static System.Random NextSystemRandom()
    {
        if (!IsSeeded) return new System.Random();
        unchecked { return new System.Random(Seed * 397 + (systemRandomCounter++)); }
    }

    static int Resolve(out string source)
    {
        source = "none";

        // Command line / env: how the headless gym and batch runs pass a seed.
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if ((args[i] == "-seed" || args[i] == "--seed") && int.TryParse(args[i + 1], out int s))
                { source = "-seed"; return s; }

            string env = Environment.GetEnvironmentVariable("ARC_SEED");
            if (!string.IsNullOrEmpty(env) && int.TryParse(env, out int e))
            { source = "ARC_SEED"; return e; }
        }
        catch (Exception)
        {
            // WebGL has neither a command line nor environment variables, and asking for them
            // can throw rather than return empty. Fall through to the URL, which is the only
            // channel a browser build actually has.
        }

        // WebGL: ?seed=1234 on the page URL. absoluteURL is empty outside a browser build.
        try
        {
            string url = Application.absoluteURL;
            if (!string.IsNullOrEmpty(url))
            {
                int q = url.IndexOf('?');
                if (q >= 0)
                    foreach (string pair in url.Substring(q + 1).Split('&'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq <= 0) continue;
                        if (pair.Substring(0, eq).Trim().ToLowerInvariant() != "seed") continue;
                        if (int.TryParse(pair.Substring(eq + 1).Trim(), out int u))
                        { source = "url"; return u; }
                    }
            }
        }
        catch (Exception) { }

        return -1;
    }
}
