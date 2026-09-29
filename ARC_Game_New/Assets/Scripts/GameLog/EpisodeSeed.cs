using System;
using UnityEngine;

/// <summary>
/// The random seed every episode starts from — the companion to <see cref="EpisodeReproLog"/>,
/// which records the resulting RNG state. Together they close the loop: the log tells you which
/// episode a player had, and the seed lets you set it up again.
///
/// EVERY RUN IS SEEDED. A seed passed in (-seed, ARC_SEED, ?seed=) is used as given. Otherwise
/// one is GENERATED at startup and recorded with source "auto". This used to leave unseeded
/// runs alone (seed -1, source "none"), which meant a human study run could never be tied to a
/// seed. Generating one does not change what players experience: Unity already seeds its
/// generator unpredictably at startup, so the scenario is just as random. The difference is that
/// the seed is now known and logged.
///
/// testMode (config.json) IGNORES ?seed=, so a participant cannot pick their scenario by editing
/// the link. config.json can only be read asynchronously, after this runs, so the URL seed is
/// applied first and then replaced with a fresh auto seed as soon as the file says testMode.
/// That happens on the title screen, before any scene that draws from the generator, and the
/// recorded start state is re-captured to match.
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
    /// <summary>The seed in effect (-1 only before startup has run).</summary>
    public static int Seed { get; private set; } = -1;

    /// <summary>True once a seed has been applied (always, after startup).</summary>
    public static bool IsSeeded => Seed >= 0;

    /// <summary>How the seed arrived, for the log: "-seed", "ARC_SEED", "url", "auto", or
    /// "auto (url seed ignored: testMode)".</summary>
    public static string Source { get; private set; } = "none";

    static int systemRandomCounter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Apply()
    {
        int seed = Resolve(out string source);
        if (seed < 0) { seed = NewAutoSeed(); source = "auto"; }
        Use(seed, source);

        // testMode forbids choosing the scenario from the URL. Only decidable once config.json
        // has loaded (asynchronously, on the title screen, before any scene draws randomness).
        if (source == "url")
            RuntimeConfig.WhenLoaded(() =>
            {
                if (!RuntimeConfig.TestMode) return;
                Use(NewAutoSeed(), "auto (url seed ignored: testMode)");
                EpisodeReproLog.Recapture();
            });
    }

    static void Use(int seed, string source)
    {
        Seed = seed;
        Source = source;
        systemRandomCounter = 0;
        UnityEngine.Random.InitState(seed);
        Debug.Log($"[EpisodeSeed] seeded UnityEngine.Random with {seed} (from {source}).");
    }

    /// <summary>A fresh non-negative seed that does not draw from UnityEngine.Random.</summary>
    static int NewAutoSeed() => Guid.NewGuid().GetHashCode() & 0x7FFFFFFF;

    /// <summary>
    /// A System.Random for code that needs one (GameConfigLoader's advisory/emergency split).
    /// Derived from the episode seed, so it is deterministic for every run (the clock-seeded
    /// fallback remains only for code that runs before startup seeding). Each call gets a
    /// distinct stream, so two call sites cannot accidentally share a sequence.
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
