using System;
using UnityEngine;

/// <summary>
/// Pins the CONDITIONS a parity episode runs under, so two builds are compared as the same
/// experiment rather than as two different ones that happen to share a seed.
///
/// WHY THIS EXISTS. The first end-to-end run of the harness reported an RNG divergence at the
/// first advanced round — the exact signature of the D1 skipped-draw bug. It was not D1. One
/// build had downloaded a 67-object map from a map server running on the machine while the
/// other kept its built-in scene layout, and the diff had no way to know. A harness that can
/// produce a confident wrong answer is worse than no harness, so the conditions are now forced
/// explicitly and recorded in every trace line (see <see cref="ParityProbe"/>).
///
/// WHY IT CANNOT BE DONE FROM OUTSIDE THE PLAYER. The map URL is a SERIALIZED SCENE FIELD
/// (`GameConfigLoader.mapConfigServerUrl`), and the two branches' MainScene disagree on it:
/// upstream points at a map server, ours is blank. The config.json override that looks like the
/// way in is inert in a desktop build — both branches read it with `UnityWebRequest.Get` on a
/// bare filesystem path with no `file://` scheme, which fails on macOS, so the scene value
/// always wins. Editing the scene is not an option either: MainScene is LFS-tracked and merges
/// whole-file. Setting the field at runtime is what is left.
///
/// TIMING IS WHY IT WORKS. `GameConfigLoader` kicks off both its sheet and map coroutines in
/// `Start()`, and `AfterSceneLoad` runs after every `Awake` but before any `Start`. So this
/// lands in the one window where the field can still be changed before it is read.
///
/// WHAT IT DELIBERATELY DOES NOT DO. It does not touch `GameDataManager`. That object decides
/// whether to read the parameter sheet inside a coroutine started from `Awake`, which has
/// already run by the time anything here executes — and more importantly, the branches'
/// disagreement there (ledger D7) is a real behavioural divergence, not an environment
/// artifact. Papering over it would hide the finding. It stays visible, and version 2 reverts
/// it properly.
///
/// Off unless asked for: `-parity-hermetic`, or ARC_PARITY_HERMETIC=1.
/// </summary>
public static class ParityEnv
{
    /// <summary>True when this episode had its conditions pinned.</summary>
    public static bool Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        if (!Wanted()) return;
        Active = true;

        int pinned = 0;
        foreach (var loader in UnityEngine.Object.FindObjectsOfType<GameConfigLoader>())
        {
            if (loader == null) continue;
            // Blank, not redirected. An unreachable URL still costs a request, a timeout and a
            // warning, and the two builds would wait different amounts of wall-clock for it.
            loader.mapConfigServerUrl = "";
            // And the parameter sheet. Ours fetches a live Google Sheet, which makes the
            // parameters a property of whatever someone last typed into a spreadsheet rather
            // than of the commit under test — two runs a day apart are then not the same
            // experiment. Blanking the URL drops ours onto StreamingAssets/game_param_config.csv,
            // which ships with the build. This does NOT hide ledger D7: upstream still reads
            // hardcoded defaults and ours still reads a sheet, and the traces still say so.
            loader.googleSheetsCsvUrl = "";
            pinned++;
        }

        Debug.Log($"[ParityEnv] hermetic mode on {pinned} loader(s): map pinned to the built-in "
                + "scene layout, parameters pinned to files shipped in the build. Nothing is "
                + "fetched over the network for this episode.");
    }

    static bool Wanted()
    {
        try
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (a == "-parity-hermetic" || a == "--parity-hermetic") return true;
            string env = Environment.GetEnvironmentVariable("ARC_PARITY_HERMETIC");
            if (!string.IsNullOrEmpty(env) && env != "0") return true;
        }
        catch (Exception) { }
        return false;
    }
}
